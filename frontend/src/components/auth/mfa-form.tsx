"use client";

import { Loader2 } from "lucide-react";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { useEffect, useId, useRef, useState } from "react";
import { enrollMfa, verifyMfa, type AuthError, type MfaEnrollment } from "@/lib/auth-client";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { FormAlert, SubmitButton } from "./fields";

/** Admin TOTP: first visit shows a QR code to enrol; after that it only asks for the current code (§6.1). */
export function MfaForm() {
  const t = useTranslations("auth");
  const router = useRouter();
  const id = useId();
  const [factor, setFactor] = useState<MfaEnrollment | null>(null);
  const [code, setCode] = useState("");
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<AuthError | "mfaCodeInvalid" | null>(null);

  const enrolment = useRef<ReturnType<typeof enrollMfa> | null>(null);

  useEffect(() => {
    // Each enrol call replaces any unverified factor, so start exactly one per mount (React dev runs effects twice).
    let current = true;
    enrolment.current ??= enrollMfa();
    void enrolment.current.then((r) => {
      if (!current) return;
      if (r.ok) setFactor(r.data);
      else setError(r.error);
    });
    return () => {
      current = false;
    };
  }, []);

  if (!factor) {
    return error ? (
      <FormAlert>{t(error)}</FormAlert>
    ) : (
      <p role="status" className="flex items-center gap-2 text-muted-foreground">
        <Loader2 className="size-4 animate-spin" aria-hidden />
        {t("mfaLoading")}
      </p>
    );
  }

  const onSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!/^\d{6}$/.test(code)) return setError("mfaCodeInvalid");
    setError(null);
    setPending(true);
    const r = await verifyMfa(factor.factorId, code);
    setPending(false);
    if (!r.ok) {
      setCode("");
      return setError(r.error);
    }
    router.replace("/");
  };

  return (
    <form onSubmit={onSubmit} noValidate className="grid gap-5">
      {!factor.enrolled && factor.qrCode && (
        <div className="grid gap-3">
          <p>{t("mfaScan")}</p>
          {/* GoTrue returns the QR as an SVG data URL; next/image adds nothing for it. */}
          {/* eslint-disable-next-line @next/next/no-img-element */}
          <img src={factor.qrCode} alt={t("mfaQrAlt")} width={176} height={176} className="size-44 rounded-md bg-white p-2" />
          {factor.secret && (
            <details className="text-sm">
              <summary className="cursor-pointer text-muted-foreground">{t("mfaSecret")}</summary>
              <code className="mt-2 block font-mono text-sm break-all select-all">{factor.secret}</code>
            </details>
          )}
        </div>
      )}
      {error && <FormAlert>{t(error)}</FormAlert>}
      <div className="grid gap-1.5">
        <Label htmlFor={id}>{t("mfaCode")}</Label>
        <p className="text-sm text-muted-foreground">{t("mfaEnter")}</p>
        <Input
          id={id}
          value={code}
          onChange={(e) => setCode(e.target.value.replace(/\D/g, "").slice(0, 6))}
          inputMode="numeric"
          autoComplete="one-time-code"
          pattern="\d{6}"
          maxLength={6}
          aria-invalid={error ? true : undefined}
          className="font-mono text-lg tracking-[0.3em]"
          autoFocus
        />
      </div>
      <SubmitButton pending={pending} label={t("mfaVerify")} pendingLabel={t("mfaVerifying")} />
    </form>
  );
}
