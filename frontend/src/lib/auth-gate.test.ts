// @vitest-environment node
import { describe, expect, it } from "vitest";
import { NextRequest } from "next/server";
import { proxy } from "@/proxy";
import { nextOnboardingStep, type Me } from "./onboarding";
import { isStale, readClaims } from "./server/tokens";

const jwt = (claims: object) => `x.${Buffer.from(JSON.stringify(claims)).toString("base64url")}.sig`;
const fresh = () => jwt({ sub: "u1", exp: Math.floor(Date.now() / 1000) + 3600, aal: "aal1" });
const req = (path: string, cookie?: string) =>
  new NextRequest(`http://localhost${path}`, {
    headers: cookie ? { cookie } : {},
  });

describe("auth gate (proxy)", () => {
  it("sends signed-out visitors to /login and keeps where they were going", async () => {
    const res = await proxy(req("/attendance?week=2"));
    expect(res.status).toBe(307);
    expect(res.headers.get("location")).toBe("http://localhost/login?next=%2Fattendance%3Fweek%3D2");
  });

  it("treats an expired token with no refresh token as signed out", async () => {
    const res = await proxy(req("/", `tc_at=${jwt({ sub: "u1", exp: 1 })}`));
    expect(res.headers.get("location")).toBe("http://localhost/login");
  });

  it("lets public pages through and bounces signed-in users off /login", async () => {
    expect((await proxy(req("/forgot-password"))).headers.get("location")).toBeNull();
    const res = await proxy(req("/login", `tc_at=${fresh()}`));
    expect(res.headers.get("location")).toBe("http://localhost/");
  });

  it("lets a signed-in user through with no-store", async () => {
    const res = await proxy(req("/", `tc_at=${fresh()}`));
    expect(res.headers.get("location")).toBeNull();
    expect(res.headers.get("cache-control")).toBe("private, no-store");
  });
});

describe("tokens", () => {
  it("reads claims and rejects junk", () => {
    expect(readClaims(fresh())?.sub).toBe("u1");
    expect(readClaims("not-a-jwt")).toBeNull();
    expect(readClaims(jwt({ exp: 1 }))).toBeNull();
  });

  it("counts a token within 30 seconds of expiry as stale", () => {
    const now = Date.now();
    expect(isStale({ sub: "u", exp: Math.floor(now / 1000) + 20 }, now)).toBe(true);
    expect(isStale({ sub: "u", exp: Math.floor(now / 1000) + 120 }, now)).toBe(false);
    expect(isStale(null)).toBe(true);
  });
});

describe("nextOnboardingStep", () => {
  const me = (over: Partial<Me> = {}): Me => ({
    id: "1",
    firstName: "Ana",
    lastName: "Cruz",
    nickname: null,
    displayName: "Ana Cruz",
    email: "a@x.test",
    role: "MEMBER",
    groupId: null,
    ledGroupIds: [],
    mustChangePassword: false,
    privacyConsentVersion: "2026-11",
    requiredPrivacyConsentVersion: "2026-11",
    mfaRequired: false,
    churchName: "C",
    groupName: null,
    ...over,
  });

  it("asks for the password first, then MFA, then consent", () => {
    const all = {
      mustChangePassword: true,
      mfaRequired: true,
      privacyConsentVersion: null,
    };
    expect(nextOnboardingStep(me(all), "aal1")).toBe("/change-password");
    expect(nextOnboardingStep(me({ ...all, mustChangePassword: false }), "aal1")).toBe("/mfa");
    expect(nextOnboardingStep(me({ ...all, mustChangePassword: false }), "aal2")).toBe("/consent");
  });

  it("asks again when the notice version changes, and is done otherwise", () => {
    expect(nextOnboardingStep(me({ privacyConsentVersion: "2025-01" }), "aal1")).toBe("/consent");
    expect(nextOnboardingStep(me(), "aal1")).toBeNull();
    expect(nextOnboardingStep(me({ mfaRequired: true }), "aal2")).toBeNull();
  });
});
