import { AppShell } from "@/components/shell/app-shell";
import { getSession } from "@/lib/session";

export default async function AppLayout({ children }: LayoutProps<"/">) {
  const session = await getSession();
  const hour = Number(
    new Intl.DateTimeFormat("en-US", { hour: "numeric", hourCycle: "h23", timeZone: "Asia/Manila" }).format(new Date()),
  );
  return (
    <AppShell session={session} hour={hour}>
      {children}
    </AppShell>
  );
}
