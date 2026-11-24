import { z } from "zod";

// Messages are keys into messages/*.json → "errors"; the form translates them so schemas stay locale-free.
// The password rule mirrors Supabase auth (database/supabase/config.toml): 12+ chars, letters and digits.
const email = z.string().trim().min(1, "emailRequired").pipe(z.email("emailInvalid")).transform((v) => v.toLowerCase());

export const newPassword = z
  .string()
  .min(12, "passwordTooShort")
  .max(72, "passwordTooLong") // bcrypt truncates past 72 bytes
  .refine((v) => /\p{L}/u.test(v) && /\d/.test(v), "passwordNeedsMix");

export const loginSchema = z.object({
  email,
  password: z.string().min(1, "passwordRequired"),
});

export const forgotSchema = z.object({ email });

export const resetSchema = z
  .object({ password: newPassword, confirm: z.string().min(1, "passwordRequired") })
  .refine((v) => v.password === v.confirm, { message: "passwordsDontMatch", path: ["confirm"] });

export type LoginInput = z.infer<typeof loginSchema>;
export type ForgotInput = z.infer<typeof forgotSchema>;
export type ResetInput = z.infer<typeof resetSchema>;
