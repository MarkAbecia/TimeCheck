import { NextResponse, type NextRequest } from "next/server";
import { gotrue } from "@/lib/server/gotrue";
import { clearSession, writeSession } from "@/lib/server/tokens";

// Reset and invite emails link here (database/supabase/templates). The one-time token hash becomes an httpOnly
// session; no token sits in the browser's URL bar or JS. Invited users then owe a password, so "/" routes them to it.
const NEXT = { recovery: "/reset-password", invite: "/" } as const;
export async function GET(req: NextRequest) {
  const tokenHash = req.nextUrl.searchParams.get("token_hash");
  const expired = NextResponse.redirect(new URL("/forgot-password?expired=1", req.url), 303);
  const type = req.nextUrl.searchParams.get("type");
  if (!tokenHash || (type !== "recovery" && type !== "invite")) return expired;

  const r = await gotrue.verifyEmailLink(type, tokenHash);
  if (!r.ok) {
    clearSession(expired.cookies);
    return expired;
  }
  const res = NextResponse.redirect(new URL(NEXT[type], req.url), 303);
  writeSession(res.cookies, r.data);
  return res;
}
