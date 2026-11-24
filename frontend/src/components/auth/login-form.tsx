"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { useTranslations } from "next-intl";
import { useState } from "react";
import { useForm } from "react-hook-form";
import type { z } from "zod";
import { login, type AuthError } from "@/lib/auth-client";
import { loginSchema, type LoginInput } from "@/lib/validation/auth";
import { safeNext } from "@/lib/safe-next";
import { Field, FormAlert, PasswordField, SubmitButton } from "./fields";

export function LoginForm() {
  const t = useTranslations("auth");
  const router = useRouter();
  const next = safeNext(useSearchParams().get("next"));
  const [error, setError] = useState<AuthError | null>(null);
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<z.input<typeof loginSchema>, unknown, LoginInput>({ resolver: zodResolver(loginSchema), mode: "onTouched" });

  const onSubmit = handleSubmit(async (values) => {
    setError(null);
    const result = await login(values);
    if (result.ok) router.replace(next);
    else setError(result.error);
  });

  return (
    <form onSubmit={onSubmit} noValidate className="grid gap-4">
      {error && <FormAlert>{t(error)}</FormAlert>}
      <Field
        label={t("email")}
        type="email"
        inputMode="email"
        autoComplete="username"
        autoCapitalize="none"
        spellCheck={false}
        registration={register("email")}
        error={errors.email}
      />
      <PasswordField label={t("password")} autoComplete="current-password" registration={register("password")} error={errors.password} />
      <div className="-mt-1 flex justify-end">
        <Link href="/forgot-password" className="inline-flex min-h-11 items-center text-sm font-medium text-primary-text underline-offset-4 hover:underline">
          {t("forgot")}
        </Link>
      </div>
      <SubmitButton pending={isSubmitting} label={t("submit")} pendingLabel={t("submitting")} />
    </form>
  );
}
