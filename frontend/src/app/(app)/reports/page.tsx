import type { Metadata } from "next";
import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { ChevronRight } from "lucide-react";
import { apiGet } from "@/lib/server/api";
import { requireRole } from "@/lib/session";
import type { ReportInfo } from "@/lib/types";

export const metadata: Metadata = { title: "Reports" };

export default async function ReportsPage() {
  await requireRole("leader");
  const [t, reports] = await Promise.all([getTranslations("reports"), apiGet<ReportInfo[]>("/reports")]);

  return (
    <div className="mx-auto grid w-full max-w-3xl grid-cols-1 gap-5">
      <div>
        <h1 className="font-display text-3xl font-bold uppercase">{t("title")}</h1>
        <p className="text-muted-foreground">{t("lead")}</p>
      </div>
      <ul className="grid divide-y divide-border rounded-[10px] border border-border bg-card">
        {reports.map((r) => (
          <li key={r.type}>
            <Link
              href={`/reports/${r.type}`}
              className="flex min-h-16 items-center gap-3 px-4 py-3 hover:bg-muted/50 focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none"
            >
              <span className="min-w-0 flex-1">
                <span className="block font-medium">{r.title}</span>
                <span className="block text-sm text-muted-foreground">{r.summary}</span>
              </span>
              <ChevronRight className="size-4 shrink-0 text-muted-foreground" aria-hidden />
            </Link>
          </li>
        ))}
      </ul>
      <Link href="/reports/exports" className="inline-flex min-h-11 w-fit items-center text-sm underline underline-offset-4">
        {t("exportsLink")}
      </Link>
    </div>
  );
}
