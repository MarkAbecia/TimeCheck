"use client";

import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { useTranslations } from "next-intl";
import { useState } from "react";
import type { FieldError, UseFormRegisterReturn } from "react-hook-form";
import { login, type AuthError } from "@/lib/auth-client";
import { emailProblem, normalizeEmail } from "@/lib/validation/auth-client";
import { safeNext } from "@/lib/safe-next";
import { Field, FormAlert, PasswordField, SubmitButton } from "./fields";

type Name = "email" | "password";
const problem = (name: Name, value: unknown) => (name === "email" ? emailProblem(value) : value ? undefined : "passwordRequired");

// Plain React rather than react-hook-form, which would add 12 KB to the first page everyone loads (§9). Same behaviour: a field
// is checked when it loses focus, then as it changes, and everything on submit.
export function LoginForm() {
  const t = useTranslations("auth");
  const router = useRouter();
  const next = safeNext(useSearchParams().get("next"));
  const [error, setError] = useState<AuthError | null>(null);
  const [errors, setErrors] = useState<Partial<Record<Name, string>>>({});
  const [touched, setTouched] = useState<Partial<Record<Name, boolean>>>({});
  const [pending, setPending] = useState(false);

  const recheck = (name: Name, value: string) => setErrors((e) => ({ ...e, [name]: problem(name, value) }));
  const field = (name: Name): UseFormRegisterReturn => ({
    name,
    ref: () => {},
    onBlur: async (e) => {
      setTouched((s) => ({ ...s, [name]: true }));
      recheck(name, e.target.value);
    },
    onChange: async (e) => {
      if (touched[name]) recheck(name, e.target.value);
    },
  });
  const fieldError = (name: Name): FieldError | undefined => (errors[name] ? { type: "validate", message: errors[name] } : undefined);

  async function onSubmit(e: React.FormEvent<HTMLFormElement>) {
    e.preventDefault();
    const form = new FormData(e.currentTarget);
    const values = { email: normalizeEmail(form.get("email")), password: String(form.get("password") ?? "") };
    const found = { email: problem("email", values.email), password: problem("password", values.password) };
    setErrors(found);
    setTouched({ email: true, password: true });
    if (found.email || found.password) return;
    setError(null);
    setPending(true);
    const result = await login(values);
    if (result.ok) return router.replace(next);
    setPending(false);
    setError(result.error);
  }

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
        registration={field("email")}
        error={fieldError("email")}
      />
      <PasswordField
        label={t("password")}
        autoComplete="current-password"
        registration={field("password")}
        error={fieldError("password")}
      />
      <div className="-mt-1 flex justify-end">
        <Link
          href="/forgot-password"
          className="inline-flex min-h-11 items-center text-sm font-medium text-primary-text underline-offset-4 hover:underline"
        >
          {t("forgot")}
        </Link>
      </div>
      <SubmitButton pending={pending} label={t("submit")} pendingLabel={t("submitting")} />
    </form>
  );
}
