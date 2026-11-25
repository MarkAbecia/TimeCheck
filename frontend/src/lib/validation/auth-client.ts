// The sign-in forms' browser checks, without zod or a form library (§9 JS budget: the login page is the first one everyone
// loads). The server routes still validate with the zod schemas in ./auth; these only give the same error keys sooner.
// Keep the two in step.
const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

export const normalizeEmail = (v: unknown) => (typeof v === "string" ? v.trim().toLowerCase() : "");

/** An "errors" message key, or undefined when the email is fine. */
export const emailProblem = (v: unknown) => {
  const email = normalizeEmail(v);
  return !email ? "emailRequired" : EMAIL.test(email) ? undefined : "emailInvalid";
};

/** react-hook-form rules giving the same keys, for forms that already use it (forgot password). */
export const emailRules = { setValueAs: normalizeEmail, validate: (v: string) => emailProblem(v) ?? true } as const;
