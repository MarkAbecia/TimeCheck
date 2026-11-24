import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { NotificationSettings } from "@/components/notifications/notification-settings";
import { PasswordChangeForm, ProfileForm } from "@/components/profile/profile-forms";
import { apiGet } from "@/lib/server/api";
import { atLeast, requireSession } from "@/lib/session";
import type { NotificationPreferences } from "@/lib/types";

export const metadata: Metadata = { title: "Profile" };

type MyProfile = { displayName: string; email: string; nickname: string | null; mobile: string | null; address: string | null };

export default async function ProfilePage() {
  const session = await requireSession();
  const [t, me, prefs] = await Promise.all([
    getTranslations("profile"),
    apiGet<MyProfile>("/me"),
    apiGet<NotificationPreferences>("/me/notification-preferences"),
  ]);
  return (
    <div className="mx-auto grid w-full max-w-xl gap-6">
      <div>
        <h1 className="font-display text-3xl font-bold uppercase">{me.displayName}</h1>
        <p className="text-muted-foreground">{me.email}</p>
      </div>
      <section aria-labelledby="details" className="grid gap-3 rounded-[10px] border border-border bg-card p-4">
        <h2 id="details" className="font-semibold">
          {t("detailsTitle")}
        </h2>
        <ProfileForm initial={{ nickname: me.nickname ?? "", mobile: me.mobile ?? "", address: me.address ?? "" }} />
      </section>
      <section
        id="notifications"
        aria-labelledby="notifications-title"
        className="grid scroll-mt-20 gap-3 rounded-[10px] border border-border bg-card p-4"
      >
        <h2 id="notifications-title" className="font-semibold">
          {t("notificationsTitle")}
        </h2>
        <NotificationSettings
          // Only leaders mark attendance; members never get those.
          initial={{ ...prefs, categories: prefs.categories.filter((c) => c.category !== "attendance" || atLeast(session.role, "leader")) }}
        />
      </section>
      <section aria-labelledby="password" className="grid gap-3 rounded-[10px] border border-border bg-card p-4">
        <h2 id="password" className="font-semibold">
          {t("passwordTitle")}
        </h2>
        <PasswordChangeForm />
      </section>
    </div>
  );
}
