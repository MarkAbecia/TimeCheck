import "server-only";
import { z } from "zod";

// Server-only settings. None of these reach the browser (no NEXT_PUBLIC_), and no secret key is needed here:
// the BFF talks to Supabase Auth with the publishable key and to the API with the user's own token (ADR 0002).
const schema = z.object({
  SUPABASE_URL: z.url(),
  SUPABASE_PUBLISHABLE_KEY: z.string().min(1),
  // Render's private network hands over a bare host:port.
  API_URL: z.preprocess((v) => (typeof v === "string" && !v.includes("://") ? `http://${v}` : v), z.url()),
  APP_URL: z.url(),
  REDIS_URL: z.string().optional(),
  CHURCH_NAME: z.string().default("TimeCheck"),
});

let cached: z.infer<typeof schema> | undefined;

export function env() {
  cached ??= schema.parse(process.env);
  return cached;
}
