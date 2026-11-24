"use client";

import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { useId, useState } from "react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { send } from "@/lib/api-client";

/** "Reviewed", with an optional private note. It changes nothing else about the devotion (§6.3 fairness rule). */
export function ReviewForm({ id, note, reviewed }: { id: string; note: string | null; reviewed: boolean }) {
  const t = useTranslations("devotions");
  const router = useRouter();
  const field = useId();
  const [value, setValue] = useState(note ?? "");
  const [pending, setPending] = useState(false);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setPending(true);
    const r = await send("POST", `/devotions/${id}/review`, { note: value });
    setPending(false);
    if (r.ok) {
      toast.success(t("reviewSaved"));
      router.refresh();
    } else toast.error(r.problem.status === 0 ? t("offline") : t("unexpected"));
  }

  return (
    <form onSubmit={submit} className="grid gap-2">
      <Label htmlFor={field}>{t("reviewNote")}</Label>
      <textarea
        id={field}
        rows={2}
        maxLength={1000}
        value={value}
        onChange={(e) => setValue(e.target.value)}
        className="w-full min-w-0 rounded-lg border border-input bg-card px-3 py-2 text-base outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 dark:bg-input/30"
      />
      <Button type="submit" variant={reviewed ? "outline" : "default"} disabled={pending} className="w-fit">
        {reviewed ? t("updateReview") : t("markReviewed")}
      </Button>
    </form>
  );
}
