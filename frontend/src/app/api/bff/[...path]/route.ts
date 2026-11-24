import { NextResponse, type NextRequest } from "next/server";
import { callApi, fail, isSameOrigin, sessionFrom, signedOut, withSession } from "@/lib/server/bff";

// Browser → BFF → API for app data (PUT is attendance marking). Only these resource roots are reachable; auth flows have their own routes.
const ALLOWED = new Set(["me", "members", "groups", "events", "event-types"]);
const MAX_BODY = 64 * 1024;

async function forward(req: NextRequest, { params }: RouteContext<"/api/bff/[...path]">) {
  const { path } = await params;
  if (!ALLOWED.has(path[0]) || path.some((p) => p === ".." || p === "." || p.includes("\\"))) return fail(404);
  if (req.method !== "GET" && !isSameOrigin(req)) return fail(403);

  const session = await sessionFrom(req);
  if (!session) return signedOut();

  const body = req.method === "GET" ? undefined : await req.text();
  if (body && body.length > MAX_BODY) return withSession(fail(413), session);

  const target = `/api/v1/${path.map(encodeURIComponent).join("/")}${req.nextUrl.search}`;
  let res: Response;
  try {
    res = await callApi(target, session.accessToken, { method: req.method, body: body || undefined });
  } catch {
    return withSession(fail(502), session);
  }
  if (res.status === 401) return signedOut();

  // Pass the API's JSON (including Problem Details with field errors) straight through.
  const out = new NextResponse(res.status === 204 ? null : await res.text(), {
    status: res.status,
    headers: { "Content-Type": res.headers.get("Content-Type") ?? "application/json", "Cache-Control": "no-store" },
  });
  return withSession(out, session);
}

export { forward as GET, forward as POST, forward as PATCH, forward as PUT };
