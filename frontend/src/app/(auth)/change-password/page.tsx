import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { AuthCard } from "@/components/auth/auth-card";
import { ResetForm } from "@/components/auth/reset-form";
import { SignOutButton } from "@/components/auth/sign-out-button";
import { requireStep } from "@/lib/session";

export const metadata: Metadata = { title: "Set your password" };

// First sign-in with the temporary password a leader or admin gave out (§6.1).
export default async function ChangePasswordPage() {
  const [t] = await Promise.all([getTranslations("auth"), requireStep("/change-password")]);
  return (
    <AuthCard title={t("changeTitle")} lead={t("changeLead")}>
      <ResetForm />
      <SignOutButton label={t("notYou")} className="mt-4" />
    </AuthCard>
  );
}
