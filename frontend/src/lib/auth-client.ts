import type { ForgotInput, LoginInput } from "./validation/auth";

export type AuthError =
  "invalidCredentials" | "rateLimited" | "offline" | "unexpected" | "resetExpired" | "weakPassword" | "mfaWrong" | "consentStale";
export type AuthResult<T = undefined> = { ok: true; data: T } | { ok: false; error: AuthError };

// BFF route handlers (/api/auth/*) hold the session in httpOnly cookies; tokens never reach client JS (§6.1).
async function post<T = undefined>(path: string, body: unknown, errors: Partial<Record<number, AuthError>>): Promise<AuthResult<T>> {
  if (typeof navigator !== "undefined" && !navigator.onLine) return { ok: false, error: "offline" };
  try {
    const res = await fetch(path, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body ?? {}),
      credentials: "same-origin",
    });
    if (res.ok)
      return {
        ok: true,
        data: (res.status === 204 ? undefined : await res.json()) as T,
      };
    if (res.status === 429) return { ok: false, error: "rateLimited" };
    return { ok: false, error: errors[res.status] ?? "unexpected" };
  } catch {
    return { ok: false, error: "unexpected" };
  }
}

export const login = (input: LoginInput) =>
  post("/api/auth/login", input, {
    400: "invalidCredentials",
    401: "invalidCredentials",
  });
// The server answers 204 whether or not the email exists, so the UI can't reveal it either.
export const requestReset = (input: ForgotInput) => post("/api/auth/forgot-password", input, {});
// 401: the reset (or first-login) session is gone; 400: the password failed the server's policy.
export const setPassword = (password: string) => post("/api/auth/password", { password }, { 400: "weakPassword", 401: "resetExpired" });

export type MfaEnrollment = {
  factorId: string;
  enrolled: boolean;
  qrCode?: string;
  secret?: string;
};
export const enrollMfa = () => post<MfaEnrollment>("/api/auth/mfa/enroll", {}, {});
export const verifyMfa = (factorId: string, code: string) => post("/api/auth/mfa/verify", { factorId, code }, { 400: "mfaWrong" });

export const acceptConsent = (version: string) => post("/api/auth/consent", { version }, { 409: "consentStale" });
