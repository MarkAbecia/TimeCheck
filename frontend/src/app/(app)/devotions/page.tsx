import type { Metadata } from "next";
import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { BookOpenCheck, ChevronRight, Users } from "lucide-react";
import { AiBand } from "@/components/devotions/ai-band";
import { DevotionForm } from "@/components/devotions/devotion-form";
import { EmptyState } from "@/components/empty-state";
import { buttonVariants } from "@/components/ui/button";
import { apiGet } from "@/lib/server/api";
import { atLeast, requireSession } from "@/lib/session";
import { dayLabel, shortTime } from "@/lib/time";
import type { MyDevotions } from "@/lib/types";

export const metadata: Metadata = { title: "Devotions" };

const noon = (date: string) => new Date(`${date}T12:00:00+08:00`);

export default async function DevotionsPage() {
  const session = await requireSession();
  const [t, mine] = await Promise.all([getTranslations("devotions"), apiGet<MyDevotions>("/me/devotions")]);

  return (
    <div className="mx-auto grid w-full max-w-2xl grid-cols-1 gap-6">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="font-display text-3xl font-bold uppercase">{t("title")}</h1>
          <p className="text-muted-foreground">{t("streak", { count: mine.streak })}</p>
        </div>
        {atLeast(session.role, "leader") && (
          <Link href="/devotions/group" className={buttonVariants({ variant: "outline" })}>
            <Users className="size-4" aria-hidden />
            {t("reviewGroup")}
          </Link>
        )}
      </div>

      {mine.open.map((s) => (
        <section
          key={`${s.date}-${s.slot}`}
          aria-labelledby={`slot-${s.date}-${s.slot}`}
          className="grid gap-3 rounded-[10px] border border-border bg-card p-4"
        >
          <div className="flex flex-wrap items-baseline justify-between gap-2">
            <h2 id={`slot-${s.date}-${s.slot}`} className="font-display text-2xl font-bold uppercase">
              {t(`slot.${s.slot}`)}
            </h2>
            <p className="text-sm text-muted-foreground">
              {s.devotion ? t("submittedAt", { time: shortTime(new Date(s.devotion.submittedAt)) }) : dayLabel(noon(s.date))},{" "}
              {t("closes", { time: shortTime(new Date(s.closes)) })}
            </p>
          </div>
          <DevotionForm key={s.devotion?.id ?? "new"} date={s.date} slot={s.slot} current={s.devotion} />
        </section>
      ))}

      <section aria-labelledby="recent" className="grid grid-cols-1 gap-2">
        <h2 id="recent" className="font-semibold">
          {t("recent")}
        </h2>
        {mine.recent.length === 0 ? (
          <EmptyState icon={BookOpenCheck} title={t("emptyTitle")} body={t("emptyBody")} />
        ) : (
          <ul className="grid grid-cols-1 divide-y divide-border rounded-[10px] border border-border bg-card">
            {mine.recent.map((d) => (
              <li key={d.id}>
                <Link
                  href={`/devotions/${d.id}`}
                  className="flex min-h-14 items-center gap-3 px-4 py-2 hover:bg-muted/50 focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none"
                >
                  <span className="tabular w-28 shrink-0 text-sm text-muted-foreground">
                    {dayLabel(noon(d.date))}
                    <span className="block">{t(`slot.${d.slot}`)}</span>
                  </span>
                  <span className="min-w-0 flex-1 truncate">{d.text ?? t("photoOnly")}</span>
                  {mine.showsScores && (
                    <AiBand band={d.band} score={Math.max(d.aiTextScore ?? 0, d.aiImageScore ?? 0)} status={d.aiStatus} />
                  )}
                  <ChevronRight className="size-4 shrink-0 text-muted-foreground" aria-hidden />
                </Link>
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  );
}
