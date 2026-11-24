import type { Metadata } from "next";
import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { ChevronLeft, ChevronRight, Users } from "lucide-react";
import { AiBand } from "@/components/devotions/ai-band";
import { EmptyState } from "@/components/empty-state";
import { buttonVariants } from "@/components/ui/button";
import { apiGet } from "@/lib/server/api";
import { atLeast, requireRole } from "@/lib/session";
import { addDays, dayLabel, localDate } from "@/lib/time";
import type { DevotionBrief, GroupDevotions, GroupSummary } from "@/lib/types";

export const metadata: Metadata = { title: "Group devotions" };

const isDate = (s: unknown): s is string => typeof s === "string" && /^\d{4}-\d{2}-\d{2}$/.test(s);

export default async function GroupDevotionsPage({ searchParams }: PageProps<"/devotions/group">) {
  const session = await requireRole("leader");
  const isAdmin = atLeast(session.role, "admin");
  const [t, sp] = await Promise.all([getTranslations("devotions"), searchParams]);
  const today = localDate(new Date());
  const date = isDate(sp.date) && sp.date <= today ? sp.date : today;
  const group = typeof sp.group === "string" ? sp.group : "";
  const likelyAi = sp.likelyAi === "1";

  const q = new URLSearchParams({ date });
  if (group) q.set("groupId", group);
  if (likelyAi) q.set("likelyAi", "true");
  const [list, groups] = await Promise.all([
    apiGet<GroupDevotions>(`/devotions?${q}`),
    isAdmin ? apiGet<GroupSummary[]>("/groups") : Promise.resolve([]),
  ]);
  const link = (p: Record<string, string | undefined>) => {
    const u = new URLSearchParams();
    for (const [k, v] of Object.entries({ date, group: group || undefined, likelyAi: likelyAi ? "1" : undefined, ...p }))
      if (v) u.set(k, v);
    return `/devotions/group?${u}`;
  };

  const cell = (b: DevotionBrief | null) =>
    b ? (
      <Link href={`/devotions/${b.id}`} className="inline-flex min-h-11 items-center gap-2 underline-offset-4 hover:underline">
        <AiBand band={b.band} score={b.score} status={b.aiStatus} />
        {b.reviewed && <span className="text-xs text-muted-foreground">{t("reviewedTag")}</span>}
      </Link>
    ) : (
      <span className="text-sm text-muted-foreground">{t("notYet")}</span>
    );

  return (
    <div className="grid grid-cols-1 gap-5">
      <div>
        <Link href="/devotions" className={buttonVariants({ variant: "ghost", size: "sm", className: "mb-2 w-fit" })}>
          <ChevronLeft className="size-4" aria-hidden />
          {t("title")}
        </Link>
        <h1 className="font-display text-3xl font-bold uppercase">{t("groupTitle")}</h1>
        <p className="text-muted-foreground">
          {dayLabel(new Date(`${date}T12:00:00+08:00`))}, {t("submittedOf", { submitted: list.submitted, expected: list.expected })}
        </p>
      </div>

      {/* The fairness rule (§6.3): shown wherever scores are. */}
      <p className="rounded-lg border border-border bg-muted/40 px-3 py-2 text-sm">{t("disclaimer")}</p>

      <nav aria-label={t("pickDay")} className="flex flex-wrap items-center gap-2">
        <Link
          href={link({ date: addDays(date, -1) })}
          className={buttonVariants({ variant: "outline", size: "icon" })}
          aria-label={t("prevDay")}
        >
          <ChevronLeft className="size-4" aria-hidden />
        </Link>
        <Link
          href={link({ date: today })}
          className={buttonVariants({ variant: "outline", size: "sm" })}
          aria-current={date === today ? "page" : undefined}
        >
          {t("today")}
        </Link>
        {date < today && (
          <Link
            href={link({ date: addDays(date, 1) })}
            className={buttonVariants({ variant: "outline", size: "icon" })}
            aria-label={t("nextDay")}
          >
            <ChevronRight className="size-4" aria-hidden />
          </Link>
        )}
        <Link
          href={link({ likelyAi: likelyAi ? "" : "1" })}
          className={buttonVariants({ variant: likelyAi ? "secondary" : "ghost", size: "sm" })}
        >
          {likelyAi ? t("showEveryone") : t("likelyAiFilter")}
        </Link>
      </nav>

      {isAdmin && groups.length > 0 && (
        <nav aria-label={t("pickGroup")} className="flex flex-wrap gap-2">
          <Link
            href={link({ group: "" })}
            aria-current={!group ? "page" : undefined}
            className={buttonVariants({ variant: !group ? "secondary" : "ghost", size: "sm" })}
          >
            {t("everyone")}
          </Link>
          {groups.map((g) => (
            <Link
              key={g.id}
              href={link({ group: g.id })}
              aria-current={g.id === group ? "page" : undefined}
              className={buttonVariants({ variant: g.id === group ? "secondary" : "ghost", size: "sm" })}
            >
              {g.name}
            </Link>
          ))}
        </nav>
      )}

      {list.members.length === 0 ? (
        <EmptyState
          icon={Users}
          title={likelyAi ? t("noneFlaggedTitle") : t("noMembersTitle")}
          body={likelyAi ? t("noneFlaggedBody") : t("noMembersBody")}
        />
      ) : (
        <ul className="grid divide-y divide-border rounded-[10px] border border-border bg-card">
          {list.members.map((m) => (
            <li key={m.userId} className="grid gap-1 px-4 py-3 sm:grid-cols-[1fr_12rem_12rem] sm:items-center">
              <div className="min-w-0">
                <p className="truncate font-medium">{m.displayName}</p>
                {(isAdmin || new Set(list.members.map((x) => x.groupName)).size > 1) && m.groupName && (
                  <p className="text-xs text-muted-foreground">{m.groupName}</p>
                )}
              </div>
              <div className="flex items-center gap-2">
                <span className="w-12 text-sm text-muted-foreground sm:sr-only">{t("slotShort.DAY")}</span>
                {cell(m.day)}
              </div>
              <div className="flex items-center gap-2">
                <span className="w-12 text-sm text-muted-foreground sm:sr-only">{t("slotShort.NIGHT")}</span>
                {cell(m.night)}
              </div>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
