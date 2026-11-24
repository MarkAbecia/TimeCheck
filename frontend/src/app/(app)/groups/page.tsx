import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { GroupsManager } from "@/components/groups/groups-manager";
import { apiGet } from "@/lib/server/api";
import { requireRole } from "@/lib/session";
import type { GroupSummary, LeaderOption } from "@/lib/types";

export const metadata: Metadata = { title: "Groups" };

export default async function GroupsPage() {
  await requireRole("admin");
  const [t, groups, leaders] = await Promise.all([
    getTranslations("groups"),
    apiGet<GroupSummary[]>("/groups"),
    apiGet<LeaderOption[]>("/groups/leader-options"),
  ]);
  return (
    <div className="mx-auto grid w-full max-w-3xl gap-5">
      <div>
        <h1 className="font-display text-3xl font-bold uppercase">{t("title")}</h1>
        <p className="text-muted-foreground">{t("lead")}</p>
      </div>
      <GroupsManager groups={groups} leaders={leaders} />
    </div>
  );
}
