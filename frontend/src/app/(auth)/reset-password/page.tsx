import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { AuthCard } from "@/components/auth/auth-card";
import { ResetForm } from "@/components/auth/reset-form";
import { requireSignedIn } from "@/lib/session";

export const metadata: Metadata = { title: "New password" };

// Reached from the email link via /auth/confirm, which signed the user in with a recovery session.
export default async function ResetPasswordPage() {
  const [t] = await Promise.all([getTranslations("auth"), requireSignedIn()]);
  return (
    <AuthCard title={t("resetTitle")} lead={t("resetLead")}>
      <ResetForm />
    </AuthCard>
  );
}
