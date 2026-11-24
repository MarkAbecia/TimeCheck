import type { Metadata } from "next";
import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { ChevronRight, UserPlus, Users } from "lucide-react";
import { EmptyState } from "@/components/empty-state";
import { buttonVariants } from "@/components/ui/button";
import { apiGet } from "@/lib/server/api";
import { requireRole } from "@/lib/session";
import type { Group, GroupSummary } from "@/lib/types";

export const metadata: Metadata = { title: "Members" };

export default async function MembersPage({ searchParams }: PageProps<"/members">) {
  await requireRole("leader");
  const [t, groups, { group: wanted }] = await Promise.all([getTranslations("members"), apiGet<GroupSummary[]>("/groups"), searchParams]);
  if (groups.length === 0) return <EmptyState icon={Users} title={t("noGroupsTitle")} body={t("noGroupsBody")} />;

  const current = groups.find((g) => g.id === wanted) ?? groups[0];
  const group = await apiGet<Group>(`/groups/${current.id}`);

  return (
    <div className="grid gap-5">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="font-display text-3xl font-bold uppercase">{t("title")}</h1>
          <p className="text-muted-foreground">{t("count", { count: group.members.length, group: group.name })}</p>
        </div>
        <Link href={`/members/new?group=${group.id}`} className={buttonVariants()}>
          <UserPlus className="size-4" aria-hidden />
          {t("registerCta")}
        </Link>
      </div>

      {groups.length > 1 && (
        <nav aria-label={t("pickGroup")} className="flex flex-wrap gap-2">
          {groups.map((g) => (
            <Link
              key={g.id}
              href={`/members?group=${g.id}`}
              aria-current={g.id === group.id ? "page" : undefined}
              className={buttonVariants({ variant: g.id === group.id ? "secondary" : "ghost", size: "sm" })}
            >
              {g.name}
            </Link>
          ))}
        </nav>
      )}

      {group.members.length === 0 ? (
        <EmptyState icon={Users} title={t("emptyTitle")} body={t("emptyBody")} />
      ) : (
        <ul className="grid divide-y divide-border rounded-[10px] border border-border bg-card">
          {group.members.map((m) => (
            <li key={m.id}>
              {m.isLeader ? (
                <div className="flex min-h-14 items-center gap-3 px-4 py-2">
                  <span className="flex-1 font-medium">{m.displayName}</span>
                  <span className="text-sm text-muted-foreground">{t("leader")}</span>
                </div>
              ) : (
                <Link
                  href={`/members/${m.id}`}
                  className="flex min-h-14 items-center gap-3 px-4 py-2 hover:bg-muted/60 focus-visible:bg-muted/60"
                >
                  <span className="flex-1 font-medium">{m.displayName}</span>
                  <span className="text-sm text-muted-foreground">{t(`status.${m.status}`)}</span>
                  <ChevronRight className="size-4 text-muted-foreground" aria-hidden />
                </Link>
              )}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
