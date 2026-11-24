import type { ForgotInput, LoginInput } from "./validation/auth";

export type AuthError = "invalidCredentials" | "rateLimited" | "offline" | "unexpected" | "resetExpired";
export type AuthResult = { ok: true } | { ok: false; error: AuthError };

// BFF route handlers (/api/auth/*) set the httpOnly session cookie; tokens never reach client JS (§6.1). They land in Phase 4.
async function post(path: string, body: unknown, onClientError: AuthError): Promise<AuthResult> {
  if (typeof navigator !== "undefined" && !navigator.onLine) return { ok: false, error: "offline" };
  try {
    const res = await fetch(path, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
      credentials: "same-origin",
    });
    if (res.ok) return { ok: true };
    if (res.status === 429) return { ok: false, error: "rateLimited" };
    if (res.status === 400 || res.status === 401 || res.status === 410) return { ok: false, error: onClientError };
    return { ok: false, error: "unexpected" };
  } catch {
    return { ok: false, error: "unexpected" };
  }
}

export const login = (input: LoginInput) => post("/api/auth/login", input, "invalidCredentials");
// Always resolves ok on 2xx/4xx-not-429 from the server side, so the UI never reveals whether an email exists.
export const requestReset = (input: ForgotInput) => post("/api/auth/forgot-password", input, "unexpected");
export const resetPassword = (password: string) => post("/api/auth/reset-password", { password }, "resetExpired");
