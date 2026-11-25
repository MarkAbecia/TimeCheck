using System.Globalization;
using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TimeCheck.Application.Abstractions;
using TimeCheck.Application.Common;
using TimeCheck.Domain.Entities;
using TimeCheck.Domain.Enums;

namespace TimeCheck.Application.Features.Reports;

// ---------------------------------------------------------------- The shape every report takes (§6.8)

/// <summary>What the person asked for. Unset dates mean the last 30 days; the period ends today at the latest.</summary>
public sealed record ReportFilters(DateOnly? From = null, DateOnly? To = null, Guid? GroupId = null, Guid? EventTypeId = null, EventCategory? Category = null);

public static class ColumnKind
{
    public const string Text = "text";
    public const string Number = "number";
    public const string Percent = "percent";
    public const string Date = "date";
}

public sealed record ReportColumn(string Key, string Label, string Kind);

/// <summary>One table. Cells are strings, numbers, dates or null ("—"); percents are 0–100.</summary>
public sealed record ReportSection(string Key, string Title, IReadOnlyList<ReportColumn> Columns, IReadOnlyList<IReadOnlyList<object?>> Rows, string? Note = null);

public sealed record ChartBar(string Label, decimal Value);

/// <summary>Unit is "percent" or "count".</summary>
public sealed record ReportChart(string Title, string Unit, IReadOnlyList<ChartBar> Bars);

/// <summary>
/// A finished report: the screen, the CSV, the XLSX and the PDF are all drawn from this, so they always agree.
/// Scope lists the filters in words ("1 Nov 2026 – 30 Nov 2026", "Group: Faith").
/// </summary>
public sealed record ReportDto(
    string Type, string Title, IReadOnlyList<string> Scope, DateOnly From, DateOnly To,
    IReadOnlyList<ReportSection> Sections, ReportChart? Chart, DateTimeOffset GeneratedAt);

public sealed record ReportInfo(string Type, string Title, string Summary, bool ForLeaders, bool UsesPeriod, bool UsesEventFilter);

public static class ReportCatalog
{
    public static readonly IReadOnlyList<ReportInfo> All =
    [
        new("attendance", "Attendance", "Attended out of expected, per event type and per member, with the trend.", true, true, true),
        new("member-status", "Member status", "Each member's status, last attendance, devotion streak and flags.", true, false, false),
        new("members", "Members", "Who's in the group, counted by gender, grade level, age group and status.", true, false, false),
        new("groups", "Group monitoring", "Size, activity, attendance, devotions and invites for each group.", false, true, true),
        new("cells", "Cell status", "Status mix, growth and attendance trend for each cell.", false, true, false),
        new("hierarchy", "Leaders and members", "Every group with its leader and members.", false, false, false),
    ];

    public static ReportInfo? Find(string type) => All.FirstOrDefault(r => r.Type == type);

    /// <summary>Admins run every report; leaders run the ones about their own group (§4 permissions).</summary>
    public static bool CanRun(CurrentUserInfo me, ReportInfo r) => me.IsAdmin() || (r.ForLeaders && me.Role == UserRole.LEADER);
}

public static class ReportJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
}

public sealed class ReportFiltersValidator : AbstractValidator<ReportFilters>
{
    public ReportFiltersValidator()
    {
        RuleFor(x => x.Category).IsInEnum();
        RuleFor(x => x).Must(x => x.From is null || x.To is null || (x.To >= x.From && x.To.Value.DayNumber - x.From.Value.DayNumber <= 366))
            .WithMessage("Choose a period of at most a year that ends after it starts.").OverridePropertyName("to");
    }
}

// ---------------------------------------------------------------- Requests

public sealed record ListReports : IRequest<IReadOnlyList<ReportInfo>>;

