import { useTranslations } from "next-intl";
import type { AiBand as Band, AiStatus } from "@/lib/types";
import { cn } from "@/lib/utils";

const TONE: Record<Band, string> = {
  LIKELY_HUMAN: "border-success/40 text-success",
  UNCERTAIN: "border-warning/50 text-warning",
  LIKELY_AI: "border-destructive/40 text-destructive",
};

/** The AI-score band with its words, never colour alone (§11.1). Advisory only. */
export function AiBand({ band, score, status }: { band: Band | null; score: number | null; status: AiStatus }) {
  const t = useTranslations("devotions");
  if (status === "PENDING") return <span className="text-sm text-muted-foreground">{t("checking")}</span>;
  if (status === "SKIPPED") return <span className="text-sm text-muted-foreground">{t("notChecked")}</span>;
  if (status === "FAILED" || !band) return <span className="text-sm text-muted-foreground">{t("checkFailed")}</span>;
  return (
    <span
      className={cn(
        "tabular inline-flex items-center gap-1 rounded-md border px-1.5 py-0.5 text-sm font-medium whitespace-nowrap",
        TONE[band],
      )}
    >
      {t(`band.${band}`)}
      {score !== null && <span className="font-normal">{Math.round(score)}%</span>}
    </span>
  );
}
