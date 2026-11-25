"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useTranslations } from "next-intl";
import { useState } from "react";
import { cn } from "@/lib/utils";
import { isActive, type NavItem } from "@/lib/nav";
import { Sheet, SheetContent, SheetHeader, SheetTitle, SheetTrigger } from "@/components/ui/sheet";
import { MoreLabel, Unread } from "./nav-links";

/** The phone More sheet. Loaded on the first tap by BottomTabs, so it mounts already open. */
export function MoreSheet({
  items,
  unread,
  triggerClass,
  moreActive,
}: {
  items: NavItem[];
  unread: number;
  triggerClass: string;
  moreActive: boolean;
}) {
  const t = useTranslations("nav");
  const pathname = usePathname();
  const [open, setOpen] = useState(true);

  return (
    <Sheet open={open} onOpenChange={setOpen}>
      <SheetTrigger className={triggerClass}>
        <MoreLabel moreActive={moreActive} unread={items.some((i) => i.key === "notifications") ? unread : 0} />
      </SheetTrigger>
      <SheetContent side="bottom" className="rounded-t-2xl pb-[calc(env(safe-area-inset-bottom)+1rem)]">
        <SheetHeader>
          <SheetTitle className="font-display text-xl font-bold tracking-wide uppercase">{t("moreTitle")}</SheetTitle>
        </SheetHeader>
        <ul className="grid gap-1 px-2">
          {items.map(({ key, href, icon: Icon }) => {
            const active = isActive(href, pathname);
            return (
              <li key={key}>
                <Link
                  href={href}
                  onClick={() => setOpen(false)}
                  aria-current={active ? "page" : undefined}
                  className={cn(
                    "flex min-h-12 items-center gap-3 rounded-lg px-3 text-base font-medium hover:bg-muted",
                    active && "bg-muted text-primary-text",
                  )}
                >
                  <Icon className="size-5 text-muted-foreground" aria-hidden />
                  {t(key)}
                  {key === "notifications" && <Unread count={unread} className="ml-auto" />}
                </Link>
              </li>
            );
          })}
        </ul>
      </SheetContent>
    </Sheet>
  );
}
