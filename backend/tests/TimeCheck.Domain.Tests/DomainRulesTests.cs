using AwesomeAssertions;
using TimeCheck.Domain.Common;
using TimeCheck.Domain.Entities;
using TimeCheck.Domain.Enums;

namespace TimeCheck.Domain.Tests;

public class DomainRulesTests
{
    private static readonly TimeZoneInfo Manila = TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila");
    private static readonly DateOnly Today = new(2026, 11, 24); // Tuesday
    private static readonly Guid Church = Guid.NewGuid();

    private static AppUser Adult(UserRole role = UserRole.MEMBER)
    {
        var u = AppUser.Create(Church, "Ana", "Cruz", Gender.FEMALE, new DateOnly(1990, 5, 1), "ana@example.test", Today);
        if (role != UserRole.MEMBER) typeof(AppUser).GetProperty(nameof(AppUser.Role))!.SetValue(u, role);
        return u;
    }

    [Fact]
    public void Morning_prayer_never_generated_on_sunday()
    {
        var mp = EventType.Create(Church, "MORNING_PRAYER", "Morning Prayer", EventCategory.DAILY, EventCadence.DAILY_MON_SAT, EventScope.GROUP);
        var week = Enumerable.Range(0, 7).Select(i => Today.AddDays(i)).ToList();

        week.Where(mp.OccursOn).Should().HaveCount(6).And.NotContain(d => d.DayOfWeek == DayOfWeek.Sunday);
    }

    [Fact]
    public void Morning_prayer_cannot_be_created_on_a_sunday_in_church_time()
    {
        var mp = EventType.Create(Church, "MORNING_PRAYER", "Morning Prayer", EventCategory.DAILY, EventCadence.DAILY_MON_SAT, EventScope.GROUP);
        // 2026-11-28 21:00 UTC = Sunday 05:00 in Manila
        var start = new DateTimeOffset(2026, 11, 28, 21, 0, 0, TimeSpan.Zero);

        var act = () => Event.Create(mp, null, "MP", start, start.AddHours(1), Manila, null);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("event.sunday");
    }

    [Fact]
    public void Weekly_event_occurs_only_on_its_weekday()
    {
        var cc = EventType.Create(Church, "CELL_CELEBRATION", "CC", EventCategory.WEEKLY, EventCadence.WEEKLY, EventScope.CHURCH_WIDE, weekday: 0);
        Enumerable.Range(0, 14).Select(i => Today.AddDays(i)).Where(cc.OccursOn)
            .Should().OnlyContain(d => d.DayOfWeek == DayOfWeek.Sunday).And.HaveCount(2);
    }

    [Theory]
    [InlineData(UserRole.LEADER, 6, true)]
    [InlineData(UserRole.LEADER, 8, false)]
    [InlineData(UserRole.ADMIN, 30, true)]
    public void Attendance_edit_window(UserRole role, int daysAfterEnd, bool allowed)
    {
        var type = EventType.Create(Church, "CELL_MEETING", "Cell Meeting", EventCategory.WEEKLY, EventCadence.WEEKLY, EventScope.GROUP);
        var start = new DateTimeOffset(2026, 11, 24, 11, 0, 0, TimeSpan.Zero);
        var evt = Event.Create(type, Guid.NewGuid(), "Cell", start, start.AddHours(2), Manila, null);

        Attendance.CanEdit(evt, evt.EndsAt.AddDays(daysAfterEnd), 7, role).Should().Be(allowed);
    }

    [Fact]
    public void Second_devotion_upload_replaces_content_and_resets_ai_only_when_changed()
    {
        var d = Devotion.Submit(Church, Guid.NewGuid(), Today, DevotionSlot.DAY, "first", null, new string('a', 64), DateTimeOffset.UtcNow);
        d.RecordAiResult(12, null, "test", "1", DateTimeOffset.UtcNow);

        d.Replace("first", null, new string('a', 64), DateTimeOffset.UtcNow);
        d.AiStatus.Should().Be(AiStatus.DONE);

        d.Replace("second", null, new string('b', 64), DateTimeOffset.UtcNow);
        d.TextContent.Should().Be("second");
        d.AiStatus.Should().Be(AiStatus.PENDING);
        d.AiTextScore.Should().BeNull();
    }

    [Fact]
    public void Devotion_needs_text_or_image_and_respects_max_length()
    {
        var empty = () => Devotion.Submit(Church, Guid.NewGuid(), Today, DevotionSlot.NIGHT, "  ", null, new string('a', 64), DateTimeOffset.UtcNow);
        var tooLong = () => Devotion.Submit(Church, Guid.NewGuid(), Today, DevotionSlot.NIGHT, new string('x', 5001), null, new string('a', 64), DateTimeOffset.UtcNow);

        empty.Should().Throw<DomainException>().Which.Code.Should().Be("devotion.empty");
        tooLong.Should().Throw<DomainException>().Which.Code.Should().Be("devotion.too_long");
    }

