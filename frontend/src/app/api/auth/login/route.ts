import { NextResponse, type NextRequest } from "next/server";
import { body, clientIp, fail, isSameOrigin, limit } from "@/lib/server/bff";
import { gotrue } from "@/lib/server/gotrue";
import { writeSession } from "@/lib/server/tokens";
import { loginSchema } from "@/lib/validation/auth";

export async function POST(req: NextRequest) {
  if (!isSameOrigin(req)) return fail(403);
  const input = await body(req, (v) => loginSchema.safeParse(v));
  if (!input) return fail(400);

  // Per account+IP (guessing one password) and per IP (spraying many accounts).
  const ip = clientIp(req);
  const limited = (await limit(["login", ip, input.email], 5, 60)) ?? (await limit(["login-ip", ip], 30, 600));
  if (limited) return limited;

  const r = await gotrue.signIn(input.email, input.password);
  if (!r.ok) return fail(r.status === 429 ? 429 : r.status >= 500 ? 502 : 401);

  const res = new NextResponse(null, { status: 204 });
  writeSession(res.cookies, r.data);
  return res;
}
