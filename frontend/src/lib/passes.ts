import type { Pass } from "@/components/event-pass";
import { dayLabel, shortTime, timeRange, weekday } from "./time";
import type { EventSummary } from "./types";

/** API event → Event Pass. Leaders/admins get the check-in stub when they mark people there; members see their own mark. */
export function toPass(e: EventSummary, when: "today" | "upcoming", t: { members: (n: number) => string }): Pass {
  const starts = new Date(e.startsAt);
  const where = [e.groupName, e.location].filter(Boolean).join(" · ");
  const meta = e.marking ? [e.groupName ?? e.location, t.members(e.marking.expected)].filter(Boolean).join(" · ") : where;
  const href = `/events/${e.id}`;
  const stub: Pass["stub"] =
    when === "upcoming"
      ? { state: "upcoming", day: weekday(starts), time: shortTime(starts) }
      : e.marking
        ? e.marking.marked === 0
          ? { state: "todo", total: e.marking.expected, href }
          : { state: "done", present: e.marking.present, total: e.marking.expected, href }
        : e.myStatus === "PRESENT" || e.myStatus === "LATE"
          ? { state: "attended" }
          : { state: "pending" };
  return {
    id: e.id,
    kind: e.groupId ? "group" : "church",
    timeLabel: when === "upcoming" ? `${dayLabel(starts)} · ${shortTime(starts)}` : timeRange(e.startsAt, e.endsAt),
    name: e.title,
    meta,
    stub,
  };
}
