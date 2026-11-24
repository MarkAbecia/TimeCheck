import { NextResponse, type NextRequest } from "next/server";
import { body, clientIp, fail, isSameOrigin, limit } from "@/lib/server/bff";
import { gotrue } from "@/lib/server/gotrue";
import { forgotSchema } from "@/lib/validation/auth";

export async function POST(req: NextRequest) {
  if (!isSameOrigin(req)) return fail(403);
  const input = await body(req, (v) => forgotSchema.safeParse(v));
  if (!input) return fail(400);
  const limited = (await limit(["forgot", input.email], 3, 900)) ?? (await limit(["forgot-ip", clientIp(req)], 10, 900));
  if (limited) return limited;

  // The same answer whether or not the email has an account, so the form cannot be used to find members.
  await gotrue.recover(input.email);
  return new NextResponse(null, { status: 204 });
}
