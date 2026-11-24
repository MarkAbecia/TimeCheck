"use client";

import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { useState } from "react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { send } from "@/lib/api-client";

/** Two taps on purpose: cancelling notifies everyone expected (§6.6). */
export function CancelEvent({ id, title }: { id: string; title: string }) {
  const t = useTranslations("events");
  const router = useRouter();
  const [confirming, setConfirming] = useState(false);
  const [pending, setPending] = useState(false);

  async function cancel() {
    setPending(true);
    const r = await send("POST", `/events/${id}/cancel`);
    setPending(false);
    setConfirming(false);
    if (r.ok) {
      toast.success(t("cancelled", { title }));
      router.refresh();
    } else toast.error(r.problem.status === 0 ? t("offline") : t("unexpected"));
  }

  return confirming ? (
    <div className="flex flex-wrap items-center gap-2">
      <span className="text-sm">{t("cancelConfirm")}</span>
      <Button variant="destructive" size="sm" onClick={cancel} disabled={pending}>
        {t("cancelYes")}
      </Button>
      <Button variant="ghost" size="sm" onClick={() => setConfirming(false)}>
        {t("keep")}
      </Button>
    </div>
  ) : (
    <Button variant="ghost" size="sm" onClick={() => setConfirming(true)}>
      {t("cancelEvent")}
    </Button>
  );
}
