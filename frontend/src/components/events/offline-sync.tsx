"use client";

import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { useEffect } from "react";
import { toast } from "sonner";
import { flushQueue, pendingCount } from "@/lib/offline-queue";

/** Sends attendance saved while offline, on load and whenever the connection comes back. Renders nothing. */
export function OfflineSync() {
  const t = useTranslations("attendance");
  const router = useRouter();

  useEffect(() => {
    let running = false;
    async function sync() {
      if (running || !navigator.onLine || pendingCount() === 0) return;
      running = true;
      const r = await flushQueue();
      running = false;
      if (r.sent > 0) toast.success(t("synced", { count: r.sent }));
      if (r.clashed.length > 0) toast.error(t("syncClashed"));
      if (r.sent + r.clashed.length > 0) router.refresh();
    }
    void sync();
    window.addEventListener("online", sync);
    return () => window.removeEventListener("online", sync);
  }, [router, t]);

  return null;
}
