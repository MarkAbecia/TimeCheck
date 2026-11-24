import type { Pass } from "@/components/event-pass";
import type { Role } from "./nav";

const TZ = "Asia/Manila";
const fmt = (d: Date, o: Intl.DateTimeFormatOptions) => new Intl.DateTimeFormat("en-PH", { timeZone: TZ, ...o }).format(d);

// shortcut: demo passes until the events API lands in Phase 6; then Today reads GET /api/v1/me/today.
// Today's passes come in time order, group and church-wide together; later church-wide events go under "Coming up".
export function demoToday(role: Role, now = new Date()): { today: Pass[]; upcoming: Pass[] } {
  const leading = role !== "member";
  const later = new Date(now.getTime() + 3 * 86_400_000);
  return {
    today: [
      {
        id: "morning-prayer",
        kind: "group",
        timeLabel: "5:00 – 6:00 AM",
        name: "Morning Prayer",
        meta: "Joshua Cell · 20 members",
        stub: leading ? { state: "done", present: 18, total: 20, href: "/events/morning-prayer" } : { state: "attended" },
      },
      {
        id: "cell-meeting",
        kind: "group",
        timeLabel: "7:30 – 9:30 PM",
        name: "Cell Meeting",
        meta: "Joshua Cell · at the Cruz home",
        stub: leading ? { state: "todo", total: 20, href: "/events/cell-meeting" } : { state: "pending" },
      },
    ],
    upcoming: [
      {
        id: "week-of-fire",
        kind: "church",
        timeLabel: `${fmt(later, { weekday: "short" })} ${fmt(later, { day: "numeric" })} ${fmt(later, { month: "short" })} · 7:00 PM`,
        name: "Week of Fire",
        meta: leading ? "Main hall · your 20 members" : "Main hall",
        stub: { state: "upcoming", day: fmt(later, { weekday: "short" }), time: "7 PM" },
      },
    ],
  };
}
