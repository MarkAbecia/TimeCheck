"use client";

import { Loader2, Plus, X } from "lucide-react";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { useState } from "react";
import { FormAlert } from "@/components/auth/fields";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { send } from "@/lib/api-client";
import type { ChurchSettings } from "@/lib/types";

const MAX_BRACKETS = 8;
// Same bounds as the API validator (UpdateChurchSettingsValidator).
const LIMITS = { guestInviteDays: [1, 365], deactivatedUserYears: [1, 10], auditLogDays: [365, 3650] } as const;
type RetentionKey = keyof typeof LIMITS;

const whole = (s: string) => (/^\d{1,4}$/.test(s.trim()) ? Number(s) : NaN);

/** Age groups as the Member Report labels them: "12 and under", "13–17", …, "36 and over". */
function ageGroups(bounds: number[]) {
  return [...bounds.map((b, i) => (i === 0 ? `${b} and under` : `${bounds[i - 1] + 1}–${b}`)), `${bounds[bounds.length - 1] + 1} and over`];
}

/** Super Admin settings, first pass (§6.10): age brackets for the Member Report and retention periods (§8.4). */
export function ChurchSettingsForm({ initial }: { initial: ChurchSettings }) {
  const t = useTranslations("settings");
  const router = useRouter();
  const [brackets, setBrackets] = useState(initial.ageBrackets.map(String));
  const [retention, setRetention] = useState<Record<RetentionKey, string>>({
    guestInviteDays: String(initial.retention.guestInviteDays),
    deactivatedUserYears: String(initial.retention.deactivatedUserYears),
    auditLogDays: String(initial.retention.auditLogDays),
  });
  const [checked, setChecked] = useState(false);
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);

  const ages = brackets.map(whole);
  const ageErrors = ages.map((a, i) =>
    !(a >= 1 && a <= 120) ? t("ageRange") : i > 0 && !(a > ages[i - 1]) ? t("ageOrder", { previous: brackets[i - 1] }) : null,
  );
  const retentionErrors = Object.fromEntries(
    (Object.keys(LIMITS) as RetentionKey[]).map((k) => {
      const v = whole(retention[k]);
      const [min, max] = LIMITS[k];
      return [k, v >= min && v <= max ? null : t("between", { min, max })];
    }),
  ) as Record<RetentionKey, string | null>;
  const valid = ageErrors.every((e) => !e) && Object.values(retentionErrors).every((e) => !e);

  const edit = (fn: () => void) => {
    fn();
    setSaved(false);
  };

  async function save(e: React.FormEvent) {
    e.preventDefault();
    setChecked(true);
    setError(null);
    if (!valid) return;
    setPending(true);
    const r = await send<ChurchSettings>("PATCH", "/admin/settings", {
      ageBrackets: ages,
      retention: {
        guestInviteDays: whole(retention.guestInviteDays),
        deactivatedUserYears: whole(retention.deactivatedUserYears),
        auditLogDays: whole(retention.auditLogDays),
      },
    });
    setPending(false);
    if (!r.ok) return setError(r.problem.status === 0 ? t("offline") : r.problem.status === 403 ? t("needsMfa") : t("unexpected"));
    setSaved(true);
    router.refresh();
  }

  return (
    <form onSubmit={save} noValidate className="grid gap-6">
      {error && <FormAlert>{error}</FormAlert>}

      <section aria-labelledby="ages-title" className="grid gap-3 rounded-[10px] border border-border bg-card p-4">
        <div className="grid gap-1">
          <h2 id="ages-title" className="font-semibold">
            {t("agesTitle")}
          </h2>
          <p className="text-sm text-muted-foreground">{t("agesLead")}</p>
        </div>
        <ol className="grid gap-2">
          {brackets.map((value, i) => {
            const id = `age-${i}`;
            const err = checked ? ageErrors[i] : null;
            return (
              <li key={i} className="grid gap-1">
                <div className="flex items-end gap-2">
                  <div className="grid flex-1 gap-1.5">
                    <Label htmlFor={id}>{t("ageLimit", { n: i + 1 })}</Label>
                    <Input
                      id={id}
                      inputMode="numeric"
                      value={value}
                      aria-invalid={Boolean(err)}
                      aria-describedby={err ? `${id}-error` : undefined}
                      onChange={(e) => edit(() => setBrackets((b) => b.map((x, j) => (j === i ? e.target.value : x))))}
                    />
                  </div>
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon"
                    disabled={brackets.length === 1}
                    aria-label={t("removeAge", { n: i + 1 })}
                    onClick={() => edit(() => setBrackets((b) => b.filter((_, j) => j !== i)))}
                  >
                    <X className="size-4" aria-hidden />
                  </Button>
                </div>
                {err && (
                  <p id={`${id}-error`} className="text-sm text-destructive">
                    {err}
                  </p>
                )}
              </li>
            );
          })}
        </ol>
        <Button
          type="button"
          variant="outline"
          className="w-fit"
          disabled={brackets.length >= MAX_BRACKETS}
          onClick={() => edit(() => setBrackets((b) => [...b, String((whole(b[b.length - 1]) || 0) + 10)]))}
        >
          <Plus className="size-4" aria-hidden />
          {t("addAge")}
        </Button>
        <p className="text-sm" aria-live="polite">
          {ageErrors.every((e) => !e) ? t("agesPreview", { groups: ageGroups(ages).join(", ") }) : t("agesPreviewWaiting")}
        </p>
      </section>

      <section aria-labelledby="retention-title" className="grid gap-3 rounded-[10px] border border-border bg-card p-4">
        <div className="grid gap-1">
          <h2 id="retention-title" className="font-semibold">
            {t("retentionTitle")}
          </h2>
          <p className="text-sm text-muted-foreground">{t("retentionLead")}</p>
        </div>
        {(Object.keys(LIMITS) as RetentionKey[]).map((k) => {
          const err = checked ? retentionErrors[k] : null;
          return (
            <div key={k} className="grid gap-1.5">
              <Label htmlFor={k}>{t(`${k}.label`)}</Label>
              <p id={`${k}-hint`} className="text-sm text-muted-foreground">
                {t(`${k}.hint`)}
              </p>
              <div className="flex items-center gap-2">
                <Input
                  id={k}
                  inputMode="numeric"
                  className="max-w-32"
                  value={retention[k]}
                  aria-invalid={Boolean(err)}
                  aria-describedby={err ? `${k}-hint ${k}-error` : `${k}-hint`}
                  onChange={(e) => edit(() => setRetention((r) => ({ ...r, [k]: e.target.value })))}
                />
                <span className="text-sm text-muted-foreground">{t(`${k}.unit`)}</span>
              </div>
              {err && (
                <p id={`${k}-error`} className="text-sm text-destructive">
                  {err}
                </p>
              )}
            </div>
          );
        })}
      </section>

      <div className="flex flex-wrap items-center gap-3">
        <Button type="submit" disabled={pending}>
          {pending && <Loader2 className="size-4 animate-spin" aria-hidden />}
          {t("save")}
        </Button>
        {saved && (
          <p role="status" className="text-sm">
            {t("saved")}
          </p>
        )}
      </div>
    </form>
  );
}
