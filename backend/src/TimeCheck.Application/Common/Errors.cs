using System.Text;
using TimeCheck.Application.Abstractions;
using TimeCheck.Domain.Enums;

namespace TimeCheck.Application.Common;

/// <summary>Mapped to 404. Also used instead of 403 when revealing existence would leak data (IDOR).</summary>
public sealed class NotFoundException(string message = "Not found.") : Exception(message);

/// <summary>Mapped to 403.</summary>
public sealed class ForbiddenException(string message = "You don't have access to this.") : Exception(message);

/// <summary>Mapped to 409.</summary>
public sealed class ConflictException(string message) : Exception(message);

/// <summary>A possible duplicate person. Field is "mobile" or "nameBirthdate"; no details of the other person are revealed.</summary>
public sealed record DuplicateMatch(string Field, bool SameGroup);

/// <summary>Mapped to 409 with the matches; the client asks the user to confirm and resends with confirmDuplicates.</summary>
public sealed class DuplicateWarningException(IReadOnlyList<DuplicateMatch> matches)
    : Exception("This might be someone who is already registered.")
{
    public IReadOnlyList<DuplicateMatch> Matches { get; } = matches;
}

/// <summary>An event already booked at that time for the same group (or church-wide). A warning, not a block (§6.6).</summary>
public sealed record EventConflict(Guid Id, string Title, DateTimeOffset StartsAt, DateTimeOffset EndsAt);

/// <summary>Mapped to 409 with the conflicts; the client asks the user to confirm and resends with confirmConflicts.</summary>
public sealed class EventConflictWarningException(IReadOnlyList<EventConflict> conflicts)
    : Exception("Something is already scheduled at that time.")
{
    public IReadOnlyList<EventConflict> Conflicts { get; } = conflicts;
}

public sealed record CursorPage<T>(IReadOnlyList<T> Items, string? NextCursor);

/// <summary>Opaque keyset cursor: (timestamp, id).</summary>
public static class Cursor
{
    public static string Encode(DateTimeOffset at, Guid id) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{at.UtcTicks}:{id}")).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static (DateTimeOffset At, Guid Id)? Decode(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor)) return null;
        try
        {
            var s = cursor.Replace('-', '+').Replace('_', '/');
            s = s.PadRight(s.Length + ((4 - (s.Length % 4)) % 4), '=');
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(s)).Split(':');
            return (new DateTimeOffset(long.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture), TimeSpan.Zero), Guid.Parse(parts[1]));
        }
        catch (Exception e) when (e is FormatException or IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            throw new FluentValidation.ValidationException("Invalid cursor.");
        }
    }
}

/// <summary>Resource-level checks (IDOR prevention, §8.1).</summary>
public static class Access
{
    public static bool IsAdmin(this CurrentUserInfo u) => u.Role.IsAtLeast(UserRole.ADMIN);

    public static bool CanManageGroup(this CurrentUserInfo u, Guid groupId) => u.IsAdmin() || u.LedGroupIds.Contains(groupId);

    /// <summary>Members may see their own group; leaders their led groups; admins everything.</summary>
    public static bool CanViewGroup(this CurrentUserInfo u, Guid groupId) => u.CanManageGroup(groupId) || u.GroupId == groupId;
}
