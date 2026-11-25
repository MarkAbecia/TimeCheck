"use client";

import { MailCheck } from "lucide-react";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { requestReset, type AuthError } from "@/lib/auth-client";
import type { ForgotInput } from "@/lib/validation/auth";
import { emailRules } from "@/lib/validation/auth-client";
import { buttonVariants } from "@/components/ui/button";
import { Field, FormAlert, SubmitButton } from "./fields";

export function ForgotForm() {
  const t = useTranslations("auth");
  const [sentTo, setSentTo] = useState<string | null>(null);
  const [error, setError] = useState<AuthError | null>(null);
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<ForgotInput>({ mode: "onTouched" });

  if (sentTo) {
    return (
      <div role="status" className="grid gap-4">
        <MailCheck className="size-8 text-success" aria-hidden />
        <div>
          <h2 className="font-display text-2xl font-bold uppercase">{t("linkSentTitle")}</h2>
          <p className="mt-1 text-pretty text-muted-foreground">{t("linkSentBody", { email: sentTo })}</p>
        </div>
        <Link href="/login" className={buttonVariants({ variant: "outline", size: "lg", className: "w-full" })}>
          {t("backToSignIn")}
        </Link>
      </div>
    );
  }

  const onSubmit = handleSubmit(async (values) => {
    setError(null);
    const result = await requestReset(values);
    if (result.ok) setSentTo(values.email);
    else setError(result.error);
  });

  return (
    <form onSubmit={onSubmit} noValidate className="grid gap-4">
      {error && <FormAlert>{t(error)}</FormAlert>}
      <Field
        label={t("email")}
        type="email"
        inputMode="email"
        autoComplete="email"
        autoCapitalize="none"
        spellCheck={false}
        registration={register("email", emailRules)}
        error={errors.email}
      />
      <SubmitButton pending={isSubmitting} label={t("sendLink")} pendingLabel={t("sending")} />
      <Link
        href="/login"
        className="inline-flex min-h-11 items-center justify-center text-sm font-medium text-primary-text underline-offset-4 hover:underline"
      >
        {t("backToSignIn")}
      </Link>
    </form>
  );
}
