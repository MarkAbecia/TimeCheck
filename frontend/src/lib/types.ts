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
