import type { Metadata } from "next";
import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { ChevronRight, Search, UserPlus, Users } from "lucide-react";
import { EmptyState } from "@/components/empty-state";
import { buttonVariants } from "@/components/ui/button";
import { apiGet } from "@/lib/server/api";
import { atLeast, requireRole } from "@/lib/session";
import type { Group, GroupSummary, MemberHit } from "@/lib/types";

export const metadata: Metadata = { title: "Members" };

export default async function MembersPage({ searchParams }: PageProps<"/members">) {
  const session = await requireRole("leader");
  const isAdmin = atLeast(session.role, "admin");
  const [t, groups, params] = await Promise.all([getTranslations("members"), apiGet<GroupSummary[]>("/groups"), searchParams]);
  const wanted = typeof params.group === "string" ? params.group : undefined;
  const q = isAdmin && typeof params.q === "string" ? params.q.trim() : "";

  // Admins search everyone in the church (§6.10); the results replace the group list.
  if (q) {
    const hits = q.length >= 2 ? await apiGet<MemberHit[]>(`/members?q=${encodeURIComponent(q)}`) : null;
    return (
      <div className="grid gap-5">
        <h1 className="font-display text-3xl font-bold uppercase">{t("title")}</h1>
        <SearchForm q={q} label={t("search.label")} placeholder={t("search.placeholder")} submit={t("search.submit")} />
        {hits === null ? (
          <p className="text-muted-foreground">{t("search.tooShort")}</p>
        ) : (
          <>
            <p className="text-muted-foreground" role="status">
              {t("search.count", { count: hits.length, q })}
            </p>
            {hits.length > 0 && (
              <ul className="grid divide-y divide-border rounded-[10px] border border-border bg-card">
                {hits.map((h) => (
                  <li key={h.id}>
                    <Link
                      href={`/members/${h.id}`}
                      className="flex min-h-14 items-center gap-3 px-4 py-2 hover:bg-muted/60 focus-visible:bg-muted/60"
                    >
                      <span className="grid flex-1">
                        <span className="font-medium">{h.displayName}</span>
                        <span className="text-sm text-muted-foreground">{h.groupName ?? t("search.noGroup")}</span>
                      </span>
                      <span className="text-sm text-muted-foreground">
                        {h.isActive ? t(`status.${h.status}`) : t("search.deactivated")}
                      </span>
                      <ChevronRight className="size-4 text-muted-foreground" aria-hidden />
                    </Link>
                  </li>
                ))}
              </ul>
            )}
          </>
        )}
        <Link href="/members" className={buttonVariants({ variant: "ghost", className: "w-fit" })}>
          {t("search.back")}
        </Link>
      </div>
    );
  }

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

      {isAdmin && <SearchForm q="" label={t("search.label")} placeholder={t("search.placeholder")} submit={t("search.submit")} />}

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

/** A plain GET form, so search adds no client JavaScript to the page. */
function SearchForm({ q, label, placeholder, submit }: { q: string; label: string; placeholder: string; submit: string }) {
  return (
    <form role="search" action="/members" className="flex max-w-xl gap-2">
      <label htmlFor="member-search" className="sr-only">
        {label}
      </label>
      {/* Plain elements styled like ui/input and ui/button: those are Base UI client components, and this page has little JS to spare. */}
      <input
        id="member-search"
        name="q"
        type="search"
        defaultValue={q}
        placeholder={placeholder}
        minLength={2}
        maxLength={100}
        required
        className="h-11 w-full min-w-0 rounded-lg border border-input bg-card px-3 py-2 text-base transition-colors outline-none placeholder:text-muted-foreground focus-visible:border-ring dark:bg-input/30"
      />
      <button type="submit" className={buttonVariants({ variant: "outline" })}>
        <Search className="size-4" aria-hidden />
        {submit}
      </button>
    </form>
  );
}
