using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using TimeCheck.Application.Abstractions;
using TimeCheck.Application.Features.Devotions;

namespace TimeCheck.Api.IntegrationTests;

/// <summary>§6.3 devotion upload and AI detection, §15 Q4/Q5/Q9, and the access checks around them.</summary>
[Collection(ApiTestGroup.Name)]
public class DevotionsTests(ApiFactory factory)
{
    private static readonly Guid MemberAppId = Guid.Parse("10000000-0000-0000-0000-000000000004");
    private static readonly Guid Member2AppId = Guid.Parse("10000000-0000-0000-0000-000000000006");
    private static readonly Guid Member2Auth = Guid.Parse("30000000-0000-0000-0000-000000000006");
    private static readonly TimeSpan Manila = TimeSpan.FromHours(8);
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.Add(Manila));

    private HttpClient Member => factory.ClientFor(ApiFactory.MemberAuth);
    private HttpClient Leader => factory.ClientFor(ApiFactory.LeaderAuth);

    private static async Task<HttpResponseMessage> Put(HttpClient client, DateOnly date, string slot, string? text, byte[]? image = null, bool keepImage = false)
    {
        using var form = new MultipartFormDataContent();
        if (text is not null) form.Add(new StringContent(text), "text");
        form.Add(new StringContent(keepImage ? "true" : "false"), "keepImage");
        if (image is not null) form.Add(new ByteArrayContent(image), "image", "photo.png");
        return await client.PutAsync($"/api/v1/me/devotions/{date:yyyy-MM-dd}/{slot}", form);
    }

    private static async Task<JsonElement> Ok(HttpResponseMessage res)
    {
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static byte[] Png()
    {
        using var bmp = new SKBitmap(40, 30);
        bmp.Erase(new SKColor((byte)Random.Shared.Next(256), 40, 90));
        using var img = SKImage.FromBitmap(bmp);
        return img.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }

    private async Task<int> RunChecker()
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<DevotionChecker>().RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task A_second_upload_edits_the_first_and_the_audit_never_holds_the_text()
    {
        var first = await Ok(await Put(Member, Today, "DAY", $"Psalm 23 reflection {Guid.NewGuid()}"));
        var second = await Ok(await Put(Member, Today, "DAY", "  Psalm 23: The Lord is my shepherd. Edited.  "));

        second.GetProperty("id").GetGuid().Should().Be(first.GetProperty("id").GetGuid());
        second.GetProperty("text").GetString().Should().Be("Psalm 23: The Lord is my shepherd. Edited.");
        second.GetProperty("aiStatus").GetString().Should().Be("PENDING");
        second.GetProperty("aiTextScore").ValueKind.Should().Be(JsonValueKind.Null, "members don't see scores unless the church allows it");
        (await factory.Scalar<long>($"select count(*) from devotions where user_id = '{MemberAppId}' and devotion_date = '{Today:yyyy-MM-dd}' and slot = 'DAY'")).Should().Be(1);

        var audit = await factory.Scalar<string>($"select after::text from audit_logs where action = 'devotion.submit' and entity_id = '{second.GetProperty("id").GetGuid()}' order by occurred_at desc limit 1");
        audit.Should().Contain("hasText").And.NotContain("shepherd");
    }

    [Fact]
    public async Task Closed_slots_and_bad_files_are_refused()
    {
        var old = await Put(Member, Today.AddDays(-3), "DAY", "Late");
        old.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await old.Content.ReadAsStringAsync()).Should().Contain("devotion.closed");

        var fake = await Put(Member, Today, "DAY", null, Encoding.UTF8.GetBytes("<?php echo 'not an image'; ?>"));
        fake.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var empty = await Put(Member, Today, "DAY", "   ");
        empty.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        (await Put(factory.ClientFor(ApiFactory.MemberAuth), Today, "DAY", new string('x', 5001))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Images_are_re_encoded_kept_private_and_scored_by_the_worker()
    {
        var member2 = factory.ClientFor(Member2Auth);
        // Bytes hidden after the PNG's end (where a polyglot or GPS block could sit) must not survive.
        var png = Png().Concat(Encoding.ASCII.GetBytes("GPS-SECRET-14.5995,120.9842")).ToArray();
        var saved = await Ok(await Put(member2, Today, "DAY", null, png));
        var id = saved.GetProperty("id").GetGuid();
        saved.GetProperty("hasImage").GetBoolean().Should().BeTrue();

        var stored = factory.Storage.Objects.Single(o => o.Key.StartsWith("devotions/", StringComparison.Ordinal) && o.Key.Contains(Member2AppId.ToString()));
        stored.Value.ContentType.Should().Be("image/jpeg");
        stored.Value.Bytes.Take(3).Should().Equal(0xFF, 0xD8, 0xFF);
        Encoding.ASCII.GetString(stored.Value.Bytes).Should().NotContain("GPS-SECRET");

        // Same image again, now with text: the image keeps its key; dropping the image removes it from storage after commit.
        await Ok(await Put(member2, Today, "DAY", "With a caption", keepImage: true));
        factory.Storage.Objects.Should().ContainKey(stored.Key);
        await Ok(await Put(member2, Today, "DAY", "Text only now"));
        factory.Storage.Objects.Should().NotContainKey(stored.Key);
        await Ok(await Put(member2, Today, "DAY", null, png)); // back to the photo for the worker

        (await RunChecker()).Should().BeGreaterThan(0);
        var leader2 = factory.ClientFor(ApiFactory.Leader2Auth);
        var detail = await leader2.GetFromJsonAsync<JsonElement>($"/api/v1/devotions/{id}");
        var d = detail.GetProperty("devotion");
        d.GetProperty("aiStatus").GetString().Should().Be("DONE");
        d.GetProperty("aiImageScore").GetDecimal().Should().BeInRange(0, 100);
        d.GetProperty("band").GetString().Should().BeOneOf("LIKELY_HUMAN", "UNCERTAIN", "LIKELY_AI");
        detail.GetProperty("imageUrl").GetString().Should().Contain("ttl=300", "signed URLs live 5 minutes at most");
        detail.GetProperty("canReview").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Identical_content_reuses_the_earlier_score()
    {
        var text = $"Shared verse {Guid.NewGuid()}";
        await Ok(await Put(Member, Today, "DAY", text));
        await RunChecker();
        var member2 = factory.ClientFor(Member2Auth);
        await Ok(await Put(member2, Today, "DAY", text));
        await RunChecker();

        var scores = await factory.Scalar<long>($"select count(distinct ai_text_score) from devotions where devotion_date = '{Today:yyyy-MM-dd}' and slot = 'DAY' and text_content = '{text}' and ai_status = 'DONE'");
        scores.Should().Be(1);
    }

    [Fact]
    public async Task Leaders_see_their_group_with_bands_and_review_without_changing_anything_else()
    {
        var mine = await Ok(await Put(Member, Today, "DAY", $"Reviewed reflection {Guid.NewGuid()}"));
        var id = mine.GetProperty("id").GetGuid();
        await RunChecker();

        var list = await Leader.GetFromJsonAsync<JsonElement>("/api/v1/devotions");
        var row = list.GetProperty("members").EnumerateArray().Single(m => m.GetProperty("userId").GetGuid() == MemberAppId);
        row.GetProperty("day").GetProperty("band").GetString().Should().NotBeNull();
        list.GetProperty("members").EnumerateArray().Should().NotContain(m => m.GetProperty("userId").GetGuid() == Member2AppId, "member2 is in another group");

        var filtered = await Leader.GetFromJsonAsync<JsonElement>("/api/v1/devotions?likelyAi=true");
        filtered.GetProperty("members").EnumerateArray().Should().OnlyContain(m =>
            m.GetProperty("day").ValueKind == JsonValueKind.Object && m.GetProperty("day").GetProperty("band").GetString() == "LIKELY_AI"
            || m.GetProperty("night").ValueKind == JsonValueKind.Object && m.GetProperty("night").GetProperty("band").GetString() == "LIKELY_AI");

        var reviewed = await Leader.PostAsJsonAsync($"/api/v1/devotions/{id}/review", new { note = "Talked after cell; it's hers." });
        var body = await Ok(reviewed);
        body.GetProperty("devotion").GetProperty("reviewed").GetBoolean().Should().BeTrue();
        body.GetProperty("reviewNote").GetString().Should().Be("Talked after cell; it's hers.");
        (await factory.Scalar<string>($"select ai_status::text from devotions where id = '{id}'")).Should().Be("DONE", "reviewing never changes the result");
        (await factory.Scalar<string>($"select after::text from audit_logs where action = 'devotion.review' and entity_id = '{id}'")).Should().NotContain("Talked");

        // The member sees their own devotion, but not the leader's note or (by default) the score.
        var own = await Member.GetFromJsonAsync<JsonElement>($"/api/v1/devotions/{id}");
        own.GetProperty("reviewNote").ValueKind.Should().Be(JsonValueKind.Null);
        own.GetProperty("devotion").GetProperty("band").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Devotions_are_private_to_the_member_their_leader_and_admins()
    {
        var mine = await Ok(await Put(Member, Today, "DAY", $"Private {Guid.NewGuid()}"));
        var id = mine.GetProperty("id").GetGuid();

        (await factory.ClientFor(ApiFactory.Leader2Auth).GetAsync($"/api/v1/devotions/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await factory.ClientFor(Member2Auth).GetAsync($"/api/v1/devotions/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await factory.ClientFor(ApiFactory.OtherChurchAuth).GetAsync($"/api/v1/devotions/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await factory.ClientFor(ApiFactory.AdminAuth, "aal2").GetAsync($"/api/v1/devotions/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Member.GetAsync("/api/v1/devotions")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Member.PostAsJsonAsync($"/api/v1/devotions/{id}/review", new { note = "self" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Leader.GetAsync($"/api/v1/devotions?groupId={ApiFactory.Group2}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task My_devotions_show_open_slots_and_the_streak()
    {
        await Ok(await Put(Member, Today, "DAY", $"Streak {Guid.NewGuid()}"));
        var me = await Member.GetFromJsonAsync<JsonElement>("/api/v1/me/devotions");
        me.GetProperty("streak").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        me.GetProperty("open").EnumerateArray().Should().Contain(s => s.GetProperty("slot").GetString() == "DAY" && s.GetProperty("date").GetString() == Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
            && s.GetProperty("devotion").ValueKind == JsonValueKind.Object);
    }

    [Fact]
    public async Task Reminders_go_once_to_people_who_have_not_submitted()
    {
        await Ok(await Put(Member, Today, "DAY", $"Before the reminder {Guid.NewGuid()}"));
        await factory.Scalar<int>($"delete from devotions where user_id = '{Member2AppId}' and devotion_date = '{Today:yyyy-MM-dd}' and slot = 'DAY'");
        await factory.Scalar<int>("delete from notifications where type like 'devotion.reminder%'");
        var at = new DateTimeOffset(Today.ToDateTime(new TimeOnly(7, 30)), Manila);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        await new DevotionReminders(db, new FixedClock(at)).RunAsync(CancellationToken.None);
        await new DevotionReminders(db, new FixedClock(at.AddMinutes(15))).RunAsync(CancellationToken.None);

        (await factory.Scalar<long>($"select count(*) from notifications where type = 'devotion.reminder.day' and user_id = '{Member2AppId}'")).Should().Be(1);
        (await factory.Scalar<long>($"select count(*) from notifications where type = 'devotion.reminder.day' and user_id = '{MemberAppId}'")).Should().Be(0);
        (await factory.Scalar<long>("select count(*) from notifications where type = 'devotion.reminder.night'")).Should().Be(0, "the night reminder is at 20:00");
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
    }
}
