import type { ReportChart as Chart } from "@/lib/types";
import { formatValue } from "./format";

/**
 * One series as horizontal bars: label, bar, value. A list, so it reads the same to a screen reader and wraps on a phone.
 * Percent bars share a 0–100 scale; counts scale to the largest. The tables below hold every number.
 */
export function ReportChart({ chart, empty }: { chart: Chart; empty: string }) {
  const max = chart.unit === "percent" ? 100 : Math.max(1, ...chart.bars.map((b) => b.value));
  return (
    <figure className="grid gap-3 rounded-[10px] border border-border bg-card p-4">
      <figcaption className="font-medium">{chart.title}</figcaption>
      {chart.bars.length === 0 ? (
        <p className="text-sm text-muted-foreground">{empty}</p>
      ) : (
        <ul className="grid gap-1">
          {chart.bars.map((b) => (
            <li
              key={b.label}
              title={`${b.label}: ${formatValue(b.value, chart.unit)}`}
              className="grid min-h-7 grid-cols-[minmax(0,8rem)_minmax(0,1fr)_3.75rem] items-center gap-3 rounded-sm text-sm hover:bg-muted/50"
            >
              <span className="truncate text-muted-foreground">{b.label}</span>
              <span className="h-3 w-full" aria-hidden>
                <span
                  className="block h-full rounded-r-[4px] bg-chart-1"
                  style={{ width: `${Math.max(0, Math.min(100, (b.value / max) * 100))}%` }}
                />
              </span>
              <span className="tabular text-right">{formatValue(b.value, chart.unit)}</span>
            </li>
          ))}
        </ul>
      )}
    </figure>
  );
}
