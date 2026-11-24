import { NextResponse, type NextRequest } from "next/server";
import { gotrue, type AuthSession } from "@/lib/server/gotrue";
import { ACCESS_COOKIE, REFRESH_COOKIE, clearSession, isStale, readClaims, writeSession } from "@/lib/server/tokens";

const SONNER_CSS = "'sha256-StEaX+se6YS7pqjzrzMIA0KaX9zF/8zAhvQXZAe5epY='";

/** Pages reachable without a session. Everything else, (app) routes included, redirects to /login (§6.1 AC). */
const PUBLIC_PATHS = ["/login", "/forgot-password", "/auth/confirm"];
const isPublic = (path: string) => PUBLIC_PATHS.some((p) => path === p || path.startsWith(`${p}/`));

/**
 * Runs before every page: the auth gate with silent refresh (§6.1 "Expired sessions refresh silently"), then a
 * per-request CSP nonce and security headers (§8.3). Signature checks and onboarding live behind it: the API
 * verifies every token, and the (app) layout routes users who still owe a password change, MFA or consent.
 */
export async function proxy(request: NextRequest) {
  const path = request.nextUrl.pathname;

  let access = request.cookies.get(ACCESS_COOKIE)?.value;
  const refreshToken = request.cookies.get(REFRESH_COOKIE)?.value;
  let refreshed: AuthSession | undefined;
  if (isStale(readClaims(access)) && refreshToken) {
    const r = await gotrue.refresh(refreshToken);
    if (r.ok) {
      refreshed = r.data;
      access = r.data.accessToken;
      // Server components in this same request must see the new token, not the expired one.
      request.cookies.set(ACCESS_COOKIE, r.data.accessToken);
      request.cookies.set(REFRESH_COOKIE, r.data.refreshToken);
    }
  }
  const signedIn = !isStale(readClaims(access));

  if (!signedIn && !isPublic(path)) {
    const login = new URL("/login", request.url);
    if (path !== "/") login.searchParams.set("next", `${path}${request.nextUrl.search}`);
    const res = NextResponse.redirect(login);
    if (refreshToken) clearSession(res.cookies); // a refresh token that no longer works is dead weight
    return res;
  }
  if (signedIn && path === "/login") {
    const res = NextResponse.redirect(new URL("/", request.url));
    if (refreshed) writeSession(res.cookies, refreshed);
    return res;
  }

  const response = withSecurityHeaders(request);
  if (refreshed) writeSession(response.cookies, refreshed);
  return response;
}

function withSecurityHeaders(request: NextRequest) {
  const nonce = Buffer.from(crypto.randomUUID()).toString("base64");
  const isDev = process.env.NODE_ENV === "development";

  const csp = [
    "default-src 'self'",
    `script-src 'self' 'nonce-${nonce}' 'strict-dynamic'${isDev ? " 'unsafe-eval'" : ""}`,
    // Hashes: sonner's injected stylesheet (src/lib/csp.test.ts fails when an upgrade changes it) and the empty <style>.
    `style-src 'self' 'nonce-${nonce}' ${SONNER_CSS} 'sha256-47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU='`,
    // React renders style="" attributes (Base UI positioning, sonner); attributes can't carry a nonce. See ADR 0005.
    "style-src-attr 'unsafe-inline'",
    "img-src 'self' blob: data:",
    "font-src 'self'",
    `connect-src 'self'${isDev ? " ws:" : ""}`,
    "object-src 'none'",
    "base-uri 'self'",
    "form-action 'self'",
    "frame-ancestors 'none'",
    ...(isDev ? [] : ["upgrade-insecure-requests"]),
  ].join("; ");

  const requestHeaders = new Headers(request.headers);
  requestHeaders.set("x-nonce", nonce);
  requestHeaders.set("Content-Security-Policy", csp);

  const response = NextResponse.next({ request: { headers: requestHeaders } });
  const h = response.headers;
  h.set("Content-Security-Policy", csp);
  h.set("X-Content-Type-Options", "nosniff");
  h.set("Referrer-Policy", "strict-origin-when-cross-origin");
  h.set("X-Frame-Options", "DENY");
  h.set("Permissions-Policy", "camera=(self), microphone=(), geolocation=(), payment=(), usb=()");
  h.set("Cross-Origin-Opener-Policy", "same-origin");
  // Signed-in pages carry personal data: never let a shared cache or the back-forward cache keep them.
  h.set("Cache-Control", "private, no-store");
  if (!isDev) h.set("Strict-Transport-Security", "max-age=63072000; includeSubDomains; preload");
  return response;
}

export const config = {
  matcher: [
    {
      source: "/((?!api|_next/static|_next/image|favicon.ico|icon|apple-icon|manifest.webmanifest).*)",
      missing: [
        { type: "header", key: "next-router-prefetch" },
        { type: "header", key: "purpose", value: "prefetch" },
      ],
    },
  ],
};
