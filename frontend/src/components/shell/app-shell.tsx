import { getTranslations } from "next-intl/server";
import type { Session } from "@/lib/session";
import { cn } from "@/lib/utils";
import { SignOutButton } from "@/components/auth/sign-out-button";
import { ThemeToggle } from "@/components/theme-toggle";
import { BottomTabs, SideNav } from "./nav-links";

/** The gold foil band: role, plus the group for leaders and members. */
export async function RoleBand({ session, className }: { session: Session; className?: string }) {
  const t = await getTranslations("roles");
  const label = [t(session.role), session.groupName].filter(Boolean).join(" · ");
  return (
    <span
      className={cn(
        "inline-block rounded-[2px] bg-shell-accent px-2 py-px font-display text-xs font-bold tracking-[0.12em] text-accent-foreground uppercase",
        className,
      )}
    >
      {label}
    </span>
  );
}

function Wordmark({ churchName, poweredBy, compact }: { churchName: string; poweredBy: string; compact?: boolean }) {
  return (
    <div className="min-w-0">
      {/* Logo slot: churches.logo arrives with church settings; the name carries the brand until then. */}
      <p className={cn("truncate font-display font-bold tracking-[0.02em] uppercase", compact ? "text-xl" : "text-[1.375rem] leading-tight")}>
        {churchName}
      </p>
      <p className="text-[0.6875rem] text-shell-muted">{poweredBy}</p>
    </div>
  );
}

function Avatar({ initials }: { initials: string }) {
  return (
    <span aria-hidden className="grid size-9 shrink-0 place-items-center rounded-full bg-sidebar-accent text-sm font-semibold">
      {initials}
    </span>
  );
}

function monogram(name: string) {
  return name.split(/\s+/).map((w) => w[0]).join("").slice(0, 2);
}

export function greetingKey(hour: number) {
  return hour < 12 ? "morning" : hour < 18 ? "afternoon" : "evening";
}

export async function AppShell({
  session,
  hour,
  unread = 0,
  children,
}: {
  session: Session;
  hour: number;
  unread?: number;
  children: React.ReactNode;
}) {
  const t = await getTranslations();
  const greeting = t(`greeting.${greetingKey(hour)}`, { name: session.firstName });
  const poweredBy = t("brand.poweredBy");

  return (
    <div className="min-h-dvh">
      <a
        href="#main"
        className="sr-only focus:not-sr-only focus:fixed focus:top-3 focus:left-3 focus:z-50 focus:rounded-md focus:bg-card focus:px-4 focus:py-2"
      >
        {t("nav.skip")}
      </a>

      {/* Tablet rail (md) widening to the full sidebar (lg). */}
      <aside className="shell fixed inset-y-0 left-0 z-30 hidden w-24 flex-col bg-shell text-shell-foreground md:flex lg:w-64">
        <div className="flex h-16 items-center justify-center border-b border-shell-border px-3 lg:h-auto lg:justify-start lg:px-5 lg:py-5">
          <span aria-hidden className="font-display text-2xl font-bold uppercase lg:hidden">
            {monogram(session.churchName)}
          </span>
          <div className="hidden min-w-0 flex-1 lg:block">
            <Wordmark churchName={session.churchName} poweredBy={poweredBy} />
          </div>
          <ThemeToggle className="-mr-2 hidden lg:inline-flex" />
        </div>
        <SideNav role={session.role} unread={unread} />
        <div className="flex flex-col items-center gap-2 border-t border-shell-border p-3 lg:flex-row lg:items-center lg:gap-3 lg:p-4">
          <Avatar initials={session.initials} />
          <div className="hidden min-w-0 flex-1 lg:block">
            <p className="truncate text-sm font-semibold">{greeting}</p>
            <RoleBand session={session} className="mt-1 max-w-full truncate" />
          </div>
          <ThemeToggle className="lg:hidden" />
          <SignOutButton label={t("auth.signOut")} variant="shell" />
        </div>
      </aside>

      {/* Phone app bar. */}
      <header className="shell border-b border-shell-border bg-shell px-5 pt-[calc(env(safe-area-inset-top)+1rem)] pb-4 text-shell-foreground md:hidden">
        <div className="flex items-start justify-between gap-3">
          <Wordmark churchName={session.churchName} poweredBy={poweredBy} />
          <div className="-mt-1.5 -mr-2 flex">
            <ThemeToggle />
            <SignOutButton label={t("auth.signOut")} variant="shell" />
          </div>
        </div>
        <div className="mt-3.5 flex items-center gap-2.5">
          <Avatar initials={session.initials} />
          <div className="min-w-0">
            <p className="truncate text-[0.9375rem] font-semibold">{greeting}</p>
            <RoleBand session={session} className="mt-0.5" />
          </div>
        </div>
      </header>

      <div className="md:pl-24 lg:pl-64">
        <main
          id="main"
          className="mx-auto w-full max-w-[1280px] px-4 pt-5 pb-[calc(env(safe-area-inset-bottom)+6rem)] md:px-6 md:pt-8 md:pb-10 lg:px-8"
        >
          {children}
        </main>
      </div>

      <BottomTabs role={session.role} unread={unread} />
    </div>
  );
}
