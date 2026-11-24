import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { AuthCard } from "@/components/auth/auth-card";
import { LoginForm } from "@/components/auth/login-form";

export const metadata: Metadata = { title: "Sign in" };

export default async function LoginPage() {
  const t = await getTranslations("auth");
  return (
    <>
      <AuthCard title={t("signInTitle")} lead={t("signInLead")}>
        <LoginForm />
      </AuthCard>
      <p className="mx-auto mt-5 max-w-[400px] px-2 text-center text-sm text-muted-foreground">{t("noAccount")}</p>
    </>
  );
}
