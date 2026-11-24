import "server-only";
import { createHash } from "node:crypto";
import { NextResponse, type NextRequest } from "next/server";
import { env } from "./env";
import { gotrue, type AuthSession } from "./gotrue";
import { hit } from "./rate-limit";
import { ACCESS_COOKIE, REFRESH_COOKIE, clearSession, isStale, readClaims, writeSession } from "./tokens";

/** Problem-ish JSON errors the client maps to friendly copy; no Supabase detail leaks through. */
export const fail = (status: number, init?: ResponseInit) => NextResponse.json({ status }, { status, ...init });

/**
 * State-changing BFF routes only accept same-origin requests. SameSite=Lax already keeps the session cookie off
 * cross-site POSTs; this also stops login CSRF, which needs no cookie.
 */
export function isSameOrigin(req: NextRequest) {
  const origin = req.headers.get("origin");
  return origin !== null && origin === new URL(env().APP_URL).origin;
}

export function clientIp(req: NextRequest) {
  return req.headers.get("x-forwarded-for")?.split(",")[0]?.trim() || "unknown";
}

/** Rate-limit key parts are hashed so emails and IPs never sit in Redis in clear. */
export async function limit(parts: string[], max: number, windowSeconds: number) {
  const key = createHash("sha256").update(parts.join("|")).digest("hex").slice(0, 32);
  const r = await hit(key, max, windowSeconds);
  return r.allowed ? null : fail(429, { headers: { "Retry-After": String(r.retryAfter) } });
}

/**
 * The caller's access token, refreshed when stale. Returns the session to write back (when refreshed) so the
 * handler can set it on its response. Null means signed out.
 */
export async function sessionFrom(req: NextRequest): Promise<{ accessToken: string; refreshed?: AuthSession } | null> {
  const access = req.cookies.get(ACCESS_COOKIE)?.value;
  if (access && !isStale(readClaims(access))) return { accessToken: access };
  const refreshToken = req.cookies.get(REFRESH_COOKIE)?.value;
  if (!refreshToken) return null;
  const r = await gotrue.refresh(refreshToken);
  return r.ok ? { accessToken: r.data.accessToken, refreshed: r.data } : null;
}

export function withSession(res: NextResponse, s: { refreshed?: AuthSession } | null) {
  if (s?.refreshed) writeSession(res.cookies, s.refreshed);
  return res;
}

export function signedOut(status = 401) {
  const res = fail(status);
  clearSession(res.cookies);
  return res;
}

/** Calls the .NET API with the user's token (server to server; the browser never holds it). */
export function callApi(path: string, accessToken: string, init: RequestInit = {}) {
  const headers = new Headers(init.headers);
  headers.set("Authorization", `Bearer ${accessToken}`);
  if (init.body) headers.set("Content-Type", "application/json");
  return fetch(`${env().API_URL}${path}`, {
    ...init,
    headers,
    cache: "no-store",
    signal: init.signal ?? AbortSignal.timeout(10_000),
  });
}

/** Parse a JSON body against a schema; null on anything malformed. */
export async function body<T>(req: NextRequest, parse: (v: unknown) => { success: true; data: T } | { success: false }) {
  try {
    const r = parse(await req.json());
    return r.success ? r.data : null;
  } catch {
    return null;
  }
}
