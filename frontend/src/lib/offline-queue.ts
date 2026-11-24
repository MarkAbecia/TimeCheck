import { send } from "./api-client";
import type { AttendanceStatus } from "./types";

// Attendance marked with no signal waits here until the phone is back online (§11.3 offline queue, §9 retries).
// Only ids and statuses are stored, never names. Marking is a PUT of the final state, so resending is safe.
const KEY = "tc.attendance-queue.v1";

export type QueuedMark = { userId: string; status: AttendanceStatus; note: string | null; rowVersion: number | null };
type Queue = Record<string, { marks: QueuedMark[]; queuedAt: string }>;

const read = (): Queue => {
  try {
    return JSON.parse(localStorage.getItem(KEY) ?? "{}") as Queue;
  } catch {
    return {};
  }
};
const write = (q: Queue) => (Object.keys(q).length ? localStorage.setItem(KEY, JSON.stringify(q)) : localStorage.removeItem(KEY));

/** Adds marks for an event; a later mark for the same person replaces the earlier one. */
export function queueMarks(eventId: string, marks: QueuedMark[]) {
  const q = read();
  const byUser = new Map((q[eventId]?.marks ?? []).map((m) => [m.userId, m]));
  for (const m of marks) byUser.set(m.userId, m);
  q[eventId] = { marks: [...byUser.values()], queuedAt: new Date().toISOString() };
  write(q);
}

export const pendingFor = (eventId: string): QueuedMark[] => read()[eventId]?.marks ?? [];
export const pendingCount = () => Object.values(read()).reduce((n, e) => n + e.marks.length, 0);
export const clearQueue = () => localStorage.removeItem(KEY);

export type FlushResult = { sent: number; clashed: string[]; kept: number };

/**
 * Sends every queued event. Kept for later: offline, server errors, rate limits. Dropped and reported: a clash with someone else's
 * newer marks (409), or an event that can no longer be marked (403/404/422), since retrying would never succeed.
 */
export async function flushQueue(): Promise<FlushResult> {
  const result: FlushResult = { sent: 0, clashed: [], kept: 0 };
  for (const [eventId, entry] of Object.entries(read())) {
    const r = await send("PUT", `/events/${eventId}/attendance`, { marks: entry.marks });
    const retry = !r.ok && (r.problem.status === 0 || r.problem.status === 429 || r.problem.status >= 500);
    if (retry) {
      result.kept += entry.marks.length;
      continue;
    }
    const q = read();
    delete q[eventId];
    write(q);
    if (r.ok) result.sent += entry.marks.length;
    else result.clashed.push(eventId);
  }
  return result;
}
