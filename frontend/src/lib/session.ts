import "server-only";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { cache } from "react";
import type { Role } from "./nav";
import { nextOnboardingStep, type Me, type OnboardingStep } from "./onboarding";
import { callApi } from "./server/bff";
import { env } from "./server/env";
import { ACCESS_COOKIE, readClaims } from "./server/tokens";

export type Session = {
  firstName: string;
  initials: string;
  role: Role;
  groupName: string | null;
  churchName: string;
};

/** The signed-in user from the API, once per request. Null when there is no valid session. */
export const getMe = cache(async (): Promise<{ me: Me; aal: string | undefined } | null> => {
  const token = (await cookies()).get(ACCESS_COOKIE)?.value;
  if (!token) return null;
  const res = await callApi("/api/v1/me", token);
  if (res.status === 401) return null;
  if (!res.ok) throw new Error(`GET /me failed with ${res.status}`);
  return { me: (await res.json()) as Me, aal: readClaims(token)?.aal };
});

/** For (app) pages: a signed-in, fully onboarded user, or a redirect to whatever is owed. */
export async function requireSession(): Promise<Session> {
  const current = await getMe();
  if (!current) redirect("/login");
  const step = nextOnboardingStep(current.me, current.aal);
  if (step) redirect(step);
  return toSession(current.me);
}

/** For onboarding pages: signed in and owing exactly this step; otherwise sent where they belong. */
export async function requireStep(step: OnboardingStep): Promise<Me> {
  const current = await getMe();
  if (!current) redirect("/login");
  const owed = nextOnboardingStep(current.me, current.aal);
  if (owed !== step) redirect(owed ?? "/");
  return current.me;
}

/** For the reset page: any signed-in user (the recovery link signs them in). */
export async function requireSignedIn(): Promise<Me> {
  const current = await getMe();
  if (!current) redirect("/forgot-password?expired=1");
  return current.me;
}

function toSession(me: Me): Session {
  const first = me.nickname ?? me.firstName;
  return {
    firstName: first,
    initials: `${me.firstName[0] ?? ""}${me.lastName[0] ?? ""}`.toUpperCase(),
    role: me.role.toLowerCase() as Role,
    groupName: me.groupName,
    churchName: me.churchName,
  };
}

/** Public pages (sign-in) have no session; the church name comes from deployment config. */
export async function getChurchBranding() {
  return { name: env().CHURCH_NAME };
}
