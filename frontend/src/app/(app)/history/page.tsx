import type { Metadata } from "next";
import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { History } from "lucide-react";
import { EmptyState } from "@/components/empty-state";
import { apiGet } from "@/lib/server/api";
import { requireSession } from "@/lib/session";
import { dayLabel, shortTime } from "@/lib/time";
import type { MyAttendance } from "@/lib/types";
import { cn } from "@/lib/utils";

export const metadata: Metadata = { title: "My attendance" };

export default async function HistoryPage() {
  await requireSession();
  const [t, h] = await Promise.all([getTranslations("history"), apiGet<MyAttendance>("/me/attendance")]);
  const rate = h.counted > 0 ? Math.round((h.attended / h.counted) * 100) : null;

  return (
    <div className="mx-auto grid w-full max-w-3xl grid-cols-1 gap-5">
      <div>
        <h1 className="font-display text-3xl font-bold uppercase">{t("title")}</h1>
        <p className="text-muted-foreground">{t("lead")}</p>
      </div>

      <dl className="grid grid-cols-2 gap-3">
        <div className="rounded-[10px] border border-border bg-card p-4">
          <dt className="text-sm text-muted-foreground">{t("rate")}</dt>
          <dd className="tabular font-display text-4xl font-bold">{rate === null ? "–" : `${rate}%`}</dd>
          <dd className="text-sm text-muted-foreground">{t("rateOf", { attended: h.attended, counted: h.counted })}</dd>
        </div>
        <div className="rounded-[10px] border border-border bg-card p-4">
          <dt className="text-sm text-muted-foreground">{t("streak")}</dt>
          <dd className="tabular font-display text-4xl font-bold">{h.morningPrayerStreak}</dd>
          <dd className="text-sm text-muted-foreground">{t("streakUnit", { count: h.morningPrayerStreak })}</dd>
        </div>
      </dl>

      {h.items.length === 0 ? (
        <EmptyState icon={History} title={t("emptyTitle")} body={t("emptyBody")} />
      ) : (
        <ul className="grid divide-y divide-border rounded-[10px] border border-border bg-card">
          {h.items.map((i) => (
            <li key={i.eventId}>
              <Link
                href={`/events/${i.eventId}`}
                className="flex min-h-14 items-center gap-3 px-4 py-2 hover:bg-muted/50 focus-visible:outline-none focus-visible:ring-3 focus-visible:ring-ring/50"
              >
                <span className="tabular w-28 shrink-0 text-sm text-muted-foreground">
                  {dayLabel(new Date(i.startsAt))}
                  <span className="block">{shortTime(new Date(i.startsAt))}</span>
                </span>
                <span className="min-w-0 flex-1 truncate font-medium">{i.title}</span>
                <span
                  className={cn(
                    "shrink-0 text-sm font-medium",
                    (i.status === "ABSENT" || i.status === "UNMARKED") && "text-muted-foreground",
                  )}
                >
                  {t(`status.${i.status}`)}
                </span>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
