import type { Metadata } from "next";
import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { ChevronDown, ScrollText, X } from "lucide-react";
import { EmptyState } from "@/components/empty-state";
import { Button, buttonVariants } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { apiGet } from "@/lib/server/api";
import { requireRole } from "@/lib/session";
import { addDays, dayLabel, shortTime, toInstant } from "@/lib/time";

export const metadata: Metadata = { title: "Audit log" };

type AuditEntry = {
  id: string;
  actorId: string | null;
  actorName: string | null;
  action: string;
  entityType: string | null;
  entityId: string | null;
  entityName: string | null;
  before: string | null;
  after: string | null;
  occurredAt: string;
};

const DATE = /^\d{4}-\d{2}-\d{2}$/;
const UUID = /^[0-9a-f-]{36}$/i;
const selectClass =
  "h-11 w-full min-w-0 rounded-lg border border-input bg-card px-3 text-base outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 dark:bg-input/30";

/** Super Admin audit viewer (§6.10): who did what to whom, and when. Filters and paging are plain links and a GET form. */
export default async function AuditPage({ searchParams }: PageProps<"/audit">) {
  await requireRole("super_admin");
  const sp = await searchParams;
  const one = (k: string) => (typeof sp[k] === "string" ? (sp[k] as string) : "");
  const f = {
    action: one("action").slice(0, 100),
    actor: UUID.test(one("actor")) ? one("actor") : "",
    entityType: one("entityType").slice(0, 100),
    entityId: one("entityId").slice(0, 100),
    from: DATE.test(one("from")) ? one("from") : "",
    to: DATE.test(one("to")) ? one("to") : "",
    cursor: one("cursor"),
  };

  const api = new URLSearchParams({ limit: "50" });
  if (f.action) api.set("action", f.action);
  if (f.actor) api.set("actorId", f.actor);
  if (f.entityType && f.entityId) {
    api.set("entityType", f.entityType);
    api.set("entityId", f.entityId);
  }
  if (f.from) api.set("from", toInstant(f.from, "00:00"));
  if (f.to) api.set("to", toInstant(addDays(f.to, 1), "00:00")); // the whole "to" day, church time
  if (f.cursor) api.set("cursor", f.cursor);

  const [t, page, actions] = await Promise.all([
    getTranslations("audit"),
    apiGet<{ items: AuditEntry[]; nextCursor: string | null }>(`/admin/audit-logs?${api}`),
    apiGet<string[]>("/admin/audit-actions"),
  ]);

  // Links keep the other filters; the cursor is dropped whenever a filter changes.
  const href = (change: Partial<typeof f>) => {
    const next = { ...f, cursor: "", ...change };
    const q = new URLSearchParams(Object.entries(next).filter(([, v]) => v) as [string, string][]);
    return q.size ? `/audit?${q}` : "/audit";
  };
  const actorName = f.actor ? (page.items.find((i) => i.actorId === f.actor)?.actorName ?? t("someone")) : "";
  const entityName = f.entityId ? (page.items.find((i) => i.entityId === f.entityId)?.entityName ?? f.entityId) : "";

  return (
    <div className="grid gap-5">
      <div>
        <h1 className="font-display text-3xl font-bold uppercase">{t("title")}</h1>
        <p className="text-muted-foreground">{t("lead")}</p>
      </div>

      {/* Folded on phones so the log starts on the first screen; always open from 40rem (globals.css .report-filters). */}
      <details open={Boolean(f.action || f.from || f.to)} className="report-filters group rounded-[10px] border border-border bg-card">
        <summary className="flex min-h-11 cursor-pointer list-none items-center justify-between gap-2 rounded-[10px] px-4 font-medium outline-none focus-visible:ring-3 focus-visible:ring-ring/50 [&::-webkit-details-marker]:hidden">
          {t("filters")}
          <ChevronDown className="size-4 group-open:rotate-180" aria-hidden />
        </summary>
        <form
          action="/audit"
          aria-label={t("filters")}
          className="grid gap-3 px-4 pt-1 pb-4 sm:grid-cols-2 sm:pt-4 lg:grid-cols-[2fr_1fr_1fr_auto] lg:items-end"
        >
          {f.actor && <input type="hidden" name="actor" value={f.actor} />}
          {f.entityId && (
            <>
              <input type="hidden" name="entityType" value={f.entityType} />
              <input type="hidden" name="entityId" value={f.entityId} />
            </>
          )}
          <div className="grid gap-1.5 sm:col-span-2 lg:col-span-1">
            <Label htmlFor="audit-action">{t("action")}</Label>
            <select id="audit-action" name="action" defaultValue={f.action} className={selectClass}>
              <option value="">{t("allActions")}</option>
              {actions.map((a) => (
                <option key={a} value={a}>
                  {a}
                </option>
              ))}
            </select>
          </div>
          <div className="grid gap-1.5">
            <Label htmlFor="audit-from">{t("from")}</Label>
            <Input id="audit-from" name="from" type="date" defaultValue={f.from} />
          </div>
          <div className="grid gap-1.5">
            <Label htmlFor="audit-to">{t("to")}</Label>
            <Input id="audit-to" name="to" type="date" defaultValue={f.to} min={f.from || undefined} />
          </div>
          <Button type="submit" className="sm:col-span-2 lg:col-span-1">
            {t("apply")}
          </Button>
        </form>
      </details>

      {(f.actor || f.entityId) && (
        <ul className="flex flex-wrap gap-2" aria-label={t("activeFilters")}>
          {f.actor && (
            <li>
              <Link href={href({ actor: "" })} className={buttonVariants({ variant: "secondary", size: "sm" })}>
                {t("byFilter", { name: actorName })}
                <X className="size-4" aria-label={t("remove")} />
              </Link>
            </li>
          )}
          {f.entityId && (
            <li>
              <Link href={href({ entityType: "", entityId: "" })} className={buttonVariants({ variant: "secondary", size: "sm" })}>
                {t("aboutFilter", { name: entityName })}
                <X className="size-4" aria-label={t("remove")} />
              </Link>
            </li>
          )}
        </ul>
      )}

      {page.items.length === 0 ? (
        <EmptyState icon={ScrollText} title={t("emptyTitle")} body={t("emptyBody")} />
      ) : (
        <ol className="grid divide-y divide-border rounded-[10px] border border-border bg-card">
          {page.items.map((e) => {
            const at = new Date(e.occurredAt);
            return (
              <li key={e.id} className="grid gap-1 px-4 py-3">
                <div className="flex flex-wrap items-baseline justify-between gap-x-4">
                  <span className="font-medium break-all">{e.action}</span>
                  <time dateTime={e.occurredAt} className="text-sm text-muted-foreground">
                    {dayLabel(at)} {shortTime(at)}
                  </time>
                </div>
                <p className="flex flex-wrap gap-x-4 text-sm text-muted-foreground">
                  <span>
                    {t("by")}{" "}
                    {e.actorId ? (
                      <Link href={href({ actor: e.actorId })} className="text-foreground underline underline-offset-4">
                        {e.actorName ?? t("someone")}
                      </Link>
                    ) : (
                      t("system")
                    )}
                  </span>
                  {e.entityType && e.entityId && (
                    <span>
                      {t("about")}{" "}
                      <Link
                        href={href({ entityType: e.entityType, entityId: e.entityId })}
                        className="text-foreground underline underline-offset-4"
                      >
                        {e.entityName ?? `${e.entityType} ${e.entityId.slice(0, 8)}`}
                      </Link>
                    </span>
                  )}
                </p>
                {(e.before || e.after) && (
                  <details className="text-sm">
                    <summary className="w-fit cursor-pointer text-muted-foreground">{t("changes")}</summary>
                    <dl className="mt-2 grid gap-2">
                      {e.before && (
                        <div>
                          <dt className="text-muted-foreground">{t("before")}</dt>
                          <dd>
                            <pre className="overflow-x-auto rounded-lg bg-muted/60 p-2 text-xs">{pretty(e.before)}</pre>
                          </dd>
                        </div>
                      )}
                      {e.after && (
                        <div>
                          <dt className="text-muted-foreground">{t("after")}</dt>
                          <dd>
                            <pre className="overflow-x-auto rounded-lg bg-muted/60 p-2 text-xs">{pretty(e.after)}</pre>
                          </dd>
                        </div>
                      )}
                    </dl>
                  </details>
                )}
              </li>
            );
          })}
        </ol>
      )}

      <nav aria-label={t("pages")} className="flex flex-wrap gap-2">
        {f.cursor && (
          <Link href={href({})} className={buttonVariants({ variant: "outline" })}>
            {t("newest")}
          </Link>
        )}
        {page.nextCursor && (
          <Link href={href({ cursor: page.nextCursor })} className={buttonVariants({ variant: "outline" })}>
            {t("older")}
          </Link>
        )}
      </nav>
    </div>
  );
}

function pretty(json: string) {
  try {
    return JSON.stringify(JSON.parse(json), null, 2);
  } catch {
    return json;
  }
}
