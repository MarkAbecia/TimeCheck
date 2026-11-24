"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { useEffect, useState } from "react";
import { toast } from "sonner";
import { Button, buttonVariants } from "@/components/ui/button";
import { send } from "@/lib/api-client";
import { dayLabel, shortTime } from "@/lib/time";
import type { Invite, InviteStatus } from "@/lib/types";
import { cn } from "@/lib/utils";

/**
 * One guest: who, which event, and whether they came. Marking opens once the event has started; a guest who came can be
 * registered by the leader, after which the invite is fixed.
 */
export function InviteItem({ invite, started, showInviter }: { invite: Invite; started: boolean; showInviter: boolean }) {
  const t = useTranslations("invites");
  const router = useRouter();
  const [pending, setPending] = useState(false);
  const [confirming, setConfirming] = useState(false);
  const converted = invite.convertedUserId !== null;

  // The second tap to remove has to come soon after the first.
  useEffect(() => {
    if (!confirming) return;
    const timer = setTimeout(() => setConfirming(false), 4000);
    return () => clearTimeout(timer);
  }, [confirming]);

  async function run(call: Promise<{ ok: boolean; problem?: { status: number } }>, done: string) {
    setPending(true);
    const r = await call;
    setPending(false);
    if (r.ok) {
      toast.success(done);
      router.refresh();
    } else toast.error(r.problem?.status === 0 ? t("offline") : t("unexpected"));
  }

  const mark = (status: InviteStatus) =>
    run(send("PUT", `/invites/${invite.id}/status`, { status: invite.status === status ? "EXPECTED" : status }), t("saved"));

  const register = `/members/new?${new URLSearchParams({ group: invite.inviterGroupId ?? "", invite: invite.id, name: invite.guestName })}`;

  return (
    <li className="grid gap-2 px-4 py-3">
      <div className="flex flex-wrap items-baseline justify-between gap-x-3 gap-y-1">
        <div className="min-w-0">
          <p className="font-medium">{invite.guestName}</p>
          {invite.guestContact && <p className="text-sm text-muted-foreground">{invite.guestContact}</p>}
        </div>
        <p className="text-sm text-muted-foreground">
          {showInviter && `${t("invitedBy", { name: invite.inviterName })}, `}
          {invite.eventTitle}, {dayLabel(new Date(invite.eventStartsAt))} {shortTime(new Date(invite.eventStartsAt))}
        </p>
      </div>

      {invite.eventCancelled ? (
        <p className="text-sm text-muted-foreground">{t("cancelled")}</p>
      ) : converted ? (
        <p className="text-sm font-medium text-success">{t("status.CONVERTED")}</p>
      ) : (
        <div className="flex flex-wrap items-center gap-2">
          {started ? (
            <div role="group" aria-label={t("didTheyCome", { name: invite.guestName })} className="flex gap-2">
              {(["CAME", "DID_NOT_COME"] as const).map((s) => (
                <Button
                  key={s}
                  type="button"
                  size="sm"
                  variant={invite.status === s ? "secondary" : "outline"}
                  aria-pressed={invite.status === s}
                  disabled={pending}
                  onClick={() => mark(s)}
                >
                  {t(`mark.${s}`)}
                </Button>
              ))}
            </div>
          ) : (
            <p className="text-sm text-muted-foreground">{t("status.EXPECTED")}</p>
          )}
          {invite.canConvert && (
            <Link href={register} className={buttonVariants({ size: "sm" })}>
              {t("register")}
            </Link>
          )}
          <Button
            type="button"
            size="sm"
            variant="ghost"
            disabled={pending}
            className={cn("ml-auto", confirming && "text-destructive")}
            onClick={() => (confirming ? run(send("DELETE", `/invites/${invite.id}`), t("removed")) : setConfirming(true))}
          >
            {confirming ? t("confirmRemove") : t("remove")}
          </Button>
        </div>
      )}
    </li>
  );
}
