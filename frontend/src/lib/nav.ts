import {
  BarChart3,
  BookOpenCheck,
  CalendarDays,
  Bell,
  History,
  LayoutGrid,
  ScrollText,
  Settings,
  Sun,
  UserPlus,
  UserRound,
  Users,
  type LucideIcon,
} from "lucide-react";

export type Role = "super_admin" | "admin" | "leader" | "member";

export type NavKey =
  | "today" | "members" | "devotions" | "events" | "invites" | "history"
  | "groups" | "reports" | "notifications" | "profile" | "settings" | "audit";

export type NavItem = { key: NavKey; href: string; icon: LucideIcon };

const ITEMS: Record<NavKey, NavItem> = {
  today: { key: "today", href: "/", icon: Sun },
  members: { key: "members", href: "/members", icon: Users },
  devotions: { key: "devotions", href: "/devotions", icon: BookOpenCheck },
  events: { key: "events", href: "/events", icon: CalendarDays },
  invites: { key: "invites", href: "/invites", icon: UserPlus },
  history: { key: "history", href: "/history", icon: History },
  groups: { key: "groups", href: "/groups", icon: LayoutGrid },
  reports: { key: "reports", href: "/reports", icon: BarChart3 },
  notifications: { key: "notifications", href: "/notifications", icon: Bell },
  profile: { key: "profile", href: "/profile", icon: UserRound },
  settings: { key: "settings", href: "/settings", icon: Settings },
  audit: { key: "audit", href: "/audit", icon: ScrollText },
};

// Ordered by how often each role reaches for it (§4.2). The first four become phone tabs; the rest live under More.
const BY_ROLE: Record<Role, NavKey[]> = {
  member: ["today", "devotions", "invites", "history", "notifications", "profile"],
  leader: ["today", "members", "devotions", "events", "invites", "reports", "history", "notifications", "profile"],
  admin: ["today", "groups", "events", "reports", "members", "devotions", "invites", "history", "notifications", "profile"],
  super_admin: [
    "today", "groups", "events", "reports", "members", "devotions", "invites", "history",
    "notifications", "profile", "settings", "audit",
  ],
};

export const PHONE_TABS = 4; // + "More" = 5, the §11.3 maximum

export function navFor(role: Role) {
  const all = BY_ROLE[role].map((k) => ITEMS[k]);
  // When everything fits in five slots, skip "More" entirely.
  const fits = all.length <= PHONE_TABS + 1;
  return { all, tabs: fits ? all : all.slice(0, PHONE_TABS), more: fits ? [] : all.slice(PHONE_TABS) };
}

export function isActive(href: string, pathname: string) {
  return href === "/" ? pathname === "/" : pathname === href || pathname.startsWith(`${href}/`);
}

