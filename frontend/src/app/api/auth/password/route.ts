import { NextResponse, type NextRequest } from "next/server";
import { z } from "zod";
import { body, callApi, fail, isSameOrigin, limit, sessionFrom, signedOut, withSession } from "@/lib/server/bff";
import { gotrue } from "@/lib/server/gotrue";
import { readClaims } from "@/lib/server/tokens";
import { newPassword } from "@/lib/validation/auth";

const schema = z.object({ password: newPassword });

// Serves the forced first-login change and the reset link (which signed the user in at /auth/confirm).
// The API sets the password through Supabase's admin API and clears must_change_password.
export async function POST(req: NextRequest) {
  if (!isSameOrigin(req)) return fail(403);
  const session = await sessionFrom(req);
  if (!session) return signedOut();
  const input = await body(req, (v) => schema.safeParse(v));
  if (!input) return withSession(fail(400), session);
  const limited = await limit(["password", readClaims(session.accessToken)?.sub ?? ""], 5, 600);
  if (limited) return limited;

  const res = await callApi("/api/v1/me/password", session.accessToken, {
    method: "POST",
    body: JSON.stringify({ newPassword: input.password }),
  });
  if (res.status === 401) return signedOut();
  if (res.status === 400) return withSession(fail(400), session);
  if (!res.ok) return withSession(fail(502), session);
  // A new password should lock out anyone still signed in elsewhere with the old one. Best effort: the change already succeeded.
  await gotrue.signOutOthers(session.accessToken);
  return withSession(new NextResponse(null, { status: 204 }), session);
}
