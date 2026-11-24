import type { Metadata } from "next";
import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { ChevronLeft } from "lucide-react";
import { MemberControls } from "@/components/members/member-controls";
import { MemberForm } from "@/components/members/member-form";
import { apiGet } from "@/lib/server/api";
import { atLeast, requireRole } from "@/lib/session";
import type { GroupSummary, Member } from "@/lib/types";

export const metadata: Metadata = { title: "Member" };

export default async function MemberPage({ params }: PageProps<"/members/[id]">) {
  const session = await requireRole("leader");
  const { id } = await params;
  const isAdmin = atLeast(session.role, "admin");
  const [t, member, groups] = await Promise.all([
    getTranslations("members"),
    apiGet<Member>(`/members/${encodeURIComponent(id)}`),
    isAdmin ? apiGet<GroupSummary[]>("/groups") : Promise.resolve([]),
  ]);

  return (
    <div className="mx-auto grid w-full max-w-2xl gap-6">
      <Link
        href={member.groupId ? `/members?group=${member.groupId}` : "/members"}
        className="inline-flex min-h-11 items-center gap-1 self-start text-sm font-medium text-primary-text"
      >
        <ChevronLeft className="size-4" aria-hidden />
        {member.groupName ?? t("title")}
      </Link>
      <div>
        <h1 className="font-display text-3xl font-bold uppercase">{member.displayName}</h1>
        <p className="text-muted-foreground">{member.email}</p>
      </div>
      <section aria-labelledby="manage" className="grid gap-3">
        <h2 id="manage" className="font-semibold">
          {t("manage")}
        </h2>
        <MemberControls member={member} isAdmin={isAdmin} isSuper={session.role === "super_admin"} groups={groups} />
      </section>
      <section aria-labelledby="details" className="grid gap-3">
        <h2 id="details" className="font-semibold">
          {t("details")}
        </h2>
        {/* Re-mount after a save so the form shows what the API stored. */}
        <MemberForm key={JSON.stringify(member)} mode="edit" member={member} />
      </section>
    </div>
  );
}
