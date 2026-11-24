import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { EventForm } from "@/components/events/event-form";
import { apiGet } from "@/lib/server/api";
import { atLeast, requireRole } from "@/lib/session";
import type { EventDetail, EventType, GroupSummary } from "@/lib/types";

export const metadata: Metadata = { title: "Edit event" };

export default async function EditEventPage({ params }: PageProps<"/events/[id]/edit">) {
  const session = await requireRole("leader");
  const { id } = await params;
  const [t, event, types, groups] = await Promise.all([
    getTranslations("events"),
    apiGet<EventDetail>(`/events/${id}`),
    apiGet<EventType[]>("/event-types"),
    apiGet<GroupSummary[]>("/groups"),
  ]);
  if (!event.canManage || event.isCancelled) notFound();
  return (
    <div className="mx-auto grid w-full max-w-2xl gap-5">
      <div>
        <h1 className="font-display text-3xl font-bold uppercase">{t("editTitle")}</h1>
        <p className="text-muted-foreground">{t("editLead")}</p>
      </div>
      <EventForm types={types} groups={groups} isAdmin={atLeast(session.role, "admin")} event={event} />
    </div>
  );
}
