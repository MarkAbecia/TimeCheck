"use client";

import { Loader2 } from "lucide-react";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { useId, useState } from "react";
import { toast } from "sonner";
import { FormAlert } from "@/components/auth/fields";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { send } from "@/lib/api-client";
import { dayLabel, shortTime } from "@/lib/time";
import type { Invite, InviteEvent } from "@/lib/types";

/** Log a guest for a Cell Meeting or Cell Celebration (§6.5): pick the event, type a name, done. */
export function InviteForm({ events, selected }: { events: InviteEvent[]; selected?: string }) {
  const t = useTranslations("invites");
  const router = useRouter();
  const id = useId();
  const [eventId, setEventId] = useState(events.some((e) => e.id === selected) ? selected! : (events[0]?.id ?? ""));
  const [name, setName] = useState("");
  const [contact, setContact] = useState("");
  const [pending, setPending] = useState(false);
  const [alert, setAlert] = useState<string | null>(null);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setAlert(null);
    if (!name.trim()) return setAlert(t("nameRequired"));
    setPending(true);
    const r = await send<Invite>("POST", "/invites", { eventId, guestName: name, guestContact: contact || null });
    setPending(false);
    if (r.ok) {
      toast.success(t("logged", { name: r.data.guestName }));
      setName("");
      setContact("");
      router.refresh();
      return;
    }
    const p = r.problem;
    setAlert(p.status === 0 ? t("offline") : p.code === "invite.event_cancelled" ? t("eventCancelled") : t("unexpected"));
  }

  return (
    <form onSubmit={submit} noValidate className="grid gap-3">
      {alert && <FormAlert>{alert}</FormAlert>}
      <div className="grid gap-1.5">
        <Label htmlFor={`${id}-event`}>{t("event")}</Label>
        <select
          id={`${id}-event`}
          value={eventId}
          onChange={(e) => setEventId(e.target.value)}
          className="h-11 w-full min-w-0 rounded-lg border border-input bg-card px-3 text-base outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 dark:bg-input/30"
        >
          {events.map((e) => (
            <option key={e.id} value={e.id}>
              {dayLabel(new Date(e.startsAt))} {shortTime(new Date(e.startsAt))}, {e.title}
            </option>
          ))}
        </select>
      </div>
      <div className="grid gap-3 sm:grid-cols-2">
        <div className="grid gap-1.5">
          <Label htmlFor={`${id}-name`}>{t("guestName")}</Label>
          <Input id={`${id}-name`} value={name} onChange={(e) => setName(e.target.value)} maxLength={200} autoComplete="off" />
        </div>
        <div className="grid gap-1.5">
          <Label htmlFor={`${id}-contact`}>{t("guestContact")}</Label>
          <Input
            id={`${id}-contact`}
            value={contact}
            onChange={(e) => setContact(e.target.value)}
            maxLength={200}
            autoComplete="off"
            aria-describedby={`${id}-privacy`}
          />
        </div>
      </div>
      <p id={`${id}-privacy`} className="text-sm text-muted-foreground">
        {t("privacy")}
      </p>
      <Button type="submit" size="lg" disabled={pending} className="sm:w-fit">
        {pending && <Loader2 className="size-4 animate-spin" aria-hidden />}
        {pending ? t("logging") : t("log")}
      </Button>
    </form>
  );
}
