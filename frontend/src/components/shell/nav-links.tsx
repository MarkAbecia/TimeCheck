"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useTranslations } from "next-intl";
import { useState } from "react";
import { Ellipsis } from "lucide-react";
import { cn } from "@/lib/utils";
import { isActive, navFor, type Role } from "@/lib/nav";

/** Unread notifications, in words for screen readers. Shown on the Notifications item (and More, when it's in there). */
export function Unread({ count, className }: { count: number; className?: string }) {
  const t = useTranslations("nav");
  if (count <= 0) return null;
  return (
    <span
      className={cn(
        "tabular rounded-full bg-primary px-1.5 text-[0.6875rem] leading-[1.125rem] font-semibold text-primary-foreground",
        className,
      )}
    >
      {count > 99 ? "99+" : count}
      <span className="sr-only"> {t("unread", { count })}</span>
    </span>
  );
}

/** The More tab's face, shared by the placeholder button and the loaded sheet's trigger. */
export function MoreLabel({ moreActive, unread }: { moreActive: boolean; unread: number }) {
  const t = useTranslations("nav");
  return (
    <>
      <Ellipsis className={cn("size-[1.375rem]", moreActive && "text-shell-active")} aria-hidden />
      {t("more")}
      <Unread count={unread} className="absolute top-1 left-1/2 ml-2" />
    </>
  );
}

/** Side rail (md) and full sidebar (lg): every item for the role, no "More". */
export function SideNav({ role, unread = 0 }: { role: Role; unread?: number }) {
  const t = useTranslations("nav");
  const pathname = usePathname();
  return (
    <nav aria-label={t("label")} className="flex-1 overflow-y-auto px-2 py-3 lg:px-3">
      <ul className="flex flex-col gap-1">
        {navFor(role).all.map(({ key, href, icon: Icon }) => {
          const active = isActive(href, pathname);
          return (
            <li key={key}>
              <Link
                href={href}
                aria-current={active ? "page" : undefined}
                className={cn(
                  "group flex min-h-11 flex-col items-center justify-center gap-1 rounded-lg px-1 py-2 text-[0.6875rem] font-medium text-shell-muted transition-colors hover:bg-white/5 hover:text-shell-foreground",
                  "lg:flex-row lg:justify-start lg:gap-3 lg:px-3 lg:text-sm",
                  active && "bg-white/[0.07] text-shell-foreground",
                )}
              >
                <Icon className={cn("size-5 shrink-0", active && "text-shell-active")} aria-hidden />
                <span className="text-center leading-tight lg:text-left">{t(key)}</span>
                {key === "notifications" && <Unread count={unread} className="lg:ml-auto" />}
              </Link>
            </li>
          );
        })}
      </ul>
    </nav>
  );
}

/** Phone bottom tab bar: up to four tabs plus More (§11.3). */
export function BottomTabs({ role, unread = 0 }: { role: Role; unread?: number }) {
  const t = useTranslations("nav");
  const pathname = usePathname();
  // The More sheet and its dialog library load on the first tap, then replace the button, opened (§9 JS budget).
  const [Sheet, setSheet] = useState<typeof import("./more-sheet").MoreSheet | null>(null);
  const { tabs, more } = navFor(role);
  const moreActive = more.some((i) => isActive(i.href, pathname));

  const tab = "relative flex min-h-14 flex-col items-center justify-center gap-1 text-[0.6875rem] font-medium transition-colors";
  const triggerClass = cn(tab, "w-full", moreActive ? "text-shell-foreground" : "text-shell-muted");

  return (
    <nav
      aria-label={t("label")}
      className="shell fixed inset-x-0 bottom-0 z-40 border-t border-shell-border bg-shell pb-[env(safe-area-inset-bottom)] md:hidden"
    >
      <ul className="grid auto-cols-fr grid-flow-col">
        {tabs.map(({ key, href, icon: Icon }) => {
          const active = isActive(href, pathname);
          return (
            <li key={key}>
              <Link
                href={href}
                aria-current={active ? "page" : undefined}
                className={cn(tab, active ? "text-shell-foreground" : "text-shell-muted")}
              >
                <Icon className={cn("size-[1.375rem]", active && "text-shell-active")} aria-hidden />
                {t(key)}
                {key === "notifications" && <Unread count={unread} className="absolute top-1 left-1/2 ml-2" />}
              </Link>
            </li>
          );
        })}
        {more.length > 0 && (
          <li>
            {Sheet ? (
              <Sheet items={more} unread={unread} triggerClass={triggerClass} moreActive={moreActive} />
            ) : (
              <button
                type="button"
                aria-haspopup="dialog"
                aria-expanded={false}
                className={triggerClass}
                onClick={() => void import("./more-sheet").then((m) => setSheet(() => m.MoreSheet))}
              >
                <MoreLabel moreActive={moreActive} unread={more.some((i) => i.key === "notifications") ? unread : 0} />
              </button>
            )}
          </li>
        )}
      </ul>
    </nav>
  );
}
