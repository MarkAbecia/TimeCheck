import { describe, expect, it } from "vitest";
import { forgotSchema, loginSchema, resetSchema } from "./auth";

const firstError = (r: { success: boolean; error?: { issues: { message: string }[] } }) => r.error?.issues[0]?.message;

describe("auth schemas", () => {
  it("normalises email and requires both login fields", () => {
    expect(loginSchema.parse({ email: "  Leader@TimeCheck.TEST ", password: "x" }).email).toBe("leader@timecheck.test");
    expect(firstError(loginSchema.safeParse({ email: "", password: "x" }))).toBe("emailRequired");
    expect(firstError(loginSchema.safeParse({ email: "nope", password: "x" }))).toBe("emailInvalid");
    expect(firstError(loginSchema.safeParse({ email: "a@b.co", password: "" }))).toBe("passwordRequired");
  });

  it("forgot-password needs a valid email only", () => {
    expect(forgotSchema.safeParse({ email: "a@b.co" }).success).toBe(true);
    expect(firstError(forgotSchema.safeParse({ email: "a@" }))).toBe("emailInvalid");
  });

  it("new passwords match Supabase policy: 12–72 chars, letters and digits", () => {
    const check = (password: string, confirm = password) => firstError(resetSchema.safeParse({ password, confirm }));
    expect(check("short1")).toBe("passwordTooShort");
    expect(check("a1".repeat(37))).toBe("passwordTooLong");
    expect(check("onlylettershere")).toBe("passwordNeedsMix");
    expect(check("123456789012")).toBe("passwordNeedsMix");
    expect(check("blessed-day-2026", "blessed-day-2027")).toBe("passwordsDontMatch");
    expect(check("blessed-day-2026")).toBeUndefined();
  });
});
