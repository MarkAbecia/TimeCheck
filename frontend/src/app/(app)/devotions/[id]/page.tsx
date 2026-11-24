import type { Metadata } from "next";
import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { ChevronLeft } from "lucide-react";
import { AiBand } from "@/components/devotions/ai-band";
import { ReviewForm } from "@/components/devotions/review-form";
import { buttonVariants } from "@/components/ui/button";
import { apiGet } from "@/lib/server/api";
import { requireSession } from "@/lib/session";
import { dayLabel, shortTime } from "@/lib/time";
import type { DevotionDetail } from "@/lib/types";

export const metadata: Metadata = { title: "Devotion" };

export default async function DevotionPage({ params }: PageProps<"/devotions/[id]">) {
  await requireSession();
  const [{ id }, t] = await Promise.all([params, getTranslations("devotions")]);
  const d = await apiGet<DevotionDetail>(`/devotions/${id}`);
  const v = d.devotion;

  return (
    <div className="mx-auto grid w-full max-w-2xl grid-cols-1 gap-5">
      <Link
        href={d.canReview ? "/devotions/group" : "/devotions"}
        className={buttonVariants({ variant: "ghost", size: "sm", className: "w-fit" })}
      >
        <ChevronLeft className="size-4" aria-hidden />
        {d.canReview ? t("groupTitle") : t("title")}
      </Link>

      <div className="grid gap-1">
        <p className="text-sm text-muted-foreground">
          {d.canReview ? `${d.memberName}${d.groupName ? `, ${d.groupName}` : ""}` : t("yours")}
        </p>
        <h1 className="font-display text-3xl font-bold uppercase">
          {t(`slot.${v.slot}`)}, {dayLabel(new Date(`${v.date}T12:00:00+08:00`))}
        </h1>
        <p className="text-sm text-muted-foreground">{t("submittedAt", { time: shortTime(new Date(v.submittedAt)) })}</p>
      </div>

      {v.text && <p className="max-w-prose whitespace-pre-line">{v.text}</p>}
      {d.imageUrl && (
        // eslint-disable-next-line @next/next/no-img-element -- a 5-minute signed URL; next/image would cache it past expiry
        <img
          src={d.imageUrl}
          alt={t("photoAlt", { name: d.memberName })}
          className="max-h-[70vh] w-auto rounded-[10px] border border-border object-contain"
        />
      )}

      {d.showsScores && (
        <section aria-labelledby="ai" className="grid gap-2 rounded-[10px] border border-border bg-card p-4">
          <h2 id="ai" className="font-semibold">
            {t("aiTitle")}
          </h2>
          <div>
            <AiBand band={v.band} score={v.band ? Math.max(v.aiTextScore ?? 0, v.aiImageScore ?? 0) : null} status={v.aiStatus} />
          </div>
          {(v.aiTextScore !== null || v.aiImageScore !== null) && (
            <dl className="tabular flex flex-wrap gap-x-6 gap-y-1 text-sm">
              {v.aiTextScore !== null && (
                <div className="flex gap-2">
                  <dt className="text-muted-foreground">{t("textScore")}</dt>
                  <dd>{Math.round(v.aiTextScore)}%</dd>
                </div>
              )}
              {v.aiImageScore !== null && (
                <div className="flex gap-2">
                  <dt className="text-muted-foreground">{t("imageScore")}</dt>
                  <dd>{Math.round(v.aiImageScore)}%</dd>
                </div>
              )}
            </dl>
          )}
          <p className="text-sm text-muted-foreground">{t("disclaimer")}</p>
        </section>
      )}

      {d.canReview && (
        <section aria-labelledby="review" className="grid gap-2">
          <h2 id="review" className="font-semibold">
            {t("reviewTitle")}
          </h2>
          {d.reviewedAt && <p className="text-sm text-muted-foreground">{t("reviewedOn", { date: dayLabel(new Date(d.reviewedAt)) })}</p>}
          <ReviewForm id={v.id} note={d.reviewNote} reviewed={v.reviewed} />
        </section>
      )}
    </div>
  );
}
