"use client";

import { Check, CloudOff, Loader2 } from "lucide-react";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { useEffect, useMemo, useState } from "react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { send } from "@/lib/api-client";
import { pendingFor, queueMarks, type QueuedMark } from "@/lib/offline-queue";
import type { AttendanceStatus, EventDetail, RosterEntry } from "@/lib/types";
import { cn } from "@/lib/utils";

const CHOICES: Exclude<AttendanceStatus, "UNMARKED">[] = ["PRESENT", "LATE", "ABSENT", "EXCUSED"];

type Row = RosterEntry & { saved: AttendanceStatus };

const initial = (detail: EventDetail, waiting: QueuedMark[] = []): Row[] => {
  // Marks waiting on this phone show as they will be once they send.
  const pending = new Map(waiting.map((m) => [m.userId, m.status]));
  return (detail.roster ?? []).map((r) => ({
    ...r,
    saved: pending.has(r.userId) ? pending.get(r.userId)! : r.status,
    status: pending.get(r.userId) ?? r.status,
  }));
};

/**
 * "Mark all present", tap the exceptions, save: three interactions for a whole roster (§6.2). Saves go through the offline queue
 * when there is no signal.
 */
export function AttendanceSheet({ detail, groupId }: { detail: EventDetail; groupId?: string }) {
  const t = useTranslations("attendance");
  const router = useRouter();
  const [rows, setRows] = useState<Row[]>(() => initial(detail));
  const [saving, setSaving] = useState(false);
  const readOnly = !detail.canEditMarks;

  // The queue lives in localStorage, which the server render can't see, so waiting marks are applied after mount.
  useEffect(() => {
    const waiting = pendingFor(detail.id);
    // eslint-disable-next-line react-hooks/set-state-in-effect -- one-time sync from browser storage
    if (waiting.length > 0) setRows(initial(detail, waiting));
  }, [detail]);
  const showGroups = new Set(rows.map((r) => r.groupId)).size > 1;

  const changed = useMemo(() => rows.filter((r) => r.status !== r.saved), [rows]);
  const unmarked = rows.filter((r) => r.status === "UNMARKED").length;
  const present = rows.filter((r) => r.status === "PRESENT" || r.status === "LATE").length;

  const set = (userId: string, status: AttendanceStatus) => setRows((rs) => rs.map((r) => (r.userId === userId ? { ...r, status } : r)));
  const markRestPresent = () => setRows((rs) => rs.map((r) => (r.status === "UNMARKED" ? { ...r, status: "PRESENT" } : r)));

  async function save() {
    const marks: QueuedMark[] = changed.map((r) => ({ userId: r.userId, status: r.status, note: r.note, rowVersion: r.rowVersion }));
    if (marks.length === 0) return;
    setSaving(true);
    const offline = typeof navigator !== "undefined" && !navigator.onLine;
    const r = offline
      ? null
      : await send<EventDetail>("PUT", `/events/${detail.id}/attendance${groupId ? `?groupId=${groupId}` : ""}`, { marks });
    setSaving(false);

    if (r?.ok) {
      setRows(initial({ ...detail, roster: r.data.roster }));
      toast.success(t("saved", { count: marks.length }));
      router.refresh();
    } else if (!r || r.problem.status === 0 || r.problem.status >= 500) {
      queueMarks(detail.id, marks);
      setRows((rs) => rs.map((row) => ({ ...row, saved: row.status })));
      toast(t("queued"), { icon: <CloudOff className="size-4" aria-hidden /> });
    } else if (r.problem.code === "stale") {
      toast.error(t("stale"));
      router.refresh();
    } else {
      toast.error(r.problem.status === 403 ? t("closed") : t("unexpected"));
    }
  }

  if (rows.length === 0) return <p className="text-muted-foreground">{t("nobody")}</p>;

  return (
    <div className="grid gap-3 pb-24">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="tabular text-sm text-muted-foreground" aria-live="polite">
          {t("progress", { marked: rows.length - unmarked, total: rows.length, present })}
        </p>
        {!readOnly && unmarked > 0 && (
          <Button type="button" variant="secondary" onClick={markRestPresent}>
            <Check className="size-4" aria-hidden />
            {unmarked === rows.length ? t("allPresent") : t("restPresent")}
          </Button>
        )}
      </div>
      {readOnly && <p className="rounded-lg bg-muted px-3 py-2 text-sm">{t("readOnly")}</p>}

      <ul className="grid divide-y divide-border rounded-[10px] border border-border bg-card">
        {rows.map((r) => (
          <li key={r.userId}>
            <fieldset className="grid gap-2 px-3 py-3 sm:grid-cols-[1fr_auto] sm:items-center sm:px-4" disabled={readOnly}>
              <legend className="sr-only">{r.displayName}</legend>
              <div className="min-w-0">
                <p className="truncate font-medium" aria-hidden>
                  {r.displayName}
                </p>
                {showGroups && r.groupName && <p className="text-xs text-muted-foreground">{r.groupName}</p>}
              </div>
              <div className="grid grid-cols-4 gap-1 rounded-lg bg-muted p-1">
                {CHOICES.map((c) => (
                  <label
                    key={c}
                    className={cn(
                      "relative flex min-h-11 cursor-pointer items-center justify-center rounded-md px-2 text-sm font-medium text-muted-foreground has-[:focus-visible]:ring-3 has-[:focus-visible]:ring-ring/50 has-[:disabled]:cursor-not-allowed",
                      r.status === c && (c === "ABSENT" ? "bg-card text-destructive shadow-sm" : "bg-card text-foreground shadow-sm"),
                    )}
                  >
                    <input
                      type="radio"
                      name={`status-${r.userId}`}
                      value={c}
                      checked={r.status === c}
                      onChange={() => set(r.userId, c)}
                      className="sr-only"
                    />
                    {t(`short.${c}`)}
                  </label>
                ))}
              </div>
            </fieldset>
          </li>
        ))}
      </ul>

      {!readOnly && (
        <div className="fixed inset-x-0 bottom-[calc(env(safe-area-inset-bottom)+4.5rem)] z-20 px-4 md:bottom-6 md:left-auto md:right-6 md:px-0">
          <Button type="button" size="lg" className="w-full shadow-lg md:w-auto" disabled={saving || changed.length === 0} onClick={save}>
            {saving && <Loader2 className="size-4 animate-spin" aria-hidden />}
            {changed.length === 0 ? t("allSaved") : t("save", { count: changed.length })}
          </Button>
        </div>
      )}
    </div>
  );
}
