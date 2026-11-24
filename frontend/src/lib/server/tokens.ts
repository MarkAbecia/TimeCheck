/**
 * Session cookies and JWT claims, shared by proxy.ts (edge of every page request) and the BFF route handlers.
 * The access token is only *read* here for routing (expiry, aal); the API verifies its signature on every call.
 */
export const ACCESS_COOKIE = "tc_at";
export const REFRESH_COOKIE = "tc_rt";
const REFRESH_MAX_AGE = 60 * 60 * 24 * 30; // Supabase refresh tokens rotate on use; this bounds an idle session.

export type Claims = { sub: string; exp: number; aal?: "aal1" | "aal2"; email?: string; amr?: { method: string }[] };

export function readClaims(token: string | undefined): Claims | null {
  if (!token) return null;
  try {
    const payload = token.split(".")[1];
    const json = JSON.parse(Buffer.from(payload, "base64url").toString("utf8")) as Claims;
    return typeof json.sub === "string" && typeof json.exp === "number" ? json : null;
  } catch {
    return null;
  }
}

/** Expired, or close enough that it could expire mid-request. */
export function isStale(claims: Claims | null, now = Date.now()) {
  return !claims || claims.exp * 1000 - now < 30_000;
}

type CookieOptions = {
  httpOnly: true;
  secure: boolean;
  sameSite: "lax";
  path: "/";
  maxAge: number;
};

export function cookieOptions(maxAge: number): CookieOptions {
  // Secure everywhere except plain-http local dev (§6.1: httpOnly, Secure, SameSite=Lax).
  return {
    httpOnly: true,
    secure: process.env.NODE_ENV === "production",
    sameSite: "lax",
    path: "/",
    maxAge,
  };
}

type CookieJar = {
  set(name: string, value: string, options: CookieOptions): unknown;
};

export function writeSession(jar: CookieJar, s: { accessToken: string; refreshToken: string; expiresIn: number }) {
  jar.set(ACCESS_COOKIE, s.accessToken, cookieOptions(s.expiresIn));
  jar.set(REFRESH_COOKIE, s.refreshToken, cookieOptions(REFRESH_MAX_AGE));
}

export function clearSession(jar: CookieJar) {
  jar.set(ACCESS_COOKIE, "", cookieOptions(0));
  jar.set(REFRESH_COOKIE, "", cookieOptions(0));
}
