using AwesomeAssertions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TimeCheck.Application.Abstractions;
using TimeCheck.Application.Common;
using TimeCheck.Application.Features.Admin;
using TimeCheck.Application.Features.Me;
using TimeCheck.Domain.Entities;
using TimeCheck.Domain.Enums;

namespace TimeCheck.Application.Tests;

public class PipelineTests
{
    private static readonly CurrentUserInfo Leader = new(Guid.NewGuid(), Guid.NewGuid(), UserRole.LEADER, true, [Guid.NewGuid()], null);

    private sealed record Ping(string Value) : IRequest<string>, IAuditedRequest
    {
        public string AuditAction => "test.ping";
    }

    private sealed class PingHandler(AuditScope audit) : IRequestHandler<Ping, string>
    {
        public Task<string> Handle(Ping request, CancellationToken ct)
        {
            audit.Describe("thing", Guid.Empty, null, new { request.Value });
            return Task.FromResult(request.Value);
        }
    }

    private static readonly IReportCache Reports = Substitute.For<IReportCache>();

    private static (Dispatcher Dispatcher, IAppDbContext Db, DbSet<AuditLog> Audit) Build()
    {
        var db = Substitute.For<IAppDbContext>();
        var auditSet = Substitute.For<DbSet<AuditLog>>();
        db.AuditLogs.Returns(auditSet);
        var current = Substitute.For<ICurrentUser>();
        current.Info.Returns(Leader);
        var request = Substitute.For<IRequestContext>();
        request.IpAddress.Returns("203.0.113.5");
        request.CorrelationId.Returns("corr-1");
        var bidx = Substitute.For<IBlindIndexer>();
        bidx.Ip(Arg.Any<string>()).Returns("hashed-ip");

        var services = new ServiceCollection().AddApplication();
        services.AddSingleton(db).AddSingleton(current).AddSingleton(request).AddSingleton(bidx).AddSingleton(Reports);
        services.AddScoped<IRequestHandler<Ping, string>, PingHandler>();
        var sp = services.BuildServiceProvider().CreateScope().ServiceProvider;
        return (sp.GetRequiredService<Dispatcher>(), db, auditSet);
    }

    [Fact]
    public async Task Audited_request_writes_one_audit_row_with_hashed_ip_and_saves_once()
    {
        var (dispatcher, db, audit) = Build();

        var result = await dispatcher.Send(new Ping("hello"));

        result.Should().Be("hello");
        audit.Received(1).Add(Arg.Is<AuditLog>(a =>
            a.Action == "test.ping" && a.ActorId == Leader.AppUserId && a.ChurchId == Leader.ChurchId &&
            a.IpHash == "hashed-ip" && a.CorrelationId == "corr-1" && a.After!.Contains("hello")));
        await db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await Reports.Received().InvalidateAsync(Leader.ChurchId); // the church's cached reports are stale now
    }

    [Fact]
    public async Task Invalid_request_is_rejected_before_the_handler_and_nothing_is_saved()
    {
        var (dispatcher, db, audit) = Build();

        var act = () => dispatcher.Send(new UpdateMyProfile(null, "not-a-phone!", null));

        (await act.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().Contain(e => e.PropertyName == "Mobile");
        audit.DidNotReceive().Add(Arg.Any<AuditLog>());
        await db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(50, true)]
    [InlineData(201, false)]
    public void Audit_log_page_size_is_bounded(int limit, bool valid) =>
        new ListAuditLogsValidator().Validate(new ListAuditLogs(null, null, null, null, null, limit)).IsValid.Should().Be(valid);
}

public class AccessAndCursorTests
{
    [Fact]
    public void Leaders_manage_only_groups_they_lead_and_members_view_only_their_own()
    {
        var led = Guid.NewGuid();
        var other = Guid.NewGuid();
        var leader = new CurrentUserInfo(Guid.NewGuid(), Guid.NewGuid(), UserRole.LEADER, true, [led], led);
        var member = new CurrentUserInfo(Guid.NewGuid(), Guid.NewGuid(), UserRole.MEMBER, true, [], led);
        var admin = new CurrentUserInfo(Guid.NewGuid(), Guid.NewGuid(), UserRole.ADMIN, true, [], null);

        leader.CanManageGroup(led).Should().BeTrue();
        leader.CanManageGroup(other).Should().BeFalse();
        member.CanViewGroup(led).Should().BeTrue();
        member.CanManageGroup(led).Should().BeFalse();
        member.CanViewGroup(other).Should().BeFalse();
        admin.CanManageGroup(other).Should().BeTrue();
    }

    [Fact]
    public void Cursor_round_trips_and_rejects_garbage()
    {
        var at = new DateTimeOffset(2026, 11, 24, 1, 2, 3, TimeSpan.Zero);
        var id = Guid.NewGuid();

        Cursor.Decode(Cursor.Encode(at, id)).Should().Be((at, id));
        Cursor.Decode(null).Should().BeNull();
        var act = () => Cursor.Decode("%%%not-a-cursor");
        act.Should().Throw<ValidationException>();
    }
}