    [Fact]
    public void Minor_requires_guardian_consent()
    {
        var birth = Today.AddYears(-15);
        var noGuardian = () => AppUser.Create(Church, "Kid", "Cruz", Gender.MALE, birth, "kid@example.test", Today);
        noGuardian.Should().Throw<DomainException>().Which.Code.Should().Be("guardian.required");

        var kid = AppUser.Create(Church, "Kid", "Cruz", Gender.MALE, birth, "kid@example.test", Today,
            guardian: new AppUser.Guardian("Maria Cruz", "09171234567", DateTimeOffset.UtcNow));
        kid.IsMinor.Should().BeTrue();
        kid.RefreshMinorFlag(Today.AddYears(3)).Should().BeTrue();
        kid.IsMinor.Should().BeFalse();
    }

    [Fact]
    public void Age_counts_birthday_correctly()
    {
        AppUser.AgeOn(new DateOnly(2008, 11, 25), Today).Should().Be(17);
        AppUser.AgeOn(new DateOnly(2008, 11, 24), Today).Should().Be(18);
    }

    [Fact]
    public void Admin_can_promote_member_to_leader_and_change_is_recorded()
    {
        var admin = Adult(UserRole.ADMIN);
        var member = Adult();

        var change = member.ChangeRole(UserRole.LEADER, admin, "Leads a new cell");

        member.Role.Should().Be(UserRole.LEADER);
        change.FromRole.Should().Be(UserRole.MEMBER);
        change.ToRole.Should().Be(UserRole.LEADER);
        change.ChangedBy.Should().Be(admin.Id);
    }

    [Theory]
    [InlineData(UserRole.ADMIN, UserRole.MEMBER, UserRole.ADMIN)]       // only super admin promotes to admin
    [InlineData(UserRole.ADMIN, UserRole.ADMIN, UserRole.LEADER)]       // only super admin demotes an admin
    [InlineData(UserRole.LEADER, UserRole.MEMBER, UserRole.LEADER)]     // leaders can't promote
    public void Role_changes_are_restricted(UserRole actorRole, UserRole from, UserRole to)
    {
        var actor = Adult(actorRole);
        var target = Adult(from);

        var act = () => target.ChangeRole(to, actor, null);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("role.forbidden");
    }

    [Fact]
    public void Cannot_change_own_role()
    {
        var super = Adult(UserRole.SUPER_ADMIN);
        var act = () => super.ChangeRole(UserRole.ADMIN, super, null);
        act.Should().Throw<DomainException>().Which.Code.Should().Be("role.self");
    }

    [Fact]
    public void Status_change_is_recorded()
    {
        var member = Adult();
        var leaderId = Guid.NewGuid();

        var change = member.ChangeStatus(MemberStatus.DISCIPLE, leaderId, null);

        member.Status.Should().Be(MemberStatus.DISCIPLE);
        change.FromStatus.Should().Be(MemberStatus.NEW_MEMBER);
        var again = () => member.ChangeStatus(MemberStatus.DISCIPLE, leaderId, null);
        again.Should().Throw<DomainException>();
    }
}

public class DevotionRulesTests
{
    private static readonly DateOnly Tue = new(2026, 11, 24);
    private static DateTime At(DateOnly d, int h, int m = 0) => d.ToDateTime(new TimeOnly(h, m));

    [Fact]
    public void Day_is_open_all_day_and_night_runs_past_midnight()
    {
        var w = DevotionWindows.Default;
        w.OpenAt(At(Tue, 10)).Should().Equal((DevotionSlot.DAY, Tue));
        w.OpenAt(At(Tue, 19)).Should().Equal((DevotionSlot.DAY, Tue), (DevotionSlot.NIGHT, Tue));
        w.OpenAt(At(Tue, 23, 59)).Should().Contain((DevotionSlot.DAY, Tue));
        // Just after midnight: a new Day, and Monday night is still open until 05:00 inclusive.
        w.OpenAt(At(Tue, 0, 30)).Should().Equal((DevotionSlot.NIGHT, Tue.AddDays(-1)), (DevotionSlot.DAY, Tue));
        w.IsOpen(DevotionSlot.NIGHT, Tue.AddDays(-1), At(Tue, 5, 0)).Should().BeTrue();
        w.IsOpen(DevotionSlot.NIGHT, Tue.AddDays(-1), At(Tue, 5, 1)).Should().BeFalse();
        w.IsOpen(DevotionSlot.NIGHT, Tue, At(Tue, 17, 59)).Should().BeFalse();
    }

    [Fact]
    public void A_second_upload_edits_the_first_and_rechecks_only_changed_content()
    {
        var now = DateTimeOffset.UtcNow;
        var hash = new string('a', 64);
        var d = Devotion.Submit(Guid.NewGuid(), Guid.NewGuid(), Tue, DevotionSlot.DAY, "Psalm 23", null, hash, now);
        d.RecordAiResult(12, null, "dev", "1", now);

        d.Replace("Psalm 23", null, hash, now);
        d.AiStatus.Should().Be(AiStatus.DONE, "the same content keeps its score");

        d.Replace("Psalm 23, verse 4", null, new string('b', 64), now);
        d.AiStatus.Should().Be(AiStatus.PENDING);
        d.AiTextScore.Should().BeNull();

        var empty = () => d.Replace("  ", null, hash, now);
        empty.Should().Throw<DomainException>();
    }
}
