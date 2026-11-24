import { describe, expect, it } from "vitest";
import { ageOn, editSchema, registerSchema, toMemberBody } from "./member";

const adult = {
  firstName: "Ana",
  lastName: "Cruz",
  nickname: "",
  gender: "FEMALE",
  birthdate: "1990-05-01",
  gradeLevel: "",
  mobile: "0917 123 4567",
  email: "ana@example.com",
  address: "",
  joinedOn: "",
  guardianName: "",
  guardianContact: "",
  guardianConsent: false,
};
const kidBirthdate = `${new Date().getFullYear() - 12}-01-01`;
const issues = (r: { success: boolean; error?: { issues: { path: PropertyKey[]; message: string }[] } }) =>
  Object.fromEntries((r.error?.issues ?? []).map((i) => [i.path.join("."), i.message]));

describe("member schema", () => {
  it("counts a birthday not yet reached this year", () => {
    expect(ageOn("2008-12-25", new Date(2026, 11, 24))).toBe(17);
    expect(ageOn("2008-12-24", new Date(2026, 11, 24))).toBe(18);
  });

  it("accepts an adult without a guardian and sends no guardian", () => {
    const r = registerSchema.safeParse(adult);
    expect(r.success).toBe(true);
    expect(toMemberBody(r.data!)).toMatchObject({ guardian: null, nickname: null, joinedOn: null, email: "ana@example.com" });
  });

  it("requires a guardian who agreed for a minor", () => {
    const r = registerSchema.safeParse({ ...adult, birthdate: kidBirthdate });
    expect(issues(r)).toEqual({ guardianName: "required", guardianContact: "required", guardianConsent: "guardianConsentRequired" });
    const ok = registerSchema.safeParse({
      ...adult,
      birthdate: kidBirthdate,
      guardianName: "Maria",
      guardianContact: "0918",
      guardianConsent: true,
    });
    expect(toMemberBody(ok.data!).guardian).toEqual({ name: "Maria", contact: "0918", consent: true });
  });

  it("rejects a future birthdate, a bad mobile and a missing gender", () => {
    const r = registerSchema.safeParse({ ...adult, birthdate: "2999-01-01", mobile: "call me", gender: "" });
    expect(issues(r)).toMatchObject({ birthdate: "birthdateInvalid", mobile: "mobileInvalid", gender: "required" });
  });

  it("edit needs a join date but no email", () => {
    const rest: Omit<typeof adult, "email"> & { email?: string } = { ...adult };
    delete rest.email;
    expect(issues(editSchema.safeParse(rest))).toEqual({ joinedOn: "dateRequired" });
    expect("email" in toMemberBody(editSchema.parse({ ...rest, joinedOn: "2024-01-07" }))).toBe(false);
  });
});
