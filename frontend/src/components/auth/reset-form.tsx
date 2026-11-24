"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { CircleCheck } from "lucide-react";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { resetPassword, type AuthError } from "@/lib/auth-client";
import { resetSchema, type ResetInput } from "@/lib/validation/auth";
import { buttonVariants } from "@/components/ui/button";
import { FormAlert, PasswordField, SubmitButton } from "./fields";

export function ResetForm() {
  const t = useTranslations("auth");
  const [done, setDone] = useState(false);
  const [error, setError] = useState<AuthError | null>(null);
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<ResetInput>({ resolver: zodResolver(resetSchema), mode: "onTouched" });

  if (done) {
    return (
      <div role="status" className="grid gap-4">
        <CircleCheck className="size-8 text-success" aria-hidden />
        <div>
          <h2 className="font-display text-2xl font-bold uppercase">{t("resetDoneTitle")}</h2>
          <p className="mt-1 text-muted-foreground">{t("resetDoneBody")}</p>
        </div>
        <Link href="/login" className={buttonVariants({ size: "lg", className: "w-full" })}>
          {t("backToSignIn")}
        </Link>
      </div>
    );
  }

  const onSubmit = handleSubmit(async ({ password }) => {
    setError(null);
    const result = await resetPassword(password);
    if (result.ok) setDone(true);
    else setError(result.error);
  });

  return (
    <form onSubmit={onSubmit} noValidate className="grid gap-4">
      {error && (
        <FormAlert>
          {t(error)}
          {error === "resetExpired" && (
            <>
              {" "}
              <Link href="/forgot-password" className="font-semibold underline">
                {t("requestNewLink")}
              </Link>
            </>
          )}
        </FormAlert>
      )}
      <PasswordField label={t("newPassword")} autoComplete="new-password" registration={register("password")} error={errors.password} />
      <PasswordField label={t("confirmPassword")} autoComplete="new-password" registration={register("confirm")} error={errors.confirm} />
      <SubmitButton pending={isSubmitting} label={t("savePassword")} pendingLabel={t("saving")} />
    </form>
  );
}
