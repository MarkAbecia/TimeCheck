import type { Metadata } from "next";
import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { ChevronLeft, FileDown } from "lucide-react";
import { EmptyState } from "@/components/empty-state";
import { DownloadButton } from "@/components/reports/export-buttons";
import { buttonVariants } from "@/components/ui/button";
import { apiGet } from "@/lib/server/api";
import { requireRole } from "@/lib/session";
import { dayLabel, shortTime } from "@/lib/time";
import type { ReportJob } from "@/lib/types";

export const metadata: Metadata = { title: "Your exports" };

const when = (iso: string) => `${dayLabel(new Date(iso))}, ${shortTime(new Date(iso))}`;

export default async function ExportsPage() {
  await requireRole("leader");
  const [t, jobs] = await Promise.all([getTranslations("reports"), apiGet<ReportJob[]>("/me/report-jobs")]);

  return (
    <div className="mx-auto grid w-full max-w-3xl grid-cols-1 gap-5">
      <div>
        <Link href="/reports" className={buttonVariants({ variant: "ghost", size: "sm", className: "mb-2 w-fit" })}>
          <ChevronLeft className="size-4" aria-hidden />
          {t("title")}
        </Link>
        <h1 className="font-display text-3xl font-bold uppercase">{t("exportsTitle")}</h1>
        <p className="text-muted-foreground">{t("exportsLead")}</p>
      </div>
      {jobs.length === 0 ? (
        <EmptyState icon={FileDown} title={t("exportsEmptyTitle")} body={t("exportsEmptyBody")} />
      ) : (
        <ul className="grid divide-y divide-border rounded-[10px] border border-border bg-card">
          {jobs.map((j) => {
            // The Worker marks a file expired within a minute of its 24 hours; the download link checks again.
            const ready = j.status === "DONE" && j.expiresAt !== null;
            const busy = j.status === "QUEUED" || j.status === "RUNNING";
            return (
              <li key={j.id} className="flex flex-wrap items-center gap-3 px-4 py-3">
                <div className="min-w-0 flex-1">
                  <p className="font-medium">
                    {j.title} <span className="text-sm font-normal text-muted-foreground">{j.format.toUpperCase()}</span>
                  </p>
                  {j.scope.length > 0 && <p className="text-sm text-muted-foreground">{j.scope.join(", ")}</p>}
                  <p className="text-sm text-muted-foreground">
                    {ready
                      ? t("readyUntil", { time: when(j.expiresAt!) })
                      : j.status === "FAILED"
                        ? (j.error ?? t("unexpected"))
                        : busy
                          ? t("making", { time: when(j.createdAt) })
                          : t("expired")}
                  </p>
                </div>
                {ready && <DownloadButton jobId={j.id} label={t("download")} />}
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}
