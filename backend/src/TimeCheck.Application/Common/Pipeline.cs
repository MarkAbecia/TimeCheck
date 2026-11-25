using System.Text.Json;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using TimeCheck.Application.Abstractions;
using TimeCheck.Domain.Entities;

namespace TimeCheck.Application.Common;

#pragma warning disable CA1040 // marker interfaces are the point here
public interface IRequest<TResult>;
#pragma warning restore CA1040

/// <summary>Mutating requests implement this; the dispatcher writes one audit row per success (§10).</summary>
public interface IAuditedRequest
{
    string AuditAction { get; }
}

public interface IRequestHandler<in TRequest, TResult> where TRequest : IRequest<TResult>
{
    Task<TResult> Handle(TRequest request, CancellationToken ct);
}

/// <summary>Handlers describe what they changed; the dispatcher turns it into the audit row.</summary>
public sealed class AuditScope
{
    public string? EntityType { get; private set; }
    public string? EntityId { get; private set; }
    public object? Before { get; private set; }
    public object? After { get; private set; }

    public void Describe(string entityType, Guid entityId, object? before, object? after)
    {
        EntityType = entityType;
        EntityId = entityId.ToString();
        Before = before;
        After = after;
    }
}

/// <summary>
/// Side effects outside the database transaction. Rollback hooks undo external calls made by a handler when the
/// request fails (e.g. delete an invited auth user); after-commit hooks run once the data is saved (cache eviction).
/// </summary>
public sealed class UnitOfWork
{
    internal readonly List<Func<Task>> Rollback = [];
    internal readonly List<Func<Task>> Committed = [];

    public void OnRollback(Func<Task> undo) => Rollback.Add(undo);
    public void AfterCommit(Func<Task> action) => Committed.Add(action);
}

/// <summary>validate → handle → audit → save (one transaction via SaveChanges). Handlers never call SaveChanges.</summary>
public sealed class Dispatcher(
    IServiceProvider services, IAppDbContext db, ICurrentUser currentUser, IRequestContext requestContext,
    IBlindIndexer blindIndexer, AuditScope auditScope, UnitOfWork unitOfWork, IReportCache reportCache)
{
    // Enums as their names, so audit rows read "PRESENT" rather than 0.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    public async Task<TResult> Send<TResult>(IRequest<TResult> request, CancellationToken ct = default)
    {
        var requestType = request.GetType();

        var validatorType = typeof(IValidator<>).MakeGenericType(requestType);
        foreach (IValidator validator in services.GetServices(validatorType).Cast<IValidator>())
        {
            var result = await validator.ValidateAsync(new ValidationContext<object>(request), ct);
            if (!result.IsValid) throw new ValidationException(result.Errors);
        }

        var handlerType = typeof(IRequestHandler<,>).MakeGenericType(requestType, typeof(TResult));
        var handler = services.GetRequiredService(handlerType);
        TResult response;
        try
        {
            response = await (Task<TResult>)handlerType.GetMethod("Handle")!.Invoke(handler, [request, ct])!;
            WriteAudit(request);
            await db.SaveChangesAsync(ct); // no-op for queries
            // Every write is audited, so this is "the church's data changed": cached reports are stale.
            if (request is IAuditedRequest) unitOfWork.AfterCommit(() => reportCache.InvalidateAsync(currentUser.Info.ChurchId));
        }
        catch
        {
            foreach (var undo in unitOfWork.Rollback) await undo();
            throw;
        }

        foreach (var action in unitOfWork.Committed)
        {
            try { await action(); }
            catch (Exception e) when (e is not OperationCanceledException) { /* best effort: the change is already saved */ }
        }
        return response;
    }

    private void WriteAudit(object request)
    {
        if (request is IAuditedRequest audited)
        {
            var user = currentUser.Info;
            db.AuditLogs.Add(AuditLog.Create(
                user.ChurchId, user.AppUserId, audited.AuditAction, auditScope.EntityType, auditScope.EntityId,
                auditScope.Before is null ? null : JsonSerializer.Serialize(auditScope.Before, Json),
                auditScope.After is null ? null : JsonSerializer.Serialize(auditScope.After, Json),
                requestContext.IpAddress is { } ip ? blindIndexer.Ip(ip) : null,
                requestContext.UserAgent, requestContext.CorrelationId));
        }
    }
}

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(Dispatcher).Assembly;
        foreach (var type in assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false }))
        {
            foreach (var itf in type.GetInterfaces().Where(i => i.IsGenericType &&
                         (i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>) || i.GetGenericTypeDefinition() == typeof(IValidator<>))))
            {
                services.AddScoped(itf, type);
            }
        }
        services.AddScoped<AuditScope>();
        services.AddScoped<UnitOfWork>();
        services.AddScoped<Dispatcher>();
        services.AddScoped<Features.Events.EventGenerator>();
        services.AddScoped<Features.Devotions.DevotionChecker>();
        services.AddScoped<Features.Devotions.DevotionReminders>();
        services.AddScoped<Features.Invites.InvitePurge>();
        services.AddScoped<Features.Invites.InviteReminders>();
        services.AddScoped<Features.Notifications.NotificationDelivery>();
        services.AddScoped<Features.Notifications.AttendanceReminders>();
        services.AddScoped<Features.Reports.ReportBuilder>();
        services.AddScoped<Features.Reports.ReportExports>();
        return services;
    }
}
