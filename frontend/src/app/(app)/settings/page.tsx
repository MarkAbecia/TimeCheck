import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { ChurchSettingsForm } from "@/components/settings/church-settings-form";
import { apiGet } from "@/lib/server/api";
import { requireRole } from "@/lib/session";
import type { ChurchSettings } from "@/lib/types";

export const metadata: Metadata = { title: "Settings" };

/** Super Admin settings (§6.10). First pass: age groups and how long data is kept. */
export default async function SettingsPage() {
  await requireRole("super_admin");
  const [t, settings] = await Promise.all([getTranslations("settings"), apiGet<ChurchSettings>("/admin/settings")]);
  return (
    <div className="mx-auto grid w-full max-w-xl gap-6">
      <div>
        <h1 className="font-display text-3xl font-bold uppercase">{t("title")}</h1>
        <p className="text-muted-foreground">{t("lead")}</p>
      </div>
      <ChurchSettingsForm initial={settings} />
    </div>
  );
}
