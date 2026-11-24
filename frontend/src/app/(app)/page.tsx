import type { Metadata } from "next";
import { getFormatter, getTranslations } from "next-intl/server";
import { CalendarCheck } from "lucide-react";
import { EventPass, type Pass } from "@/components/event-pass";
import { EmptyState } from "@/components/empty-state";
import { demoToday } from "@/lib/demo-today";
import { getSession } from "@/lib/session";

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
  const [t, format, session] = await Promise.all([getTranslations("today"), getFormatter(), getSession()]);
  const { today, upcoming } = demoToday(session.role);
  const now = new Date();
  // "Tue 24 Nov": day before month, as Filipinos write it.
  const date = `${format.dateTime(now, { weekday: "short" })} ${format.dateTime(now, { day: "numeric" })} ${format.dateTime(now, { month: "short" })}`;

  if (today.length + upcoming.length === 0) {
    return (
      <>
        <h1 className={sectionHeading}>{t("heading", { date })}</h1>
        <EmptyState icon={CalendarCheck} title={t("emptyTitle")} body={t("emptyBody")} />
      </>
    );
  }

  return (
    <div className="flex flex-col gap-7">
      <section aria-labelledby="today-heading">
        <h1 id="today-heading" className={sectionHeading}>
          {t("heading", { date })}
        </h1>
        <PassList passes={today} />
      </section>
      {upcoming.length > 0 && (
        <section aria-labelledby="upcoming-heading">
          <h2 id="upcoming-heading" className={sectionHeading}>
            {t("comingUp")}
          </h2>
          <PassList passes={upcoming} />
        </section>
      )}
      <p className="mx-1 text-sm text-muted-foreground">{t("sampleNote")}</p>
    </div>
  );
}
