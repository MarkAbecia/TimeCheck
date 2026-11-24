import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { MemberForm } from "@/components/members/member-form";
import { apiGet } from "@/lib/server/api";
import { requireRole } from "@/lib/session";
import type { GroupSummary } from "@/lib/types";

export const metadata: Metadata = { title: "Register a member" };

export default async function NewMemberPage({ searchParams }: PageProps<"/members/new">) {
  await requireRole("leader");
  const [t, groups, { group: wanted }] = await Promise.all([getTranslations("members"), apiGet<GroupSummary[]>("/groups"), searchParams]);
  const group = groups.find((g) => g.id === wanted) ?? (groups.length === 1 ? groups[0] : undefined);
  if (!group) notFound();
  return (
    <div className="mx-auto grid w-full max-w-2xl gap-5">
      <div>
        <h1 className="font-display text-3xl font-bold uppercase">{t("registerTitle")}</h1>
        <p className="text-muted-foreground">{t("registerLead", { group: group.name })}</p>
      </div>
      <MemberForm mode="register" groupId={group.id} />
    </div>
  );
}
