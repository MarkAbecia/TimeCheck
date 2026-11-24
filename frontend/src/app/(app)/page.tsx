import type { Metadata } from "next";
import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { CalendarCheck, CircleAlert } from "lucide-react";
import { EventPass, type Pass } from "@/components/event-pass";
import { EmptyState } from "@/components/empty-state";
import { toPass } from "@/lib/passes";
import { apiGet } from "@/lib/server/api";
import { requireSession } from "@/lib/session";
import { dayLabel } from "@/lib/time";
import type { Today } from "@/lib/types";

export const metadata: Metadata = { title: "Today" };

function PassList({ passes }: { passes: Pass[] }) {
  return (
    <ul className="grid gap-3 lg:grid-cols-2 lg:gap-4">
      {passes.map((p) => (
        <li key={p.id}>
          <EventPass pass={p} />
        </li>
      ))}
    </ul>
  );
}

const sectionHeading = "mx-1 mb-2.5 font-display text-[0.9375rem] font-bold tracking-[0.14em] text-muted-foreground uppercase";

export default async function TodayPage() {
  await requireSession();
  const [t, tp, data] = await Promise.all([getTranslations("today"), getTranslations("pass"), apiGet<Today>("/me/today")]);
  const members = (n: number) => tp("members", { count: n });
  const today = data.today.map((e) => toPass(e, "today", { members }));
  const upcoming = data.upcoming.map((e) => toPass(e, "upcoming", { members }));
  const date = dayLabel(new Date(`${data.date}T12:00:00+08:00`));

  const unmarked = data.unmarkedPast > 0 && (
    <Link
      href="/events?view=past"
      className="flex min-h-11 items-center gap-2 rounded-[10px] border border-warning/40 bg-warning/10 px-4 py-2.5 text-sm font-medium"
    >
      <CircleAlert className="size-4 shrink-0" aria-hidden />
      {t("unmarked", { count: data.unmarkedPast })}
    </Link>
  );

  if (today.length + upcoming.length === 0) {
    return (
      <div className="flex flex-col gap-4">
        <h1 className={sectionHeading}>{t("heading", { date })}</h1>
        {unmarked}
        <EmptyState icon={CalendarCheck} title={t("emptyTitle")} body={t("emptyBody")} />
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-7">
      <section aria-labelledby="today-heading" className="flex flex-col gap-3">
        <h1 id="today-heading" className={`${sectionHeading} mb-0`}>
          {t("heading", { date })}
        </h1>
        {unmarked}
        {today.length > 0 ? <PassList passes={today} /> : <p className="mx-1 text-muted-foreground">{t("nothingToday")}</p>}
      </section>
      {upcoming.length > 0 && (
        <section aria-labelledby="upcoming-heading">
          <h2 id="upcoming-heading" className={sectionHeading}>
            {t("comingUp")}
          </h2>
          <PassList passes={upcoming} />
        </section>
      )}
    </div>
  );
}
