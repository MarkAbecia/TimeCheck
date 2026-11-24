import { NextResponse, type NextRequest } from "next/server";
import { gotrue } from "@/lib/server/gotrue";
import { clearSession, writeSession } from "@/lib/server/tokens";

// The reset email links here (database/supabase/templates/recovery.html). The one-time token hash becomes an
// httpOnly session, then the user chooses a new password; no token sits in the browser's URL bar or JS.
export async function GET(req: NextRequest) {
  const tokenHash = req.nextUrl.searchParams.get("token_hash");
  const expired = NextResponse.redirect(new URL("/forgot-password?expired=1", req.url), 303);
  if (!tokenHash || req.nextUrl.searchParams.get("type") !== "recovery") return expired;

  const r = await gotrue.verifyRecovery(tokenHash);
  if (!r.ok) {
    clearSession(expired.cookies);
    return expired;
  }
  const res = NextResponse.redirect(new URL("/reset-password", req.url), 303);
  writeSession(res.cookies, r.data);
  return res;
}
