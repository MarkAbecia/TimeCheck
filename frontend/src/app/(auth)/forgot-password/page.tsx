import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { AuthCard } from "@/components/auth/auth-card";
import { ForgotForm } from "@/components/auth/forgot-form";

export const metadata: Metadata = { title: "Reset password" };

export default async function ForgotPasswordPage({ searchParams }: PageProps<"/forgot-password">) {
  const [t, { expired }] = await Promise.all([getTranslations("auth"), searchParams]);
  return (
    <AuthCard title={expired ? t("expiredLinkTitle") : t("forgotTitle")} lead={expired ? t("expiredLinkBody") : t("forgotLead")}>
      <ForgotForm />
    </AuthCard>
  );
}
