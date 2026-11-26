using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TimeCheck.Application.Abstractions;
using TimeCheck.Application.Common;
using TimeCheck.Application.Features.Members;
using TimeCheck.Domain.Entities;
using TimeCheck.Domain.Enums;

namespace TimeCheck.Application.Features.Admin;

public sealed record FirstSuperAdminInput(string Email, string FirstName, string LastName, Gender Gender, DateOnly Birthdate, string? Church = null);

/// <summary>
/// Creates a new church's first Super Admin (runbooks/database-roles.md), run once from the API container's shell. Nobody
/// can sign in yet to register them, and their birthdate must be encrypted with the environment's keys, so plain SQL won't
/// do. They're invited like any member: Supabase emails a link, then they set a password, consent and enrol a code.
/// Refuses once the church has a Super Admin; more are added in the app.
/// </summary>
public sealed class FirstSuperAdmin(IAppDbContext db, IAuthAdmin authAdmin, TimeProvider clock)
{
    public async Task<Guid> RunAsync(FirstSuperAdminInput input, CancellationToken ct)
    {
        var churches = await db.Churches.AsNoTracking()
            .Where(c => input.Church == null || c.Name == input.Church)
            .Select(c => c.Id).ToListAsync(ct);
        var churchId = churches.Count switch
        {
            1 => churches[0],
            0 => throw new ConflictException(input.Church is null
                ? "No church yet. Run select public.create_church('<name>', '<timezone>'); first."
                : $"No church named '{input.Church}'."),
            _ => throw new ConflictException("There's more than one church. Name one with --church."),
        };

        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.ChurchId == churchId && u.DeletedAt == null && u.Role == UserRole.SUPER_ADMIN, ct))
            throw new ConflictException("This church already has a Super Admin. Add more from the app.");

        var email = input.Email.Trim().ToLowerInvariant();
        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == email, ct)) throw new ConflictException("Someone already uses this email.");

        var today = await Members.Members.Today(db, churchId, clock, ct);
        var user = AppUser.Create(churchId, input.FirstName, input.LastName, input.Gender, input.Birthdate, email, today, status: MemberStatus.DISCIPLE);
        user.MakeFirstSuperAdmin();

        // The invite is outside the transaction: if saving fails, the new sign-in account is removed again.
        var authUserId = await authAdmin.InviteUserAsync(email, ct);
        user.LinkAuthUser(authUserId);
        db.Users.Add(user);
        db.AuditLogs.Add(AuditLog.Create(churchId, null, "member.first_super_admin", "app_user", user.Id.ToString(), null,
            JsonSerializer.Serialize(new { role = "SUPER_ADMIN" }), null, "setup command", null));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            await authAdmin.DeleteUserAsync(authUserId, CancellationToken.None);
            throw;
        }
        return user.Id;
    }
}
