import { OfflineSync } from "@/components/events/offline-sync";
import { AppShell } from "@/components/shell/app-shell";
import { requireSession } from "@/lib/session";

export default async function AppLayout({ children }: LayoutProps<"/">) {
  const session = await requireSession();
  const hour = Number(new Intl.DateTimeFormat("en-US", { hour: "numeric", hourCycle: "h23", timeZone: "Asia/Manila" }).format(new Date()));
  return (
    <AppShell session={session} hour={hour}>
      <OfflineSync />
      {children}
    </AppShell>
  );
}
