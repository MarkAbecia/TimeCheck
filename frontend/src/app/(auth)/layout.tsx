import { getTranslations } from "next-intl/server";
import { ThemeToggle } from "@/components/theme-toggle";
import { getChurchBranding } from "@/lib/session";

// The sign-in pass hangs off the black lanyard: church name first, "powered by TimeCheck" under it.
export default async function AuthLayout({ children }: LayoutProps<"/">) {
  const [t, church] = await Promise.all([getTranslations("brand"), getChurchBranding()]);
  return (
    <div className="flex min-h-dvh flex-col">
      <header className="shell bg-shell px-5 pt-[calc(env(safe-area-inset-top)+1.25rem)] pb-16 text-shell-foreground md:pb-24">
        <div className="mx-auto flex max-w-[400px] items-start justify-between gap-3">
          <div className="min-w-0">
            <p className="truncate font-display text-[1.625rem] leading-tight font-bold tracking-[0.02em] uppercase">{church.name}</p>
            <p className="text-xs text-shell-muted">{t("poweredBy")}</p>
          </div>
          <ThemeToggle className="-mt-1 -mr-2" />
        </div>
      </header>
      <main className="-mt-10 flex-1 px-4 pb-[calc(env(safe-area-inset-bottom)+2rem)] md:-mt-16">{children}</main>
    </div>
  );
}
