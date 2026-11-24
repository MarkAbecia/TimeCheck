"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { toast } from "sonner";
import { setPassword, type AuthError } from "@/lib/auth-client";
import { resetSchema, type ResetInput } from "@/lib/validation/auth";
import { FormAlert, PasswordField, SubmitButton } from "./fields";

/** New password after a reset link, or the forced change on first sign-in. Either way the user is signed in. */
export function ResetForm() {
  const t = useTranslations("auth");
  const router = useRouter();
  const [error, setError] = useState<AuthError | null>(null);
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<ResetInput>({
    resolver: zodResolver(resetSchema),
    mode: "onTouched",
  });

  const onSubmit = handleSubmit(async ({ password }) => {
    setError(null);
    const result = await setPassword(password);
    if (!result.ok) return setError(result.error);
    toast.success(t("resetDoneTitle"), { description: t("resetDoneBody") });
    // The app layout sends the user on to MFA or consent if either is still owed.
    router.replace("/");
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
