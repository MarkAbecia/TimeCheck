import { NextResponse, type NextRequest } from "next/server";
import { z } from "zod";
import { body, callApi, fail, isSameOrigin, sessionFrom, signedOut, withSession } from "@/lib/server/bff";

const schema = z.object({ version: z.string().min(1).max(50) });

export async function POST(req: NextRequest) {
  if (!isSameOrigin(req)) return fail(403);
  const session = await sessionFrom(req);
  if (!session) return signedOut();
  const input = await body(req, (v) => schema.safeParse(v));
  if (!input) return withSession(fail(400), session);

  const res = await callApi("/api/v1/me/privacy-consent", session.accessToken, {
    method: "POST",
    body: JSON.stringify(input),
  });
  if (res.status === 401) return signedOut();
  // 409: the notice changed while the page was open, so the user must read the new one.
  if (!res.ok) return withSession(fail(res.status === 400 ? 409 : 502), session);
  return withSession(new NextResponse(null, { status: 204 }), session);
}
