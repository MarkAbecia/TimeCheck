import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { EventForm } from "@/components/events/event-form";
import { apiGet } from "@/lib/server/api";
import { atLeast, requireRole } from "@/lib/session";
import type { EventType, GroupSummary } from "@/lib/types";

export const metadata: Metadata = { title: "Schedule an event" };

export default async function NewEventPage({ searchParams }: PageProps<"/events/new">) {
  const session = await requireRole("leader");
  const [t, types, groups, { group }] = await Promise.all([
    getTranslations("events"),
    apiGet<EventType[]>("/event-types"),
    apiGet<GroupSummary[]>("/groups"),
    searchParams,
  ]);
  return (
    <div className="mx-auto grid w-full max-w-2xl gap-5">
      <div>
        <h1 className="font-display text-3xl font-bold uppercase">{t("newTitle")}</h1>
        <p className="text-muted-foreground">{t("newLead")}</p>
      </div>
      <EventForm
        types={types}
        groups={groups}
        isAdmin={atLeast(session.role, "admin")}
        defaultGroupId={typeof group === "string" ? group : undefined}
      />
    </div>
  );
}
