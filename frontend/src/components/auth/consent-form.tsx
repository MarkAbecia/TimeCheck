"use client";

import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { useState } from "react";
import { acceptConsent, type AuthError } from "@/lib/auth-client";
import { FormAlert, SubmitButton } from "./fields";

export function ConsentForm({ version }: { version: string }) {
  const t = useTranslations("auth");
  const router = useRouter();
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<AuthError | null>(null);

  const onSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    setPending(true);
    const r = await acceptConsent(version);
    setPending(false);
    if (r.ok) router.replace("/");
    else setError(r.error);
  };

  return (
    <form onSubmit={onSubmit} className="grid gap-4">
      {error && <FormAlert>{t(error)}</FormAlert>}
      <SubmitButton pending={pending} label={t("consentAccept")} pendingLabel={t("consentAccepting")} />
    </form>
  );
}
