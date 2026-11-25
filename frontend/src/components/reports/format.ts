import type { ReportCell, ReportColumn } from "@/lib/types";

const number = new Intl.NumberFormat("en", { maximumFractionDigits: 1 });
const date = new Intl.DateTimeFormat("en-GB", { day: "numeric", month: "short", year: "numeric", timeZone: "UTC" });

/** How a report cell reads on screen; the API's CSV/XLSX/PDF use the same rules. Null is "—". */
export function cellText(value: ReportCell, kind: ReportColumn["kind"]): string {
  if (value === null || value === "") return "—";
  if (typeof value === "number") return kind === "percent" ? `${number.format(value)}%` : number.format(value);
  if (kind === "date" && /^\d{4}-\d{2}-\d{2}$/.test(value)) return date.format(new Date(`${value}T00:00:00Z`));
  return value;
}

export const formatValue = (value: number, unit: "percent" | "count") =>
  unit === "percent" ? `${number.format(value)}%` : number.format(value);
