import { NextResponse, type NextRequest } from "next/server";
import { z } from "zod";
import { body, fail, isSameOrigin, limit, sessionFrom, signedOut, withSession } from "@/lib/server/bff";
import { gotrue } from "@/lib/server/gotrue";
import { readClaims, writeSession } from "@/lib/server/tokens";

const schema = z.object({
  factorId: z.string().min(1).max(64),
  code: z.string().regex(/^\d{6}$/),
});

export async function POST(req: NextRequest) {
  if (!isSameOrigin(req)) return fail(403);
  const session = await sessionFrom(req);
  if (!session) return signedOut();
  const input = await body(req, (v) => schema.safeParse(v));
  if (!input) return withSession(fail(400), session);
  const limited = await limit(["mfa", readClaims(session.accessToken)?.sub ?? ""], 5, 300);
  if (limited) return limited;

  const challenge = await gotrue.challenge(session.accessToken, input.factorId);
  if (!challenge.ok) return challenge.status === 401 ? signedOut() : withSession(fail(400), session);
  const verified = await gotrue.verify(session.accessToken, input.factorId, challenge.data.id, input.code);
  if (!verified.ok) return withSession(fail(verified.status === 429 ? 429 : 400), session);

  // The verified session is aal2; it replaces the aal1 tokens.
  const res = new NextResponse(null, { status: 204 });
  writeSession(res.cookies, verified.data);
  return res;
}