public sealed class ListReportsHandler(ICurrentUser current) : IRequestHandler<ListReports, IReadOnlyList<ReportInfo>>
{
    public Task<IReadOnlyList<ReportInfo>> Handle(ListReports request, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ReportInfo>>(ReportCatalog.All.Where(r => ReportCatalog.CanRun(current.Info, r)).ToList());
}

public sealed record RunReport(string Type, ReportFilters Filters) : IRequest<ReportDto>;

public sealed class RunReportValidator : AbstractValidator<RunReport>
{
    public RunReportValidator() => RuleFor(x => x.Filters).SetValidator(new ReportFiltersValidator());
}

public sealed class RunReportHandler(ReportBuilder builder, ICurrentUser current) : IRequestHandler<RunReport, ReportDto>
{
    public Task<ReportDto> Handle(RunReport r, CancellationToken ct) => builder.RunCachedAsync(current.Info, r.Type, r.Filters, ct);
}

// ---------------------------------------------------------------- Building

/// <summary>The resolved request: who may see what, over which days. Groups/EventTypes null means "all".</summary>
public sealed record ReportPlan(
    ReportInfo Info, Guid ChurchId, Guid[]? Groups, Guid[]? EventTypes, DateOnly From, DateOnly To, DateOnly Today,
    string ChurchSettings, IReadOnlyList<string> Scope)
{
    public string CacheKey => string.Join('|', Info.Type, From.DayNumber, To.DayNumber, Today.DayNumber,
        Groups is null ? "all" : string.Join(',', Groups.Order()), EventTypes is null ? "all" : string.Join(',', EventTypes.Order()));
}

/// <summary>Attended = present or late; counted = expected minus excused; unmarked counts as absent (§5.2).</summary>
internal sealed record Tally(int Total, int Present, int Late, int Excused, int Absent, int Unmarked)
{
    public static readonly Tally Zero = new(0, 0, 0, 0, 0, 0);
    public int Attended => Present + Late;
    public int Counted => Total - Excused;
    public decimal? Rate => Pct(Attended, Counted);
    public Tally Plus(Tally o) => new(Total + o.Total, Present + o.Present, Late + o.Late, Excused + o.Excused, Absent + o.Absent, Unmarked + o.Unmarked);
    public static decimal? Pct(int part, int whole) => whole == 0 ? null : Math.Round(100m * part / whole, 1, MidpointRounding.AwayFromZero);
}

/// <summary>
/// Builds the §6.8 reports from the attendance_facts view (one definition of "expected", so totals equal the raw rows) and the
/// member tables. Every query names the church: the Worker runs these with no tenant filter.
/// </summary>
public sealed class ReportBuilder(IAppDbContext db, IReportCache cache, TimeProvider clock)
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly int[] DefaultAgeBrackets = [12, 17, 24, 35];

    public async Task<ReportDto> RunCachedAsync(CurrentUserInfo me, string type, ReportFilters f, CancellationToken ct)
    {
        var plan = await PlanAsync(me, type, f, ct);
        var bytes = await cache.GetOrCreateAsync(plan.ChurchId, plan.CacheKey,
            async c => JsonSerializer.SerializeToUtf8Bytes(await BuildAsync(plan, c), ReportJson.Options), ct);
        return JsonSerializer.Deserialize<ReportDto>(bytes, ReportJson.Options)!;
    }

    public async Task<ReportDto> RunAsync(CurrentUserInfo me, string type, ReportFilters f, CancellationToken ct) =>
        await BuildAsync(await PlanAsync(me, type, f, ct), ct);

