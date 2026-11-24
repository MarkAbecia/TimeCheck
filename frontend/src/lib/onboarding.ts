/** GET /api/v1/me, as the API returns it (backend MeDto). */
export type Me = {
  id: string;
  firstName: string;
  lastName: string;
  nickname: string | null;
  displayName: string;
  email: string;
  role: "SUPER_ADMIN" | "ADMIN" | "LEADER" | "MEMBER";
  groupId: string | null;
  ledGroupIds: string[];
  mustChangePassword: boolean;
  privacyConsentVersion: string | null;
  requiredPrivacyConsentVersion: string;
  mfaRequired: boolean;
  churchName: string;
  groupName: string | null;
};

export type OnboardingStep = "/change-password" | "/mfa" | "/consent";

/**
 * What a signed-in user still owes before the app opens, in order (§6.1, §8.4). The API enforces the password and
 * consent steps itself and MFA on admin endpoints; this only routes the user to the right page.
 */
export function nextOnboardingStep(me: Me, aal: string | undefined): OnboardingStep | null {
  if (me.mustChangePassword) return "/change-password";
  if (me.mfaRequired && aal !== "aal2") return "/mfa";
  if (me.privacyConsentVersion !== me.requiredPrivacyConsentVersion) return "/consent";
  return null;
}
