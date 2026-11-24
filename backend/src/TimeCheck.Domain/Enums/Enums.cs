namespace TimeCheck.Domain.Enums;

// Names match the Postgres enum labels exactly (mapped by Npgsql).
#pragma warning disable CA1707, CA1700
public enum UserRole { SUPER_ADMIN, ADMIN, LEADER, MEMBER }
public enum MemberStatus { NEW_MEMBER, DISCIPLE, INCONSISTENT, INACTIVE }
public enum Gender { MALE, FEMALE }
public enum EventCategory { DAILY, WEEKLY, LEADER_SCHEDULED, ADMIN_SCHEDULED }
public enum EventCadence { DAILY, DAILY_MON_SAT, WEEKLY, AD_HOC, SCHEDULED }
public enum EventScope { INDIVIDUAL, GROUP, CHURCH_WIDE }
public enum AttendanceStatus { PRESENT, LATE, ABSENT, EXCUSED, UNMARKED }
public enum DevotionSlot { DAY, NIGHT }
public enum AiStatus { PENDING, DONE, FAILED, SKIPPED }
public enum InviteStatus { EXPECTED, CAME, DID_NOT_COME }
public enum ReportJobStatus { QUEUED, RUNNING, DONE, FAILED, EXPIRED }
#pragma warning restore CA1707, CA1700

public static class UserRoleExtensions
{
    /// <summary>Higher = more privileged.</summary>
    public static int Rank(this UserRole role) => role switch
    {
        UserRole.SUPER_ADMIN => 3,
        UserRole.ADMIN => 2,
        UserRole.LEADER => 1,
        _ => 0,
    };

    public static bool IsAtLeast(this UserRole role, UserRole minimum) => role.Rank() >= minimum.Rank();
}
