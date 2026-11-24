import { OfflineSync } from "@/components/events/offline-sync";
import { AppShell } from "@/components/shell/app-shell";
import { apiGet } from "@/lib/server/api";
import { requireSession } from "@/lib/session";

export default async function AppLayout({ children }: LayoutProps<"/">) {
  const session = await requireSession();
  // The shell's unread badge. A failure here must never take the whole app down; the page itself still loads.
  const unread = await apiGet<{ unread: number }>("/me/notifications?limit=1").then(
    (r) => r.unread,
    () => 0,
  );
  const hour = Number(new Intl.DateTimeFormat("en-US", { hour: "numeric", hourCycle: "h23", timeZone: "Asia/Manila" }).format(new Date()));
  return (
    <AppShell session={session} hour={hour} unread={unread}>
      <OfflineSync />
      {children}
    </AppShell>
  );
}
