import "server-only";
import { env } from "./env";

/** A Supabase Auth session as the BFF stores it (in httpOnly cookies, never in the browser's JS). */
export type AuthSession = {
  accessToken: string;
  refreshToken: string;
  expiresIn: number;
};

export type GoTrueResult<T> = { ok: true; data: T } | { ok: false; status: number };

type TokenResponse = {
  access_token: string;
  refresh_token: string;
  expires_in: number;
};

async function call<T>(path: string, { accessToken, ...init }: RequestInit & { accessToken?: string } = {}): Promise<GoTrueResult<T>> {
  const { SUPABASE_URL, SUPABASE_PUBLISHABLE_KEY } = env();
  const headers = new Headers(init.headers);
  headers.set("apikey", SUPABASE_PUBLISHABLE_KEY);
  if (init.body) headers.set("Content-Type", "application/json");
  if (accessToken) headers.set("Authorization", `Bearer ${accessToken}`);
  try {
    const res = await fetch(`${SUPABASE_URL}/auth/v1${path}`, {
      ...init,
      headers,
      cache: "no-store",
      signal: AbortSignal.timeout(10_000),
    });
    if (!res.ok) {
      await res.body?.cancel(); // an unread body keeps the pooled connection busy
      return { ok: false, status: res.status };
    }
    const text = await res.text();
    return { ok: true, data: (text ? JSON.parse(text) : null) as T };
  } catch {
    return { ok: false, status: 503 };
  }
}

const toSession = (r: GoTrueResult<TokenResponse>): GoTrueResult<AuthSession> =>
  r.ok
    ? {
        ok: true,
        data: {
          accessToken: r.data.access_token,
          refreshToken: r.data.refresh_token,
          expiresIn: r.data.expires_in,
        },
      }
    : r;

export const gotrue = {
  signIn: async (email: string, password: string) =>
    toSession(
      await call<TokenResponse>("/token?grant_type=password", {
        method: "POST",
        body: JSON.stringify({ email, password }),
      }),
    ),

  refresh: async (refreshToken: string) =>
    toSession(
      await call<TokenResponse>("/token?grant_type=refresh_token", {
        method: "POST",
        body: JSON.stringify({ refresh_token: refreshToken }),
      }),
    ),

  /** Revokes this session's refresh token (§6.1 "Logout revokes the refresh token"). */
  signOut: (accessToken: string) => call<null>("/logout?scope=local", { method: "POST", accessToken }),

  /** Revokes every other session of this user (other devices); this one stays signed in. */
  signOutOthers: (accessToken: string) => call<null>("/logout?scope=others", { method: "POST", accessToken }),

  /** Sends the reset email. The link lands on /auth/confirm with a one-time token hash. */
  recover: (email: string) =>
    call<null>(`/recover?redirect_to=${encodeURIComponent(`${env().APP_URL}/auth/confirm`)}`, {
      method: "POST",
      body: JSON.stringify({ email }),
    }),

  /** Exchanges a recovery link's token hash for a session. */
  /** Exchanges the one-time hash from a reset or invite email for a session. */
  verifyEmailLink: async (type: "recovery" | "invite", tokenHash: string) =>
    toSession(
      await call<TokenResponse>("/verify", {
        method: "POST",
        body: JSON.stringify({ type, token_hash: tokenHash }),
      }),
    ),

  factors: (accessToken: string) =>
    call<{
      factors?: {
        id: string;
        factor_type: string;
        status: "verified" | "unverified";
      }[];
    }>("/user", { accessToken }),

  enrollTotp: (accessToken: string) =>
    call<{ id: string; totp: { qr_code: string; secret: string } }>("/factors", {
      method: "POST",
      accessToken,
      body: JSON.stringify({
        factor_type: "totp",
        friendly_name: `TimeCheck ${crypto.randomUUID().slice(0, 8)}`,
      }),
    }),

  unenroll: (accessToken: string, factorId: string) => call<null>(`/factors/${factorId}`, { method: "DELETE", accessToken }),

  challenge: (accessToken: string, factorId: string) =>
    call<{ id: string }>(`/factors/${factorId}/challenge`, {
      method: "POST",
      accessToken,
    }),

  /** On success the session is upgraded to aal2; the new tokens replace the old ones. */
  verify: async (accessToken: string, factorId: string, challengeId: string, code: string) =>
    toSession(
      await call<TokenResponse>(`/factors/${factorId}/verify`, {
        method: "POST",
        accessToken,
        body: JSON.stringify({ challenge_id: challengeId, code }),
      }),
    ),
};
