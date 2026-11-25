import type { ReportSection } from "@/lib/types";
import { cn } from "@/lib/utils";
import { cellText } from "./format";

const numeric = (kind: string) => kind === "number" || kind === "percent";

/**
 * A report table (§11.3 data tables): stacked cards with the key fields on a phone, tap for the rest; a full table, scrolling
 * sideways inside itself, from tablet up.
 */
export function ReportTable({ section, empty, more }: { section: ReportSection; empty: string; more: string }) {
  const [first, ...rest] = section.columns;
  const key = rest.slice(0, 3);
  const extra = rest.slice(3);
  const id = `section-${section.key}`;

  return (
    <section aria-labelledby={id} className="grid gap-2">
      <h2 id={id} className="font-display text-xl font-bold uppercase">
        {section.title}
      </h2>
      {section.note && <p className="text-sm text-muted-foreground">{section.note}</p>}

      {section.rows.length === 0 ? (
        <p className="rounded-[10px] border border-border bg-card p-4 text-sm text-muted-foreground">{empty}</p>
      ) : (
        <>
          <ul className="grid gap-2 md:hidden">
            {section.rows.map((row, r) => (
              <li key={r} className="rounded-[10px] border border-border bg-card p-3">
                <p className="font-medium">{cellText(row[0], first.kind)}</p>
                <dl className="mt-1 grid grid-cols-3 gap-2 text-sm">
                  {key.map((c, i) => (
                    <div key={c.key}>
                      <dt className="text-muted-foreground">{c.label}</dt>
                      <dd className="tabular">{cellText(row[i + 1], c.kind)}</dd>
                    </div>
                  ))}
                </dl>
                {extra.length > 0 && (
                  <details className="mt-2 text-sm">
                    <summary className="flex min-h-11 cursor-pointer items-center text-muted-foreground underline-offset-4 hover:underline">
                      {more}
                    </summary>
                    <dl className="grid grid-cols-2 gap-2">
                      {extra.map((c, i) => (
                        <div key={c.key}>
                          <dt className="text-muted-foreground">{c.label}</dt>
                          <dd className="tabular">{cellText(row[i + 4], c.kind)}</dd>
                        </div>
                      ))}
                    </dl>
                  </details>
                )}
              </li>
            ))}
          </ul>

          {/* Focusable so keyboard users can scroll a wide table sideways (WCAG 2.1.1). */}
          <div
            role="region"
            aria-labelledby={id}
            tabIndex={0}
            className="hidden overflow-x-auto rounded-[10px] border border-border bg-card focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none md:block"
          >
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-border">
                  {section.columns.map((c) => (
                    <th
                      key={c.key}
                      scope="col"
                      className={cn(
                        "px-3 py-2 font-medium whitespace-nowrap text-muted-foreground",
                        numeric(c.kind) ? "text-right" : "text-left",
                      )}
                    >
                      {c.label}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-border">
                {section.rows.map((row, r) => (
                  <tr key={r} className="hover:bg-muted/40">
                    {section.columns.map((c, i) =>
                      i === 0 ? (
                        <th key={c.key} scope="row" className="px-3 py-2 text-left font-medium">
                          {cellText(row[i], c.kind)}
                        </th>
                      ) : (
                        <td key={c.key} className={cn("px-3 py-2", numeric(c.kind) ? "tabular text-right" : "text-left")}>
                          {cellText(row[i], c.kind)}
                        </td>
                      ),
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}
    </section>
  );
}
