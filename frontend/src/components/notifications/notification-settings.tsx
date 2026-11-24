"use client";

import { Loader2 } from "lucide-react";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { useEffect, useState } from "react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { send } from "@/lib/api-client";
import { currentSubscription, disablePush, enablePush, pushSupported } from "@/lib/push";
import type { CategoryPreference, NotificationPreferences } from "@/lib/types";

type Device = "checking" | "unsupported" | "blocked" | "off" | "on";

/**
 * Which channels each kind of notification uses (§6.7), and push for this browser. In-app is always on, so nothing is
 * ever lost by turning the others off.
 */
export function NotificationSettings({ initial }: { initial: NotificationPreferences }) {
  const t = useTranslations("notifications");
  const router = useRouter();
  const [prefs, setPrefs] = useState<CategoryPreference[]>(initial.categories);
  const [device, setDevice] = useState<Device>("checking");
  const [busy, setBusy] = useState<"device" | "save" | null>(null);

  // Only the browser knows this, so it's read after the first render.
  useEffect(() => {
    let live = true;
    void (async () => {
      const state: Device = !pushSupported()
        ? "unsupported"
        : Notification.permission === "denied"
          ? "blocked"
          : (await currentSubscription())
            ? "on"
            : "off";
      if (live) setDevice(state);
    })();
    return () => {
      live = false;
    };
  }, []);

  async function toggleDevice() {
    setBusy("device");
    if (device === "on") {
      await disablePush();
      setDevice("off");
    } else {
      const r = await enablePush(initial.pushPublicKey!);
      setDevice(r === "on" ? "on" : r === "denied" ? "blocked" : "off");
      if (r === "on") toast.success(t("deviceOn"));
      if (r === "failed") toast.error(t("unexpected"));
    }
    setBusy(null);
    router.refresh();
  }

  async function save(e: React.FormEvent) {
    e.preventDefault();
    setBusy("save");
    const r = await send("PUT", "/me/notification-preferences", { categories: prefs });
    setBusy(null);
    if (r.ok) toast.success(t("saved"));
    else toast.error(r.problem.status === 0 ? t("offline") : t("unexpected"));
  }

  const set = (category: string, channel: "email" | "push", on: boolean) =>
    setPrefs((p) => p.map((c) => (c.category === category ? { ...c, [channel]: on } : c)));

  return (
    <div className="grid gap-4">
      <div className="grid gap-2 rounded-lg border border-border bg-muted/40 p-3">
        <p className="text-sm">
          {!initial.pushAvailable ? t("pushNotSetUp") : device === "checking" ? t("deviceChecking") : t(`device.${device}`)}
        </p>
        {initial.pushAvailable && (device === "on" || device === "off") && (
          <Button
            type="button"
            size="sm"
            variant={device === "on" ? "outline" : "default"}
            className="w-fit"
            disabled={busy !== null}
            onClick={toggleDevice}
          >
            {busy === "device" && <Loader2 className="size-4 animate-spin" aria-hidden />}
            {device === "on" ? t("turnOff") : t("turnOn")}
          </Button>
        )}
      </div>

      <form onSubmit={save} className="grid gap-3">
        <p className="text-sm text-muted-foreground">{initial.emailAvailable ? t("alwaysInApp") : t("emailNotSetUp")}</p>
        {prefs.map((c) => (
          <fieldset key={c.category} className="grid gap-1.5">
            <legend className="font-medium">{t(`category.${c.category}`)}</legend>
            <p className="text-sm text-muted-foreground">{t(`categoryHint.${c.category}`)}</p>
            <div className="flex gap-5">
              {(["email", "push"] as const).map((ch) => (
                <label key={ch} className="flex min-h-11 items-center gap-2 text-sm">
                  <input
                    type="checkbox"
                    className="size-5 accent-primary"
                    checked={c[ch]}
                    disabled={ch === "email" ? !initial.emailAvailable : !initial.pushAvailable}
                    onChange={(e) => set(c.category, ch, e.target.checked)}
                  />
                  {t(`channel.${ch}`)}
                </label>
              ))}
            </div>
          </fieldset>
        ))}
        <Button type="submit" className="w-fit" disabled={busy !== null}>
          {busy === "save" && <Loader2 className="size-4 animate-spin" aria-hidden />}
          {t("save")}
        </Button>
      </form>
    </div>
  );
}
