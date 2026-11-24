import { NextResponse, type NextRequest } from "next/server";
import { z } from "zod";
import { body, callApi, fail, isSameOrigin, limit, sessionFrom, signedOut, withSession } from "@/lib/server/bff";
import { gotrue } from "@/lib/server/gotrue";
import { readClaims } from "@/lib/server/tokens";
import { newPassword } from "@/lib/validation/auth";

const schema = z.object({ password: newPassword, currentPassword: z.string().max(200).optional() });

// Serves the forced first-login change, the reset/invite link (which signed the user in at /auth/confirm), and the
// profile change. The API sets the password through Supabase's admin API and clears must_change_password.
export async function POST(req: NextRequest) {
  if (!isSameOrigin(req)) return fail(403);
  const session = await sessionFrom(req);
  if (!session) return signedOut();
  const input = await body(req, (v) => schema.safeParse(v));
  if (!input) return withSession(fail(400), session);
  const claims = readClaims(session.accessToken);
  const limited = await limit(["password", claims?.sub ?? ""], 5, 600);
  if (limited) return limited;

  // A password-signed-in session that owes no change is a voluntary change: a stolen session alone must not be enough.
  // Email-link sessions (reset, invite) prove themselves by the link; a forced change follows a fresh password sign-in.
  const me = await callApi("/api/v1/me", session.accessToken);
  if (me.status === 401) return signedOut();
  if (!me.ok) return withSession(fail(502), session);
  const { mustChangePassword, email } = (await me.json()) as { mustChangePassword: boolean; email: string };
  const viaPassword = claims?.amr?.some((a) => a.method === "password") ?? false;
  if (viaPassword && !mustChangePassword) {
    if (!input.currentPassword) return withSession(fail(403), session);
    const check = await gotrue.signIn(email, input.currentPassword);
    if (!check.ok) return withSession(fail(check.status === 429 ? 429 : 403), session);
  }

  const res = await callApi("/api/v1/me/password", session.accessToken, {
    method: "POST",
    body: JSON.stringify({ newPassword: input.password }),
  });
  if (res.status === 401) return signedOut();
  if (res.status === 400) return withSession(fail(400), session);
  if (!res.ok) return withSession(fail(502), session);
  // A new password should lock out anyone still signed in elsewhere with the old one (and the check's own session).
  await gotrue.signOutOthers(session.accessToken);
  return withSession(new NextResponse(null, { status: 204 }), session);
}
