"use client";

import { Download, Loader2 } from "lucide-react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { useState } from "react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { send } from "@/lib/api-client";
import type { ReportJob } from "@/lib/types";

export type ExportFilters = { from?: string; to?: string; groupId?: string; eventTypeId?: string; category?: string };

/** Opens a finished export through a short-lived private link (§6.8). */
export async function download(jobId: string, t: (key: string) => string) {
  const r = await send<{ url: string }>("GET", `/me/report-jobs/${jobId}/download`);
  if (r.ok) window.location.assign(r.data.url);
  else toast.error(r.problem.status === 0 ? t("offline") : (r.problem.title ?? t("unexpected")));
}

/** CSV, XLSX or PDF of the report as filtered. Small ones download now; big ones are made in the background and notify. */
export function ExportButtons({ type, filters }: { type: string; filters: ExportFilters }) {
  const t = useTranslations("reports");
  const router = useRouter();
  const [busy, setBusy] = useState<string | null>(null);

  async function exportAs(format: "csv" | "xlsx" | "pdf") {
    setBusy(format);
    const r = await send<ReportJob>("POST", `/reports/${type}/exports`, { format, filters });
    if (r.ok && r.data.status === "DONE") await download(r.data.id, t);
    else if (r.ok)
      toast.success(t("exportQueued"), {
        action: { label: t("exportsLink"), onClick: () => router.push("/reports/exports") },
      });
    else
      toast.error(
        r.problem.status === 0 ? t("offline") : r.problem.status === 429 ? t("tooManyExports") : (r.problem.title ?? t("unexpected")),
      );
    setBusy(null);
  }

  return (
    <div className="flex flex-wrap items-center gap-2">
      <span className="text-sm text-muted-foreground">{t("export")}</span>
      {(["csv", "xlsx", "pdf"] as const).map((f) => (
        <Button key={f} type="button" variant="outline" size="sm" disabled={busy !== null} onClick={() => exportAs(f)}>
          {busy === f ? <Loader2 className="size-4 animate-spin" aria-hidden /> : <Download className="size-4" aria-hidden />}
          {f.toUpperCase()}
        </Button>
      ))}
      <Link href="/reports/exports" className="inline-flex min-h-11 items-center text-sm underline underline-offset-4">
        {t("exportsLink")}
      </Link>
    </div>
  );
}

/** One row's download in the exports list. */
export function DownloadButton({ jobId, label }: { jobId: string; label: string }) {
  const t = useTranslations("reports");
  const [busy, setBusy] = useState(false);
  return (
    <Button
      type="button"
      size="sm"
      variant="outline"
      disabled={busy}
      onClick={async () => {
        setBusy(true);
        await download(jobId, t);
        setBusy(false);
      }}
    >
      {busy ? <Loader2 className="size-4 animate-spin" aria-hidden /> : <Download className="size-4" aria-hidden />}
      {label}
    </Button>
  );
}
