"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { toast } from "sonner";
import { Field, FormAlert, PasswordField, SubmitButton } from "@/components/auth/fields";
import { changePassword, type AuthError } from "@/lib/auth-client";
import { send } from "@/lib/api-client";
import { changePasswordSchema, type ChangePasswordInput } from "@/lib/validation/auth";
import { profileSchema, type ProfileInput } from "@/lib/validation/member";

const blank = (s: string) => (s.trim() === "" ? null : s.trim());

export function ProfileForm({ initial }: { initial: ProfileInput }) {
  const t = useTranslations("profile");
  const router = useRouter();
  const [alert, setAlert] = useState<string | null>(null);
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
    reset,
  } = useForm<ProfileInput>({ resolver: zodResolver(profileSchema), mode: "onTouched", defaultValues: initial });

  const onSubmit = handleSubmit(async (v) => {
    setAlert(null);
    const r = await send("PATCH", "/me", { nickname: blank(v.nickname), mobile: blank(v.mobile), address: blank(v.address) });
    if (r.ok) {
      toast.success(t("saved"));
      reset(v);
      router.refresh();
    } else setAlert(t(r.problem.status === 0 ? "offline" : "unexpected"));
  });

  return (
    <form onSubmit={onSubmit} noValidate className="grid gap-4">
      {alert && <FormAlert>{alert}</FormAlert>}
      <Field label={t("nickname")} autoComplete="nickname" registration={register("nickname")} error={errors.nickname} />
      <Field label={t("mobile")} type="tel" inputMode="tel" autoComplete="tel" registration={register("mobile")} error={errors.mobile} />
      <Field label={t("address")} autoComplete="street-address" registration={register("address")} error={errors.address} />
      <SubmitButton pending={isSubmitting} label={t("save")} pendingLabel={t("saving")} />
    </form>
  );
}

export function PasswordChangeForm() {
  const t = useTranslations("profile");
  const ta = useTranslations("auth");
  const [error, setError] = useState<AuthError | null>(null);
  const {
    register,
    handleSubmit,
    reset,
    formState: { errors, isSubmitting },
  } = useForm<ChangePasswordInput>({ resolver: zodResolver(changePasswordSchema), mode: "onTouched" });

  const onSubmit = handleSubmit(async (v) => {
    setError(null);
    const r = await changePassword(v.current, v.password);
    if (r.ok) {
      toast.success(t("passwordChanged"));
      reset({ current: "", password: "", confirm: "" });
    } else setError(r.error);
  });

  return (
    <form onSubmit={onSubmit} noValidate className="grid gap-4">
      {error && <FormAlert>{ta(error)}</FormAlert>}
      <PasswordField
        label={t("currentPassword")}
        autoComplete="current-password"
        registration={register("current")}
        error={errors.current}
      />
      <PasswordField label={ta("newPassword")} autoComplete="new-password" registration={register("password")} error={errors.password} />
      <PasswordField label={ta("confirmPassword")} autoComplete="new-password" registration={register("confirm")} error={errors.confirm} />
      <SubmitButton pending={isSubmitting} label={t("changePassword")} pendingLabel={ta("saving")} />
    </form>
  );
}
