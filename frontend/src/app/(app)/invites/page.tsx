import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { UserPlus } from "lucide-react";
import { EmptyState } from "@/components/empty-state";
import { InviteForm } from "@/components/invites/invite-form";
import { InviteItem } from "@/components/invites/invite-item";
import { apiGet } from "@/lib/server/api";
import { atLeast, requireSession } from "@/lib/session";
import type { Invite, MyInvites } from "@/lib/types";

export const metadata: Metadata = { title: "Invites" };

export default async function InvitesPage({ searchParams }: PageProps<"/invites">) {
  const session = await requireSession();
  const leads = atLeast(session.role, "leader");
  const [t, sp, mine, group] = await Promise.all([
    getTranslations("invites"),
    searchParams,
    apiGet<MyInvites>("/me/invites"),
    leads ? apiGet<Invite[]>("/invites") : Promise.resolve([] as Invite[]),
  ]);
  const now = new Date();
  const others = group.filter((i) => !i.mine);
  const item = (i: Invite, showInviter: boolean) => (
    <InviteItem key={i.id} invite={i} started={new Date(i.eventStartsAt) <= now} showInviter={showInviter} />
  );

  return (
    <div className="mx-auto grid w-full max-w-2xl grid-cols-1 gap-6">
      <div>
        <h1 className="font-display text-3xl font-bold uppercase">{t("title")}</h1>
        <p className="text-muted-foreground">{t("lead")}</p>
      </div>

      <section aria-labelledby="log" className="grid gap-3 rounded-[10px] border border-border bg-card p-4">
        <h2 id="log" className="font-semibold">
          {t("logTitle")}
        </h2>
        {mine.events.length > 0 ? (
          <InviteForm events={mine.events} selected={typeof sp.event === "string" ? sp.event : undefined} />
        ) : (
          <p className="text-sm text-muted-foreground">{t("noEvents")}</p>
        )}
      </section>

      <section aria-labelledby="mine" className="grid gap-2">
        <h2 id="mine" className="font-semibold">
          {t("mineTitle")}
        </h2>
        {mine.invites.length === 0 ? (
          <EmptyState icon={UserPlus} title={t("emptyTitle")} body={t("emptyBody")} />
        ) : (
          <ul className="grid divide-y divide-border rounded-[10px] border border-border bg-card">
            {mine.invites.map((i) => item(i, false))}
          </ul>
        )}
      </section>

      {leads && (
        <section aria-labelledby="group" className="grid gap-2">
          <h2 id="group" className="font-semibold">
            {t("groupTitle")}
          </h2>
          {others.length === 0 ? (
            <p className="text-sm text-muted-foreground">{t("groupEmpty")}</p>
          ) : (
            <ul className="grid divide-y divide-border rounded-[10px] border border-border bg-card">{others.map((i) => item(i, true))}</ul>
          )}
        </section>
      )}
    </div>
  );
}
