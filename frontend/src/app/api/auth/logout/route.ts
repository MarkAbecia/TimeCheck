import { NextResponse, type NextRequest } from "next/server";
import { fail, isSameOrigin, sessionFrom } from "@/lib/server/bff";
import { gotrue } from "@/lib/server/gotrue";
import { clearSession } from "@/lib/server/tokens";

// A plain form POST from the shell, so it works without JavaScript; 303 sends the browser to /login with a GET.
export async function POST(req: NextRequest) {
  if (!isSameOrigin(req)) return fail(403);
  const session = await sessionFrom(req);
  if (session) await gotrue.signOut(session.accessToken); // revokes the refresh token server-side (§6.1)
  const res = NextResponse.redirect(new URL("/login", req.url), 303);
  clearSession(res.cookies);
  return res;
}
