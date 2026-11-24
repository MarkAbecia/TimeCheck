import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { AuthCard } from "@/components/auth/auth-card";
import { ConsentForm } from "@/components/auth/consent-form";
import { SignOutButton } from "@/components/auth/sign-out-button";
import { requireStep } from "@/lib/session";

export const metadata: Metadata = { title: "Your privacy" };

const POINTS = ["consentPoint1", "consentPoint2", "consentPoint3", "consentPoint4"] as const;

// Privacy notice and consent on first sign-in, and again whenever the notice version changes (§8.4).
export default async function ConsentPage() {
  const [t, me] = await Promise.all([getTranslations("auth"), requireStep("/consent")]);
  return (
    <AuthCard title={t("consentTitle")} lead={t("consentLead")}>
      <ul className="mb-4 grid list-disc gap-2 pl-5">
        {POINTS.map((k) => (
          <li key={k} className="text-pretty">
            {t(k)}
          </li>
        ))}
      </ul>
      <p className="mb-6 text-sm text-muted-foreground">{t("consentLaw")}</p>
      <ConsentForm version={me.requiredPrivacyConsentVersion} />
      <SignOutButton label={t("notYou")} className="mt-4" />
    </AuthCard>
  );
}
