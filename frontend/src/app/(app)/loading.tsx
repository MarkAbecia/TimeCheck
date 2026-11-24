import { getTranslations } from "next-intl/server";
import { EventPassSkeleton } from "@/components/event-pass";

export default async function Loading() {
  const t = await getTranslations("today");
  return (
    <div role="status" aria-live="polite">
      <span className="sr-only">{t("loading")}</span>
      <div aria-hidden className="mx-1 mb-2.5 h-4 w-36 animate-pulse rounded bg-muted" />
      <div className="grid gap-3 lg:grid-cols-2 lg:gap-4">
        <EventPassSkeleton />
        <EventPassSkeleton />
      </div>
    </div>
  );
}
