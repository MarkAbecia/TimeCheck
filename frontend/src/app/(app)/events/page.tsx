import type { Metadata } from "next";
import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { CalendarDays, ChevronLeft, ChevronRight, Plus } from "lucide-react";
import { EmptyState } from "@/components/empty-state";
import { buttonVariants } from "@/components/ui/button";
import { apiGet } from "@/lib/server/api";
import { atLeast, requireSession } from "@/lib/session";
import { addDays, dayLabel, localDate, timeRange, weekStart } from "@/lib/time";
import type { EventCategory, EventSummary, GroupSummary } from "@/lib/types";
import { cn } from "@/lib/utils";

export const metadata: Metadata = { title: "Events" };

const CATEGORIES: EventCategory[] = ["DAILY", "WEEKLY", "LEADER_SCHEDULED", "ADMIN_SCHEDULED"];
const isDate = (s: unknown): s is string => typeof s === "string" && /^\d{4}-\d{2}-\d{2}$/.test(s);

export default async function EventsPage({ searchParams }: PageProps<"/events">) {
  const session = await requireSession();
  const isLeader = atLeast(session.role, "leader");
  const isAdmin = atLeast(session.role, "admin");
  const [t, sp] = await Promise.all([getTranslations("events"), searchParams]);
  const now = new Date();
  const today = localDate(now);
  const past = sp.view === "past";
  const start = past ? addDays(today, -14) : weekStart(isDate(sp.start) ? sp.start : today);
  const end = past ? addDays(today, -1) : addDays(start, 6);
  const group = typeof sp.group === "string" ? sp.group : "";
  const category = CATEGORIES.find((c) => c === sp.category);

  const q = new URLSearchParams({ from: start, to: end });
  if (group) q.set("groupId", group);
  if (category) q.set("category", category);
  if (!past) q.set("includeCancelled", "true");
  const [all, groups] = await Promise.all([
    apiGet<EventSummary[]>(`/events?${q}`),
    isAdmin ? apiGet<GroupSummary[]>("/groups") : Promise.resolve([]),
  ]);
  // The past view is the leader's to-do list: only events still waiting on marks, oldest first.
  const events = past ? all.filter((e) => e.marking && e.marking.marked < e.marking.expected).reverse() : all;

  const days = new Map<string, EventSummary[]>();
  for (const e of events) days.set(e.occursOn, [...(days.get(e.occursOn) ?? []), e]);

  const link = (p: Record<string, string | undefined>) => {
    const u = new URLSearchParams();
    for (const [k, v] of Object.entries({ group: group || undefined, category, ...p })) if (v) u.set(k, v);
    return `/events${u.size ? `?${u}` : ""}`;
  };

  return (
    <div className="grid grid-cols-1 gap-5">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="font-display text-3xl font-bold uppercase">{past ? t("pastTitle") : t("title")}</h1>
          <p className="text-muted-foreground">
            {past
              ? t("pastLead")
              : t("weekOf", { start: dayLabel(new Date(`${start}T12:00:00+08:00`)), end: dayLabel(new Date(`${end}T12:00:00+08:00`)) })}
          </p>
        </div>
        {isLeader && (
          <Link href="/events/new" className={buttonVariants()}>
            <Plus className="size-4" aria-hidden />
            {t("scheduleCta")}
          </Link>
        )}
      </div>

      <nav aria-label={t("navLabel")} className="flex flex-wrap items-center gap-2">
        {!past && (
          <>
            <Link
              href={link({ start: addDays(start, -7) })}
              className={buttonVariants({ variant: "outline", size: "icon" })}
              aria-label={t("prevWeek")}
            >
              <ChevronLeft className="size-4" aria-hidden />
            </Link>
            <Link
              href={link({})}
              className={buttonVariants({ variant: "outline", size: "sm" })}
              aria-current={start === weekStart(today) ? "page" : undefined}
            >
              {t("thisWeek")}
            </Link>
            <Link
              href={link({ start: addDays(start, 7) })}
              className={buttonVariants({ variant: "outline", size: "icon" })}
              aria-label={t("nextWeek")}
            >
              <ChevronRight className="size-4" aria-hidden />
            </Link>
          </>
        )}
        {isLeader && (
          <Link href={past ? link({}) : link({ view: "past" })} className={buttonVariants({ variant: "ghost", size: "sm" })}>
            {past ? t("backToWeek") : t("needsMarks")}
          </Link>
        )}
      </nav>

      {isAdmin && (
        <form className="flex flex-wrap items-end gap-3" action="/events">
          {past && <input type="hidden" name="view" value="past" />}
          {!past && <input type="hidden" name="start" value={start} />}
          <label className="grid gap-1.5 text-sm font-medium">
            {t("group")}
            <select name="group" defaultValue={group} className="h-11 rounded-lg border border-input bg-card px-3 text-base">
              <option value="">{t("allGroups")}</option>
              {groups.map((g) => (
                <option key={g.id} value={g.id}>
                  {g.name}
                </option>
              ))}
            </select>
          </label>
          <label className="grid gap-1.5 text-sm font-medium">
            {t("category")}
            <select name="category" defaultValue={category ?? ""} className="h-11 rounded-lg border border-input bg-card px-3 text-base">
              <option value="">{t("allCategories")}</option>
              {CATEGORIES.map((c) => (
                <option key={c} value={c}>
                  {t(`categories.${c}`)}
                </option>
              ))}
            </select>
          </label>
          <button type="submit" className={buttonVariants({ variant: "secondary" })}>
            {t("filter")}
          </button>
        </form>
      )}

      {events.length === 0 ? (
        <EmptyState
          icon={CalendarDays}
          title={past ? t("allMarkedTitle") : t("emptyTitle")}
          body={past ? t("allMarkedBody") : t("emptyBody")}
        />
      ) : (
        [...days].map(([day, list]) => (
          <section key={day} aria-labelledby={`day-${day}`} className="grid gap-2">
            <h2 id={`day-${day}`} className="flex items-center gap-2 font-semibold">
              {dayLabel(new Date(`${day}T12:00:00+08:00`))}
              {day === today && <span className="rounded-md bg-primary px-1.5 py-0.5 text-xs text-primary-foreground">{t("today")}</span>}
            </h2>
            <ul className="grid divide-y divide-border rounded-[10px] border border-border bg-card">
              {list.map((e) => (
                <li key={e.id}>
                  <Link
                    href={`/events/${e.id}${e.isChurchWide && group ? `?groupId=${group}` : ""}`}
                    className="flex min-h-14 items-center gap-3 px-4 py-2 hover:bg-muted/50 focus-visible:outline-none focus-visible:ring-3 focus-visible:ring-ring/50"
                  >
                    <span className="tabular w-36 shrink-0 text-sm text-muted-foreground">{timeRange(e.startsAt, e.endsAt)}</span>
                    <span className="min-w-0 flex-1">
                      <span className={cn("block truncate font-medium", e.isCancelled && "line-through text-muted-foreground")}>
                        {e.title}
                      </span>
                      <span className="block truncate text-sm text-muted-foreground">{e.groupName ?? t("wholeChurch")}</span>
                    </span>
                    <span className="tabular shrink-0 text-sm">
                      {e.isCancelled
                        ? t("cancelledTag")
                        : e.marking && new Date(e.startsAt) <= now
                          ? t("markedOf", { marked: e.marking.marked, total: e.marking.expected })
                          : e.myStatus && e.myStatus !== "UNMARKED"
                            ? t(`status.${e.myStatus}`)
                            : null}
                    </span>
                    <ChevronRight className="size-4 shrink-0 text-muted-foreground" aria-hidden />
                  </Link>
                </li>
              ))}
            </ul>
          </section>
        ))
      )}
    </div>
  );
}
