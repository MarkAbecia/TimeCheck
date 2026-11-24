import type { Metadata } from "next";
import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { Bell } from "lucide-react";
import { EmptyState } from "@/components/empty-state";
import { NotificationList } from "@/components/notifications/notification-list";
import { buttonVariants } from "@/components/ui/button";
import { apiGet } from "@/lib/server/api";
import { requireSession } from "@/lib/session";
import type { NotificationList as List } from "@/lib/types";

export const metadata: Metadata = { title: "Notifications" };

export default async function NotificationsPage({ searchParams }: PageProps<"/notifications">) {
  await requireSession();
  const [t, sp] = await Promise.all([getTranslations("notifications"), searchParams]);
  const cursor = typeof sp.cursor === "string" ? sp.cursor : "";
  const list = await apiGet<List>(`/me/notifications?limit=30${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ""}`);

  return (
    <div className="mx-auto grid w-full max-w-2xl grid-cols-1 gap-5">
      <div>
        <h1 className="font-display text-3xl font-bold uppercase">{t("title")}</h1>
        <p className="text-muted-foreground">
          {t.rich("lead", {
            link: (chunks) => (
              <Link href="/profile#notifications" className="underline underline-offset-4">
                {chunks}
              </Link>
            ),
          })}
        </p>
      </div>
      {list.items.length === 0 ? (
        <EmptyState icon={Bell} title={t("emptyTitle")} body={t("emptyBody")} />
      ) : (
        <NotificationList items={list.items} unread={list.unread} />
      )}
      <nav aria-label={t("pages")} className="flex gap-2">
        {cursor && (
          <Link href="/notifications" className={buttonVariants({ variant: "ghost", size: "sm" })}>
            {t("newest")}
          </Link>
        )}
        {list.nextCursor && (
          <Link
            href={`/notifications?cursor=${encodeURIComponent(list.nextCursor)}`}
            className={buttonVariants({ variant: "outline", size: "sm" })}
          >
            {t("older")}
          </Link>
        )}
      </nav>
    </div>
  );
}
