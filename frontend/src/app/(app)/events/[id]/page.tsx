import type { Metadata } from "next";
import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { ChevronLeft, ExternalLink, MapPin, Pencil } from "lucide-react";
import { AttendanceSheet } from "@/components/events/attendance-sheet";
import { CancelEvent } from "@/components/events/cancel-event";
import { buttonVariants } from "@/components/ui/button";
import { apiGet } from "@/lib/server/api";
import { atLeast, requireSession } from "@/lib/session";
import { dayLabel, timeRange } from "@/lib/time";
import type { EventDetail, GroupSummary } from "@/lib/types";

export const metadata: Metadata = { title: "Event" };

export default async function EventPage({ params, searchParams }: PageProps<"/events/[id]">) {
  const session = await requireSession();
  const [{ id }, sp, t] = await Promise.all([params, searchParams, getTranslations("events")]);
  const groupId = typeof sp.groupId === "string" ? sp.groupId : undefined;
  const event = await apiGet<EventDetail>(`/events/${id}${groupId ? `?groupId=${groupId}` : ""}`);
  // A church-wide roster is long; admins narrow it one cell group at a time.
  const groups = event.isChurchWide && atLeast(session.role, "admin") && event.canMark ? await apiGet<GroupSummary[]>("/groups") : [];
  const started = new Date(event.startsAt) <= new Date();

  return (
    <div className="mx-auto grid w-full max-w-3xl grid-cols-1 gap-5">
      <Link href="/events" className={buttonVariants({ variant: "ghost", size: "sm", className: "w-fit" })}>
        <ChevronLeft className="size-4" aria-hidden />
        {t("back")}
      </Link>

      <div className="grid gap-2">
        <p className="text-sm text-muted-foreground">
          {event.typeName}, {event.groupName ?? t("wholeChurch")}
        </p>
        <h1 className="font-display text-3xl font-bold uppercase text-balance">{event.title}</h1>
        <p className="tabular text-lg">
          {dayLabel(new Date(event.startsAt))}, {timeRange(event.startsAt, event.endsAt)}
        </p>
        {event.isCancelled && (
          <p className="w-fit rounded-md bg-destructive/10 px-2 py-1 text-sm font-medium text-destructive">{t("cancelledBanner")}</p>
        )}
        {event.location && (
          <p className="flex items-center gap-2">
            <MapPin className="size-4 text-muted-foreground" aria-hidden />
            {event.location}
          </p>
        )}
        {event.onlineLink && (
          <a
            href={event.onlineLink}
            target="_blank"
            rel="noopener noreferrer"
            className="flex w-fit items-center gap-2 text-primary underline underline-offset-4"
          >
            <ExternalLink className="size-4" aria-hidden />
            {t("joinOnline")}
          </a>
        )}
        {event.description && <p className="max-w-prose whitespace-pre-line text-muted-foreground">{event.description}</p>}
      </div>

      {event.canManage && !event.isCancelled && (
        <div className="flex flex-wrap items-center gap-2">
          <Link href={`/events/${event.id}/edit`} className={buttonVariants({ variant: "outline", size: "sm" })}>
            <Pencil className="size-4" aria-hidden />
            {t("edit")}
          </Link>
          <CancelEvent id={event.id} title={event.title} />
        </div>
      )}

      {event.myStatus && (
        <p className="rounded-[10px] border border-border bg-card px-4 py-3">
          {t("yourStatus")} <strong>{t(`status.${event.myStatus}`)}</strong>
        </p>
      )}

      {event.canMark && !event.isCancelled && (
        <section aria-labelledby="attendance" className="grid gap-3">
          <h2 id="attendance" className="font-display text-2xl font-bold uppercase">
            {t("attendance")}
          </h2>
          {groups.length > 0 && (
            <nav aria-label={t("pickGroup")} className="flex flex-wrap gap-2">
              <Link
                href={`/events/${event.id}`}
                aria-current={!groupId ? "page" : undefined}
                className={buttonVariants({ variant: !groupId ? "secondary" : "ghost", size: "sm" })}
              >
                {t("everyone")}
              </Link>
              {groups.map((g) => (
                <Link
                  key={g.id}
                  href={`/events/${event.id}?groupId=${g.id}`}
                  aria-current={g.id === groupId ? "page" : undefined}
                  className={buttonVariants({ variant: g.id === groupId ? "secondary" : "ghost", size: "sm" })}
                >
                  {g.name}
                </Link>
              ))}
            </nav>
          )}
          {started ? (
            <AttendanceSheet key={groupId ?? "all"} detail={event} groupId={groupId} />
          ) : (
            <p className="text-muted-foreground">{t("notStarted")}</p>
          )}
        </section>
      )}
    </div>
  );
}
