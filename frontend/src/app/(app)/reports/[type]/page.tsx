import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { ChevronLeft } from "lucide-react";
import { ExportButtons, type ExportFilters } from "@/components/reports/export-buttons";
import { ReportChart } from "@/components/reports/report-chart";
import { ReportTable } from "@/components/reports/report-table";
import { Button, buttonVariants } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { apiGet } from "@/lib/server/api";
import { atLeast, requireRole } from "@/lib/session";
import { addDays, localDate, shortTime } from "@/lib/time";
import type { EventType, GroupSummary, Report, ReportInfo } from "@/lib/types";

export const metadata: Metadata = { title: "Report" };

const isDate = (s: unknown): s is string => typeof s === "string" && /^\d{4}-\d{2}-\d{2}$/.test(s);
const span = (from: string, to: string) => (Date.parse(to) - Date.parse(from)) / 86_400_000;
const CATEGORIES = ["DAILY", "WEEKLY", "LEADER_SCHEDULED", "ADMIN_SCHEDULED"] as const;
const select =
  "h-11 w-full min-w-0 rounded-lg border border-input bg-card px-3 text-base outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 dark:bg-input/30";

export default async function ReportPage({ params, searchParams }: PageProps<"/reports/[type]">) {
  const session = await requireRole("leader");
  const isAdmin = atLeast(session.role, "admin");
  const [{ type }, sp, t] = await Promise.all([params, searchParams, getTranslations("reports")]);
  const info = (await apiGet<ReportInfo[]>("/reports")).find((r) => r.type === type);
  if (!info) notFound();

  // Filters live in the URL (a plain GET form), so a report can be bookmarked and the form works before any script loads.
  const today = localDate(new Date());
  let to = isDate(sp.to) && sp.to <= today ? sp.to : today;
  let from = isDate(sp.from) ? sp.from : addDays(to, -29);
  const badRange = span(from, to) < 0 || span(from, to) > 366;
  if (badRange) [from, to] = [addDays(today, -29), today];
  const group = typeof sp.group === "string" ? sp.group : "";
  const events = typeof sp.events === "string" ? sp.events : "";

  const filters: ExportFilters = {};
  if (info.usesPeriod) Object.assign(filters, { from, to });
  if (group) filters.groupId = group;
  if (info.usesEventFilter && events.startsWith("type:")) filters.eventTypeId = events.slice(5);
  if (info.usesEventFilter && events.startsWith("category:")) filters.category = events.slice(9);

  const [report, groups, types] = await Promise.all([
    apiGet<Report>(`/reports/${type}?${new URLSearchParams(filters as Record<string, string>)}`),
    apiGet<GroupSummary[]>("/groups"),
    info.usesEventFilter ? apiGet<EventType[]>("/event-types") : Promise.resolve([]),
  ]);
  const showGroups = isAdmin || groups.length > 1;

  return (
    <div className="grid grid-cols-1 gap-5">
      <div>
        <Link href="/reports" className={buttonVariants({ variant: "ghost", size: "sm", className: "mb-2 w-fit" })}>
          <ChevronLeft className="size-4" aria-hidden />
          {t("title")}
        </Link>
        <h1 className="font-display text-3xl font-bold uppercase">{report.title}</h1>
        <ul className="mt-1 flex flex-wrap gap-x-4 gap-y-1 text-muted-foreground" aria-label={t("showing")}>
          {report.scope.map((s) => (
            <li key={s}>{s}</li>
          ))}
        </ul>
        <p className="text-sm text-muted-foreground">{t("updated", { time: shortTime(new Date(report.generatedAt)) })}</p>
      </div>

      {(info.usesPeriod || showGroups || info.usesEventFilter) && (
        <form
          method="get"
          aria-label={t("filters")}
          className="grid gap-3 rounded-[10px] border border-border bg-card p-4 sm:grid-cols-2 lg:grid-cols-[repeat(auto-fill,minmax(13rem,1fr))] lg:items-end"
        >
          {info.usesPeriod && (
            <>
              <div className="grid gap-1.5">
                <Label htmlFor="from">{t("from")}</Label>
                <Input id="from" name="from" type="date" defaultValue={from} max={today} required />
              </div>
              <div className="grid gap-1.5">
                <Label htmlFor="to">{t("to")}</Label>
                <Input id="to" name="to" type="date" defaultValue={to} max={today} required />
              </div>
            </>
          )}
          {showGroups && (
            <div className="grid gap-1.5">
              <Label htmlFor="group">{t("group")}</Label>
              <select id="group" name="group" defaultValue={group} className={select}>
                <option value="">{isAdmin ? t("allGroups") : t("yourGroups")}</option>
                {groups.map((g) => (
                  <option key={g.id} value={g.id}>
                    {g.name}
                  </option>
                ))}
              </select>
            </div>
          )}
          {info.usesEventFilter && (
            <div className="grid gap-1.5">
              <Label htmlFor="events">{t("events")}</Label>
              <select id="events" name="events" defaultValue={events} className={select}>
                <option value="">{t("allEvents")}</option>
                <optgroup label={t("categories")}>
                  {CATEGORIES.map((c) => (
                    <option key={c} value={`category:${c}`}>
                      {t(`category.${c}`)}
                    </option>
                  ))}
                </optgroup>
                <optgroup label={t("eventTypes")}>
                  {types.map((e) => (
                    <option key={e.id} value={`type:${e.id}`}>
                      {e.name}
                    </option>
                  ))}
                </optgroup>
              </select>
            </div>
          )}
          <Button type="submit" className="w-fit">
            {t("show")}
          </Button>
        </form>
      )}
      {badRange && <p className="rounded-lg border border-border bg-muted/40 px-3 py-2 text-sm">{t("badRange")}</p>}

      <ExportButtons type={type} filters={filters} />
      {report.chart && <ReportChart chart={report.chart} empty={t("nothing")} />}
      {report.sections.map((s) => (
        <ReportTable key={s.key} section={s} empty={t("nothing")} more={t("more")} />
      ))}
    </div>
  );
}
