"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { useState } from "react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { send } from "@/lib/api-client";
import { dayLabel, shortTime } from "@/lib/time";
import type { AppNotification } from "@/lib/types";
import { cn } from "@/lib/utils";

/** The in-app list (§6.7). Opening one marks it read; unread ones say so in words, not only in weight. */
export function NotificationList({ items, unread }: { items: AppNotification[]; unread: number }) {
  const t = useTranslations("notifications");
  const router = useRouter();
  const [pending, setPending] = useState(false);

  async function markAll() {
    setPending(true);
    const r = await send("POST", "/me/notifications/read", { ids: null });
    setPending(false);
    if (r.ok) router.refresh();
    else toast.error(r.problem.status === 0 ? t("offline") : t("unexpected"));
  }

  return (
    <div className="grid gap-3">
      {unread > 0 && (
        <Button type="button" variant="outline" size="sm" className="w-fit" disabled={pending} onClick={markAll}>
          {t("markAll")}
        </Button>
      )}
      <ul className="grid divide-y divide-border rounded-[10px] border border-border bg-card">
        {items.map((n) => (
          <li key={n.id}>
            <Link
              href={n.url}
              // Saves "read" before leaving, so a quick tap away can't cancel it. Modified clicks (new tab) open as usual.
              onClick={async (e) => {
                if (n.read || e.metaKey || e.ctrlKey || e.shiftKey || e.button !== 0) return;
                e.preventDefault();
                await send("POST", "/me/notifications/read", { ids: [n.id] });
                router.push(n.url);
              }}
              className="grid gap-0.5 px-4 py-3 hover:bg-muted/50 focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none"
            >
              <span className="flex items-baseline justify-between gap-3">
                <span className={cn(!n.read && "font-semibold")}>
                  {!n.read && (
                    <span className="mr-2 rounded-sm bg-primary px-1.5 py-0.5 text-xs font-semibold text-primary-foreground">
                      {t("new")}
                    </span>
                  )}
                  {!n.read && " "}
                  {n.title}
                </span>
                <span className="shrink-0 text-xs text-muted-foreground">
                  {dayLabel(new Date(n.createdAt))} {shortTime(new Date(n.createdAt))}
                </span>
              </span>
              <span className="text-sm text-muted-foreground">{n.body}</span>
            </Link>
          </li>
        ))}
      </ul>
    </div>
  );
}
