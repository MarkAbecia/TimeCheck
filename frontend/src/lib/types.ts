// API shapes (backend DTOs). Dates are ISO strings: DateOnly as "yyyy-mm-dd".
export type ApiRole = "SUPER_ADMIN" | "ADMIN" | "LEADER" | "MEMBER";
export type MemberStatus = "NEW_MEMBER" | "DISCIPLE" | "INCONSISTENT" | "INACTIVE";
export type Gender = "MALE" | "FEMALE";

export const MEMBER_STATUSES: MemberStatus[] = ["NEW_MEMBER", "DISCIPLE", "INCONSISTENT", "INACTIVE"];

export type Member = {
  id: string;
  firstName: string;
  lastName: string;
  nickname: string | null;
  displayName: string;
  email: string;
  gender: Gender;
  birthdate: string;
  gradeLevel: string | null;
  mobile: string | null;
  address: string | null;
  status: MemberStatus;
  role: ApiRole;
  joinedOn: string;
  isMinor: boolean;
  guardianName: string | null;
  guardianContact: string | null;
  guardianConsentAt: string | null;
  groupId: string | null;
  groupName: string | null;
  isActive: boolean;
};

export type GroupMember = { id: string; displayName: string; status: MemberStatus; isLeader: boolean };
export type Group = { id: string; name: string; leaderId: string | null; parentGroupId: string | null; members: GroupMember[] };
export type GroupSummary = { id: string; name: string; leaderId: string | null; leaderName: string | null; memberCount: number };
export type LeaderOption = { id: string; displayName: string; role: ApiRole };

export type AttendanceStatus = "PRESENT" | "LATE" | "ABSENT" | "EXCUSED" | "UNMARKED";
export type EventCategory = "DAILY" | "WEEKLY" | "LEADER_SCHEDULED" | "ADMIN_SCHEDULED";

export type EventType = {
  id: string;
  code: string;
  name: string;
  category: EventCategory;
  defaultScope: "INDIVIDUAL" | "GROUP" | "CHURCH_WIDE";
  schedulable: boolean;
  leadersMaySchedule: boolean;
};

export type Marking = { expected: number; marked: number; present: number };

export type EventSummary = {
  id: string;
  typeCode: string;
  typeName: string;
  category: EventCategory;
  groupId: string | null;
  groupName: string | null;
  title: string;
  startsAt: string;
  endsAt: string;
  occursOn: string;
  location: string | null;
  isCancelled: boolean;
  isChurchWide: boolean;
  myStatus: AttendanceStatus | null;
  marking: Marking | null;
};

export type RosterEntry = {
  userId: string;
  displayName: string;
  groupId: string | null;
  groupName: string | null;
  status: AttendanceStatus;
  note: string | null;
  rowVersion: number | null;
  markedAt: string | null;
};

export type EventDetail = Omit<EventSummary, "myStatus" | "marking"> & {
  eventTypeId: string;
  description: string | null;
  onlineLink: string | null;
  seriesId: string | null;
  isGenerated: boolean;
  audienceGroupIds: string[];
  audienceUserIds: string[];
  canManage: boolean;
  canMark: boolean;
  canEditMarks: boolean;
  myStatus: AttendanceStatus | null;
  roster: RosterEntry[] | null;
};

export type Today = { date: string; today: EventSummary[]; upcoming: EventSummary[]; unmarkedPast: number };

export type HistoryItem = { eventId: string; typeCode: string; typeName: string; title: string; startsAt: string; occursOn: string; status: AttendanceStatus };
export type MyAttendance = { from: string; to: string; items: HistoryItem[]; attended: number; counted: number; morningPrayerStreak: number };
