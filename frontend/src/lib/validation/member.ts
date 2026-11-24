import { z } from "zod";

// Mirrors the API's rules (backend Features/Members). Messages are keys into messages → "errors".
const MOBILE = /^\+?[0-9 ()-]{7,20}$/;
const DATE = /^\d{4}-\d{2}-\d{2}$/;

/** Whole years between a yyyy-mm-dd birthdate and today (local). */
export function ageOn(birthdate: string, today = new Date()) {
  const [y, m, d] = birthdate.split("-").map(Number);
  let age = today.getFullYear() - y;
  if (today.getMonth() + 1 < m || (today.getMonth() + 1 === m && today.getDate() < d)) age--;
  return age;
}

export const isMinor = (birthdate: string) => DATE.test(birthdate) && ageOn(birthdate) < 18;

const todayIso = () => new Date().toLocaleDateString("en-CA"); // yyyy-mm-dd in local time
const optional = (max: number) => z.string().trim().max(max, "tooLong");

const base = {
  firstName: z.string().trim().min(1, "required").max(100, "tooLong"),
  lastName: z.string().trim().min(1, "required").max(100, "tooLong"),
  nickname: optional(100),
  gender: z.enum(["MALE", "FEMALE"], "required"),
  birthdate: z
    .string()
    .regex(DATE, "dateRequired")
    .refine((v) => v <= todayIso() && ageOn(v) <= 120, "birthdateInvalid"),
  gradeLevel: optional(50),
  mobile: z
    .string()
    .trim()
    .refine((v) => v === "" || MOBILE.test(v), "mobileInvalid"),
  address: optional(500),
  guardianName: optional(200),
  guardianContact: optional(100),
  guardianConsent: z.boolean(),
};

/** A minor needs a guardian who agreed (§6.4, §8.4). */
function guardianRule(
  v: { birthdate: string; guardianName: string; guardianContact: string; guardianConsent: boolean },
  ctx: z.RefinementCtx,
) {
  if (!isMinor(v.birthdate)) return;
  if (!v.guardianName) ctx.addIssue({ code: "custom", path: ["guardianName"], message: "required" });
  if (!v.guardianContact) ctx.addIssue({ code: "custom", path: ["guardianContact"], message: "required" });
  if (!v.guardianConsent) ctx.addIssue({ code: "custom", path: ["guardianConsent"], message: "guardianConsentRequired" });
}

export const registerSchema = z
  .object({
    ...base,
    mobile: base.mobile.refine((v) => v !== "", "required"),
    email: z.string().trim().min(1, "emailRequired").pipe(z.email("emailInvalid")),
    joinedOn: z.string().refine((v) => v === "" || (DATE.test(v) && v <= todayIso()), "joinedInvalid"),
  })
  .superRefine(guardianRule);

export const editSchema = z
  .object({
    ...base,
    joinedOn: z
      .string()
      .regex(DATE, "dateRequired")
      .refine((v) => v <= todayIso(), "joinedInvalid"),
  })
  .superRefine(guardianRule);

export type RegisterInput = z.infer<typeof registerSchema>;
export type EditInput = z.infer<typeof editSchema>;

const blank = (s: string) => (s.trim() === "" ? null : s.trim());

/** Form values → API body: blanks become null, the guardian only travels for minors. */
export function toMemberBody(v: RegisterInput | EditInput) {
  return {
    firstName: v.firstName,
    lastName: v.lastName,
    nickname: blank(v.nickname),
    gender: v.gender,
    birthdate: v.birthdate,
    gradeLevel: blank(v.gradeLevel),
    mobile: blank(v.mobile),
    address: blank(v.address),
    joinedOn: blank(v.joinedOn),
    guardian: isMinor(v.birthdate) ? { name: v.guardianName, contact: v.guardianContact, consent: v.guardianConsent } : null,
    ...("email" in v ? { email: v.email } : {}),
  };
}

export const profileSchema = z.object({ nickname: base.nickname, mobile: base.mobile, address: base.address });
export type ProfileInput = z.infer<typeof profileSchema>;

export const groupSchema = z.object({ name: z.string().trim().min(1, "required").max(100, "tooLong"), leaderId: z.string() });
export type GroupInput = z.infer<typeof groupSchema>;
