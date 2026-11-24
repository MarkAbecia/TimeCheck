import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { AuthCard } from "@/components/auth/auth-card";
import { MfaForm } from "@/components/auth/mfa-form";
import { SignOutButton } from "@/components/auth/sign-out-button";
import { requireStep } from "@/lib/session";

export const metadata: Metadata = { title: "Sign-in code" };

export default async function MfaPage() {
  const [t] = await Promise.all([getTranslations("auth"), requireStep("/mfa")]);
  return (
    <AuthCard title={t("mfaTitle")} lead={t("mfaLead")}>
      <MfaForm />
      <SignOutButton label={t("notYou")} className="mt-4" />
    </AuthCard>
  );
}