    /// <summary>Checks access and fills in defaults. Someone else's group or an unknown id is 404, a report above your role 403.</summary>
    public async Task<ReportPlan> PlanAsync(CurrentUserInfo me, string type, ReportFilters f, CancellationToken ct)
    {
        var info = ReportCatalog.Find(type) ?? throw new NotFoundException();
        if (!ReportCatalog.CanRun(me, info)) throw new ForbiddenException("This report is for admins.");

        var church = await db.Churches.AsNoTracking().FirstAsync(c => c.Id == me.ChurchId, ct);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), church.GetTimeZone()).DateTime);
        var to = f.To is { } t && t < today ? t : today;
        var from = f.From ?? to.AddDays(-29);
        if (from > to) from = to;

        var scope = new List<string>();
        scope.Add(info.UsesPeriod ? $"{Day(from)} – {Day(to)}" : $"As of {Day(today)}");

        Guid[]? groups;
        if (f.GroupId is { } g)
        {
            if (!me.IsAdmin() && !me.LedGroupIds.Contains(g)) throw new NotFoundException();
            var name = await Groups.Where(x => x.Id == g && x.ChurchId == me.ChurchId).Select(x => x.Name).FirstOrDefaultAsync(ct)
                ?? throw new NotFoundException();
            groups = [g];
            scope.Add($"Group: {name}");
        }
        else if (me.IsAdmin())
        {
            groups = null;
            scope.Add("All groups");
        }
        else
        {
            groups = [.. me.LedGroupIds];
            var names = await Groups.Where(x => x.ChurchId == me.ChurchId && groups.Contains(x.Id)).OrderBy(x => x.Name).Select(x => x.Name).ToListAsync(ct);
            scope.Add(names.Count == 1 ? $"Group: {names[0]}" : $"Your groups: {string.Join(", ", names)}");
        }

        Guid[]? types = null;
        if (info.UsesEventFilter)
        {
            if (f.EventTypeId is { } et)
            {
                var name = await EventTypes.Where(x => x.Id == et && x.ChurchId == me.ChurchId).Select(x => x.Name).FirstOrDefaultAsync(ct)
                    ?? throw new NotFoundException();
                types = [et];
                scope.Add($"Event type: {name}");
            }
            else if (f.Category is { } cat)
            {
                types = [.. await EventTypes.Where(x => x.ChurchId == me.ChurchId && x.Category == cat).Select(x => x.Id).ToListAsync(ct)];
                scope.Add($"Category: {CategoryLabel(cat)}");
            }
            else scope.Add("All event types");
        }

        return new ReportPlan(info, me.ChurchId, groups, types, from, to, today, church.Settings, scope);
    }

    public async Task<ReportDto> BuildAsync(ReportPlan p, CancellationToken ct)
    {
        var (sections, chart) = p.Info.Type switch
        {
            "attendance" => await AttendanceAsync(p, ct),
            "members" => await MembersAsync(p, ct),
            "member-status" => await MemberStatusAsync(p, ct),
            "groups" => await GroupMonitoringAsync(p, ct),
            "cells" => await CellStatusAsync(p, ct),
            "hierarchy" => await HierarchyAsync(p, ct),
            _ => throw new NotFoundException(),
        };
        return new ReportDto(p.Info.Type, p.Info.Title, p.Scope, p.From, p.To, sections, chart, clock.GetUtcNow());
    }

    // ------------------------------------------------------------ Attendance

    private async Task<(IReadOnlyList<ReportSection>, ReportChart?)> AttendanceAsync(ReportPlan p, CancellationToken ct)
    {
        var types = await TypeNamesAsync(p, ct);
        var groups = await GroupNamesAsync(p, ct);

        // Per event: gives the per-type totals, how many events, and the trend without a second pass over the rows.
        var perEvent = await TallyByAsync(Facts(p), f => new { f.EventTypeId, f.EventId, f.OccursOn }, ct);
        var byType = perEvent.GroupBy(x => x.Key.EventTypeId)
            .Select(g => (Type: types.GetValueOrDefault(g.Key, "Unknown"), Events: g.Count(), T: Sum(g.Select(x => x.T))))
            .OrderBy(x => x.Type, StringComparer.OrdinalIgnoreCase)
            .Select(x => Row(x.Type, x.Events, x.T.Total, x.T.Present, x.T.Late, x.T.Excused, x.T.Absent, x.T.Unmarked, x.T.Rate))
            .ToList();
        var total = Sum(perEvent.Select(x => x.T));
        if (byType.Count > 1)
            byType.Add(Row("All event types", perEvent.Count, total.Total, total.Present, total.Late, total.Excused, total.Absent, total.Unmarked, total.Rate));

        var perPerson = await TallyByAsync(Facts(p), f => new { f.UserId, f.GroupId }, ct);
        var names = await NamesAsync(p.ChurchId, perPerson.Select(x => x.Key.UserId), ct);
        var byMember = perPerson
            .Select(x => (Name: names.GetValueOrDefault(x.Key.UserId, "Removed member"), Group: GroupName(groups, x.Key.GroupId), x.T))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Group, StringComparer.OrdinalIgnoreCase)
            .Select(x => Row(x.Name, x.Group, x.T.Attended, x.T.Counted, x.T.Rate, x.T.Late, x.T.Excused, x.T.Unmarked))
            .ToList();

        var weekly = p.To.DayNumber - p.From.DayNumber <= 92;
        var trend = perEvent.GroupBy(x => weekly ? WeekStart(x.Key.OccursOn) : new DateOnly(x.Key.OccursOn.Year, x.Key.OccursOn.Month, 1))
            .OrderBy(g => g.Key)
            .Select(g => (g.Key, T: Sum(g.Select(x => x.T))))
            .Where(x => x.T.Rate is not null)
            .Select(x => new ChartBar(weekly ? x.Key.ToString("d MMM", Inv) : x.Key.ToString("MMM yyyy", Inv), x.T.Rate!.Value))
            .ToList();

        const string rule = "Rate is present or late out of everyone expected, not counting excused. Unmarked counts as absent.";
        return ([
            new ReportSection("byType", "By event type",
                [Text("type", "Event type"), Num("events", "Events"), Num("expected", "Expected"), Num("present", "Present"), Num("late", "Late"),
                 Num("excused", "Excused"), Num("absent", "Absent"), Num("unmarked", "Unmarked"), Pct("rate", "Rate")], byType, rule),
            new ReportSection("byMember", "By member",
                [Text("member", "Member"), Text("group", "Group"), Num("attended", "Attended"), Num("counted", "Counted"), Pct("rate", "Rate"),
                 Num("late", "Late"), Num("excused", "Excused"), Num("unmarked", "Unmarked")], byMember,
                "Someone who moved groups in this period has a row for each group."),
        ], new ReportChart(weekly ? "Attendance rate by week (week starting)" : "Attendance rate by month", "percent", trend));
    }

    // ------------------------------------------------------------ Members (demographics)

    private async Task<(IReadOnlyList<ReportSection>, ReportChart?)> MembersAsync(ReportPlan p, CancellationToken ct)
    {
        var people = await PeopleAsync(p, ct);
        var groups = await GroupNamesAsync(p, ct);
        var brackets = AgeBrackets(p.ChurchSettings);
        string Age(DateOnly birthdate) => AgeBracket(AppUser.AgeOn(birthdate, p.Today), brackets);

        var rows = people.Select(x => Row(x.Name, GroupName(groups, x.GroupId), GenderLabel(x.Gender), x.Grade ?? "Not set",
            Age(x.Birthdate), StatusLabel(x.Status), x.JoinedOn)).ToList();

        var counts = new List<IReadOnlyList<object?>>();
        void Count(string breakdown, IEnumerable<string> values, IEnumerable<string>? order = null)
        {
            var tally = values.GroupBy(v => v).ToDictionary(g => g.Key, g => g.Count());
            foreach (var key in (order ?? tally.Keys.Order(StringComparer.OrdinalIgnoreCase)).Where(tally.ContainsKey))
                counts.Add(Row(breakdown, key, tally[key], Tally.Pct(tally[key], people.Count)));
        }
        var ageOrder = AgeLabels(brackets);
        Count("Gender", people.Select(x => GenderLabel(x.Gender)));
        Count("Grade level", people.Select(x => x.Grade ?? "Not set"));
        Count("Age group", people.Select(x => Age(x.Birthdate)), ageOrder);
        Count("Status", people.Select(x => StatusLabel(x.Status)), Enum.GetValues<MemberStatus>().Select(StatusLabel));

        var byAge = people.GroupBy(x => Age(x.Birthdate)).ToDictionary(g => g.Key, g => g.Count());
        return ([
            new ReportSection("counts", "Counts",
                [Text("breakdown", "Breakdown"), Text("value", "Value"), Num("members", "Members"), Pct("share", "Share")], counts,
                $"{people.Count} active members. Age groups follow the church's settings."),
            new ReportSection("members", "Members",
                [Text("member", "Member"), Text("group", "Group"), Text("gender", "Gender"), Text("grade", "Grade level"), Text("age", "Age group"),
                 Text("status", "Status"), Date("joined", "Joined")], rows),
        ], new ReportChart("Members by age group", "count", ageOrder.Select(a => new ChartBar(a, byAge.GetValueOrDefault(a))).ToList()));
    }

    // ------------------------------------------------------------ Member status (leader's view of their group)

    private async Task<(IReadOnlyList<ReportSection>, ReportChart?)> MemberStatusAsync(ReportPlan p, CancellationToken ct)
    {
        var people = await PeopleAsync(p, ct);
        var groups = await GroupNamesAsync(p, ct);
        var ids = people.Select(x => x.Id).ToArray();

        var lastAttended = await Attendance
            .Where(a => a.ChurchId == p.ChurchId && ids.Contains(a.UserId) && (a.Status == AttendanceStatus.PRESENT || a.Status == AttendanceStatus.LATE))
            .Join(Events, a => a.EventId, e => e.Id, (a, e) => new { a.UserId, e.OccursOn })
            .GroupBy(x => x.UserId).Select(g => new { g.Key, Last = g.Max(x => x.OccursOn) })
            .ToDictionaryAsync(x => x.Key, x => x.Last, ct);

        // The last 30 days before today (today's events may not be marked yet), newest first.
        var since = p.Today.AddDays(-30);
        var recent = (await AttendanceFacts
                .Where(f => f.ChurchId == p.ChurchId && ids.Contains(f.UserId) && f.OccursOn >= since && f.OccursOn < p.Today && f.Status != AttendanceStatus.EXCUSED)
                .Select(f => new { f.UserId, f.OccursOn, f.Status })
                .ToListAsync(ct))
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.OccursOn).Take(3).ToList());

        var devotionsSince = p.Today.AddDays(-180);
        var devotions = (await Devotions
                .Where(d => d.ChurchId == p.ChurchId && ids.Contains(d.UserId) && d.DevotionDate >= devotionsSince && d.DevotionDate <= p.Today)
                .Select(d => new { d.UserId, d.DevotionDate })
                .ToListAsync(ct))
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.DevotionDate).ToHashSet());

        var rows = people.Select(x =>
        {
            var days = devotions.GetValueOrDefault(x.Id) ?? [];
            var flags = new List<string>();
            if (recent.TryGetValue(x.Id, out var last3) && last3.Count == 3 && last3.All(r => r.Status is not (AttendanceStatus.PRESENT or AttendanceStatus.LATE)))
                flags.Add("Missed the last 3");
            if (!Enumerable.Range(0, 7).Any(i => days.Contains(p.Today.AddDays(-i)))) flags.Add("No devotion in 7 days");
            return Row(x.Name, GroupName(groups, x.GroupId), StatusLabel(x.Status),
                lastAttended.TryGetValue(x.Id, out var l) ? l : null, Streak(days, p.Today), flags.Count == 0 ? null : string.Join("; ", flags));
        }).ToList();

        var byStatus = people.GroupBy(x => x.Status).ToDictionary(g => g.Key, g => g.Count());
        return ([
            new ReportSection("members", "Members",
                [Text("member", "Member"), Text("group", "Group"), Text("status", "Status"), Date("lastAttended", "Last attended"),
                 Num("streak", "Devotion streak (days)"), Text("flags", "Flags")], rows,
                "Missed the last 3: the three most recent counted events in the last 30 days were absences or unmarked."),
        ], new ReportChart("Members by status", "count",
            Enum.GetValues<MemberStatus>().Select(s => new ChartBar(StatusLabel(s), byStatus.GetValueOrDefault(s))).ToList()));
    }

    /// <summary>Days in a row with a devotion, up to today. Today not done yet doesn't break it.</summary>
    public static int Streak(HashSet<DateOnly> days, DateOnly today)
    {
        var d = days.Contains(today) ? today : today.AddDays(-1);
        var n = 0;
        while (days.Contains(d)) { n++; d = d.AddDays(-1); }
        return n;
    }

    // ------------------------------------------------------------ Group monitoring (admin)

    private async Task<(IReadOnlyList<ReportSection>, ReportChart?)> GroupMonitoringAsync(ReportPlan p, CancellationToken ct)
    {
        var groups = await ActiveGroupsAsync(p, ct);
        var leaders = await NamesAsync(p.ChurchId, groups.Where(g => g.LeaderId != null).Select(g => g.LeaderId!.Value), ct);
        var types = await TypeNamesAsync(p, ct);
        var current = CurrentMemberships(p.ChurchId);
        var size = await current.GroupBy(m => m.GroupId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);

        var perPerson = await TallyByAsync(Facts(p).Where(f => f.GroupId != null), f => new { f.GroupId, f.UserId }, ct);
        var perEvent = await TallyByAsync(Facts(p).Where(f => f.GroupId != null), f => new { f.GroupId, f.EventTypeId, f.EventId }, ct);

        var days = p.To.DayNumber - p.From.DayNumber + 1;
        var devotions = await Devotions
            .Where(d => d.ChurchId == p.ChurchId && d.DevotionDate >= p.From && d.DevotionDate <= p.To)
            .Join(current, d => d.UserId, m => m.UserId, (d, m) => m.GroupId)
            .GroupBy(g => g).Select(g => new { g.Key, N = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.N, ct);
        var invites = await Invites
            .Where(i => i.ChurchId == p.ChurchId)
            .Join(Events.Where(e => e.OccursOn >= p.From && e.OccursOn <= p.To), i => i.EventId, e => e.Id, (i, e) => i)
            .Join(current, i => i.InviterId, m => m.UserId, (i, m) => new { m.GroupId, i.Status })
            .GroupBy(x => x.GroupId).Select(g => new { g.Key, Total = g.Count(), Came = g.Count(x => x.Status == InviteStatus.CAME) })
            .ToDictionaryAsync(x => x.Key, x => (x.Total, x.Came), ct);

        var rows = new List<IReadOnlyList<object?>>();
        var bars = new List<ChartBar>();
        foreach (var g in groups)
        {
            var n = size.GetValueOrDefault(g.Id);
            var people = perPerson.Where(x => x.Key.GroupId == g.Id).ToList();
            var t = Sum(people.Select(x => x.T));
            var active = people.Count(x => x.T.Attended > 0);
            var unmarkedEvents = perEvent.Count(x => x.Key.GroupId == g.Id && x.T.Unmarked > 0);
            var (inv, came) = invites.GetValueOrDefault(g.Id);
            rows.Add(Row(g.Name, g.LeaderId is { } l ? leaders.GetValueOrDefault(l) : null, n, Tally.Pct(active, n), t.Rate,
                Tally.Pct(devotions.GetValueOrDefault(g.Id), n * days * 2), inv, came, unmarkedEvents));
            if (t.Rate is { } rate) bars.Add(new ChartBar(g.Name, rate));
        }

        var byType = perEvent.GroupBy(x => (x.Key.GroupId, x.Key.EventTypeId))
            .Select(gr => (Group: groups.FirstOrDefault(g => g.Id == gr.Key.GroupId)?.Name, Type: types.GetValueOrDefault(gr.Key.EventTypeId, "Unknown"),
                Events: gr.Count(), T: Sum(gr.Select(x => x.T))))
            .Where(x => x.Group != null)
            .OrderBy(x => x.Group, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Type, StringComparer.OrdinalIgnoreCase)
            .Select(x => Row(x.Group, x.Type, x.Events, x.T.Attended, x.T.Counted, x.T.Rate))
            .ToList();

        return ([
            new ReportSection("groups", "By group",
                [Text("group", "Group"), Text("leader", "Leader"), Num("members", "Members"), Pct("active", "Active"), Pct("attendance", "Attendance"),
                 Pct("devotions", "Devotions sent"), Num("invites", "Guests invited"), Num("came", "Guests who came"), Num("unmarked", "Events not fully marked")],
                rows,
                "Active: attended at least once in the period. Devotions sent: out of a Day and a Night devotion per member per day."),
            new ReportSection("byType", "Average attendance by event type",
                [Text("group", "Group"), Text("type", "Event type"), Num("events", "Events"), Num("attended", "Attended"), Num("counted", "Counted"),
                 Pct("rate", "Rate")], byType),
        ], new ReportChart("Attendance rate by group", "percent", bars));
    }

    // ------------------------------------------------------------ Cell status (admin)

    private async Task<(IReadOnlyList<ReportSection>, ReportChart?)> CellStatusAsync(ReportPlan p, CancellationToken ct)
    {
        var groups = await ActiveGroupsAsync(p, ct);
        var leaders = await NamesAsync(p.ChurchId, groups.Where(g => g.LeaderId != null).Select(g => g.LeaderId!.Value), ct);
        var memberships = Memberships.Where(m => m.ChurchId == p.ChurchId);
        var atStart = await memberships.Where(m => m.JoinedOn <= p.From && (m.LeftOn == null || m.LeftOn > p.From))
            .GroupBy(m => m.GroupId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        var atEnd = await memberships.Where(m => m.JoinedOn <= p.To && (m.LeftOn == null || m.LeftOn > p.To))
            .GroupBy(m => m.GroupId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        var people = await PeopleAsync(p, ct);

        // The trend compares the first half of the period with the second.
        var mid = p.From.AddDays((p.To.DayNumber - p.From.DayNumber + 1) / 2);
        var perDay = await TallyByAsync(Facts(p).Where(f => f.GroupId != null), f => new { f.GroupId, f.OccursOn }, ct);

        var rows = groups.Select(g =>
        {
            var start = atStart.GetValueOrDefault(g.Id);
            var end = atEnd.GetValueOrDefault(g.Id);
            var mine = people.Where(x => x.GroupId == g.Id).ToList();
            int S(MemberStatus s) => mine.Count(x => x.Status == s);
            var first = Sum(perDay.Where(x => x.Key.GroupId == g.Id && x.Key.OccursOn < mid).Select(x => x.T)).Rate;
            var second = Sum(perDay.Where(x => x.Key.GroupId == g.Id && x.Key.OccursOn >= mid).Select(x => x.T)).Rate;
            return Row(g.Name, g.LeaderId is { } l ? leaders.GetValueOrDefault(l) : null, start, end, end - start,
                S(MemberStatus.NEW_MEMBER), S(MemberStatus.DISCIPLE), S(MemberStatus.INCONSISTENT), S(MemberStatus.INACTIVE),
                first, second, first is null || second is null ? null : second - first);
        }).ToList();

        var inCells = people.Where(x => x.GroupId != null).ToList();
        return ([
            new ReportSection("cells", "By cell",
                [Text("cell", "Cell"), Text("leader", "Leader"), Num("start", "Members at start"), Num("end", "Members at end"), Num("growth", "Growth"),
                 Num("new", "New members"), Num("disciple", "Disciples"), Num("inconsistent", "Inconsistent"), Num("inactive", "Inactive"),
                 Pct("firstHalf", "Attendance, first half"), Pct("secondHalf", "Attendance, second half"), Num("change", "Change (points)")],
                rows, "Statuses are as of today. The halves split the period in two."),
        ], new ReportChart("Status mix across cells", "count",
            Enum.GetValues<MemberStatus>().Select(s => new ChartBar(StatusLabel(s), inCells.Count(x => x.Status == s))).ToList()));
    }

    // ------------------------------------------------------------ Leaders and members (admin)

    private async Task<(IReadOnlyList<ReportSection>, ReportChart?)> HierarchyAsync(ReportPlan p, CancellationToken ct)
    {
        var groups = await ActiveGroupsAsync(p, ct);
        var all = await Groups.Where(g => g.ChurchId == p.ChurchId)
            .Select(g => new { g.Id, g.Name, g.ParentGroupId }).ToDictionaryAsync(g => g.Id, ct);
        var leaders = await NamesAsync(p.ChurchId, groups.Where(g => g.LeaderId != null).Select(g => g.LeaderId!.Value), ct);
        var people = await PeopleAsync(p, ct);

        string Path(Guid id)
        {
            var names = new List<string>();
            Guid? at = id;
            for (var depth = 0; at is { } a && all.TryGetValue(a, out var g) && depth < 10; depth++, at = g.ParentGroupId) names.Insert(0, g.Name);
            return string.Join(" / ", names);
        }

        var rows = new List<IReadOnlyList<object?>>();
        foreach (var g in groups.Select(g => (g, Path: Path(g.Id))).OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase))
        {
            var leader = g.g.LeaderId is { } l ? leaders.GetValueOrDefault(l) : null;
            var members = people.Where(x => x.GroupId == g.g.Id).OrderBy(x => x.Role.Rank() >= UserRole.LEADER.Rank() ? 0 : 1).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
            if (members.Count == 0) rows.Add(Row(g.Path, leader ?? "No leader", "No members yet", null, null));
            foreach (var m in members) rows.Add(Row(g.Path, leader ?? "No leader", m.Name, RoleLabel(m.Role), StatusLabel(m.Status)));
        }
        if (p.Groups is null)
            foreach (var m in people.Where(x => x.GroupId is null))
                rows.Add(Row("Not in a group", null, m.Name, RoleLabel(m.Role), StatusLabel(m.Status)));

        return ([
            new ReportSection("hierarchy", "Groups, leaders and members",
                [Text("group", "Group"), Text("leader", "Leader"), Text("member", "Member"), Text("role", "Role"), Text("status", "Status")], rows,
                "A group inside another shows as Parent / Group."),
        ], null);
    }

    // ------------------------------------------------------------ Shared queries

    // No global filters: the Worker builds reports too, with no signed-in church. Every query names the church instead, and
    // soft-deleted people and invites are left out here.
    private IQueryable<AppUser> AllUsers => db.Users.IgnoreQueryFilters().AsNoTracking();
    private IQueryable<AppUser> Users => AllUsers.Where(u => u.DeletedAt == null);
    private IQueryable<CellGroup> Groups => db.Groups.IgnoreQueryFilters().AsNoTracking();
    private IQueryable<GroupMembership> Memberships => db.GroupMemberships.IgnoreQueryFilters().AsNoTracking();
    private IQueryable<EventType> EventTypes => db.EventTypes.IgnoreQueryFilters().AsNoTracking();
    private IQueryable<Event> Events => db.Events.IgnoreQueryFilters().AsNoTracking();
    private IQueryable<Attendance> Attendance => db.Attendance.IgnoreQueryFilters().AsNoTracking();
    private IQueryable<Devotion> Devotions => db.Devotions.IgnoreQueryFilters().AsNoTracking();
    private IQueryable<Invite> Invites => db.Invites.IgnoreQueryFilters().AsNoTracking().Where(i => i.DeletedAt == null);
    private IQueryable<AttendanceFact> AttendanceFacts => db.AttendanceFacts.IgnoreQueryFilters().AsNoTracking();

    private IQueryable<AttendanceFact> Facts(ReportPlan p)
    {
        var q = AttendanceFacts.Where(f => f.ChurchId == p.ChurchId && f.OccursOn >= p.From && f.OccursOn <= p.To);
        if (p.Groups is { } gs) q = q.Where(f => f.GroupId != null && gs.Contains(f.GroupId.Value));
        if (p.EventTypes is { } ts) q = q.Where(f => ts.Contains(f.EventTypeId));
        return q;
    }

    /// <summary>One grouped pass over the facts. Counting distinct values here costs seconds on a year of rows, so callers do that in memory.</summary>
    private static async Task<List<(TKey Key, Tally T)>> TallyByAsync<TKey>(IQueryable<AttendanceFact> q, Expression<Func<AttendanceFact, TKey>> key, CancellationToken ct)
    {
        var rows = await q.GroupBy(key).Select(g => new
        {
            g.Key,
            Total = g.Count(),
            Present = g.Count(x => x.Status == AttendanceStatus.PRESENT),
            Late = g.Count(x => x.Status == AttendanceStatus.LATE),
            Excused = g.Count(x => x.Status == AttendanceStatus.EXCUSED),
            Absent = g.Count(x => x.Status == AttendanceStatus.ABSENT),
            Unmarked = g.Count(x => x.Status == AttendanceStatus.UNMARKED),
        }).ToListAsync(ct);
        return rows.Select(r => (r.Key, new Tally(r.Total, r.Present, r.Late, r.Excused, r.Absent, r.Unmarked))).ToList();
    }

    private static Tally Sum(IEnumerable<Tally> tallies) => tallies.Aggregate(Tally.Zero, (a, b) => a.Plus(b));

    private IQueryable<GroupMembership> CurrentMemberships(Guid churchId)
    {
        var users = Users;
        return Memberships.Where(m => m.ChurchId == churchId && m.LeftOn == null && users.Any(u => u.Id == m.UserId && u.DeactivatedAt == null));
    }

    private sealed record Person(Guid Id, string Name, Guid? GroupId, Gender Gender, string? Grade, DateOnly Birthdate, MemberStatus Status, DateOnly JoinedOn, UserRole Role);

    /// <summary>Active people in scope today, by name, with their current group.</summary>
    private async Task<List<Person>> PeopleAsync(ReportPlan p, CancellationToken ct)
    {
        var memberships = Memberships.Where(m => m.ChurchId == p.ChurchId && m.LeftOn == null);
        var q = Users.Where(u => u.ChurchId == p.ChurchId && u.DeactivatedAt == null);
        if (p.Groups is { } gs) q = q.Where(u => memberships.Any(m => m.UserId == u.Id && gs.Contains(m.GroupId)));
        var rows = await q.Select(u => new
        {
            u.Id, u.FirstName, u.LastName, u.Gender, u.GradeLevel, u.Birthdate, u.Status, u.JoinedOn, u.Role,
            GroupId = memberships.Where(m => m.UserId == u.Id).Select(m => (Guid?)m.GroupId).FirstOrDefault(),
        }).ToListAsync(ct);
        return rows.Select(u => new Person(u.Id, $"{u.FirstName} {u.LastName}", u.GroupId, u.Gender, string.IsNullOrWhiteSpace(u.GradeLevel) ? null : u.GradeLevel,
                u.Birthdate, u.Status, u.JoinedOn, u.Role))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private sealed record GroupRow(Guid Id, string Name, Guid? LeaderId);

    private async Task<List<GroupRow>> ActiveGroupsAsync(ReportPlan p, CancellationToken ct)
    {
        var gs = p.Groups;
        return await Groups
            .Where(g => g.ChurchId == p.ChurchId && g.ArchivedAt == null && (gs == null || gs.Contains(g.Id)))
            .OrderBy(g => g.Name).Select(g => new GroupRow(g.Id, g.Name, g.LeaderId)).ToListAsync(ct);
    }

    /// <summary>Names for ids, including people since deactivated or moved. Erased people read "Removed member".</summary>
    private async Task<Dictionary<Guid, string>> NamesAsync(Guid churchId, IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToArray();
        var rows = await AllUsers.Where(u => u.ChurchId == churchId && list.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.DeletedAt }).ToListAsync(ct);
        return rows.ToDictionary(r => r.Id, r => r.DeletedAt != null ? "Removed member" : $"{r.FirstName} {r.LastName}");
    }

    private Task<Dictionary<Guid, string>> GroupNamesAsync(ReportPlan p, CancellationToken ct) =>
        Groups.Where(g => g.ChurchId == p.ChurchId).ToDictionaryAsync(g => g.Id, g => g.Name, ct);

    private Task<Dictionary<Guid, string>> TypeNamesAsync(ReportPlan p, CancellationToken ct) =>
        EventTypes.Where(t => t.ChurchId == p.ChurchId).ToDictionaryAsync(t => t.Id, t => t.Name, ct);

    // ------------------------------------------------------------ Labels and helpers

    private static object?[] Row(params object?[] cells) => cells;
    private static ReportColumn Text(string key, string label) => new(key, label, ColumnKind.Text);
    private static ReportColumn Num(string key, string label) => new(key, label, ColumnKind.Number);
    private static ReportColumn Pct(string key, string label) => new(key, label, ColumnKind.Percent);
    private static ReportColumn Date(string key, string label) => new(key, label, ColumnKind.Date);

    private static string Day(DateOnly d) => d.ToString("d MMM yyyy", Inv);
    private static DateOnly WeekStart(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));
    private static string GroupName(Dictionary<Guid, string> groups, Guid? id) => id is { } g ? groups.GetValueOrDefault(g, "Unknown group") : "Not in a group";

    /// <summary>Upper bounds from churches.settings.reports.ageBrackets (default ≤12, 13–17, 18–24, 25–35, 36+).</summary>
    public static int[] AgeBrackets(string settings)
    {
        try
        {
            if (JsonNode.Parse(settings)?["reports"]?["ageBrackets"] is JsonArray a)
            {
                var bounds = a.Select(x => x!.GetValue<int>()).ToArray();
                if (bounds.Length > 0 && bounds.Zip(bounds.Skip(1)).All(z => z.First < z.Second) && bounds[0] >= 0) return bounds;
            }
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException) { /* fall back to the default */ }
        return DefaultAgeBrackets;
    }

    public static string AgeBracket(int age, int[] bounds)
    {
        for (var i = 0; i < bounds.Length; i++)
            if (age <= bounds[i]) return i == 0 ? $"{bounds[0]} and under" : $"{bounds[i - 1] + 1}–{bounds[i]}";
        return $"{bounds[^1] + 1} and over";
    }

    private static List<string> AgeLabels(int[] bounds) =>
        [.. bounds.Select((b, i) => i == 0 ? $"{b} and under" : $"{bounds[i - 1] + 1}–{b}"), $"{bounds[^1] + 1} and over"];

    internal static string StatusLabel(MemberStatus s) => s switch
    {
        MemberStatus.NEW_MEMBER => "New member",
        MemberStatus.DISCIPLE => "Disciple",
        MemberStatus.INCONSISTENT => "Inconsistent",
        _ => "Inactive",
    };

    private static string GenderLabel(Gender g) => g == Gender.MALE ? "Male" : "Female";

    private static string RoleLabel(UserRole r) => r switch
    {
        UserRole.SUPER_ADMIN => "Super admin",
        UserRole.ADMIN => "Admin",
        UserRole.LEADER => "Leader",
        _ => "Member",
    };

    private static string CategoryLabel(EventCategory c) => c switch
    {
        EventCategory.DAILY => "Daily",
        EventCategory.WEEKLY => "Weekly",
        EventCategory.LEADER_SCHEDULED => "Leader-scheduled",
        _ => "Admin-scheduled",
    };
}
