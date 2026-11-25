import { NextResponse, type NextRequest } from "next/server";
import { callApi, fail, isSameOrigin, sessionFrom, signedOut, withSession } from "@/lib/server/bff";

// Browser → BFF → API for app data (PUT is attendance marking and devotion upload). Only these resource roots are reachable;
// auth flows have their own routes.
const ALLOWED = new Set(["me", "members", "groups", "events", "event-types", "devotions", "invites", "reports", "admin"]); // the API authorizes each route
const MAX_BODY = 64 * 1024;
// Devotion photos (§6.3): up to 10 MB plus the form fields. The only route that takes a file.
const MAX_UPLOAD = 10 * 1024 * 1024 + 64 * 1024;
const isUpload = (method: string, path: string[]) => method === "PUT" && path[0] === "me" && path[1] === "devotions" && path.length === 4;

async function forward(req: NextRequest, { params }: RouteContext<"/api/bff/[...path]">) {
  const { path } = await params;
  if (!ALLOWED.has(path[0]) || path.some((p) => p === ".." || p === "." || p.includes("\\"))) return fail(404);
  if (req.method !== "GET" && !isSameOrigin(req)) return fail(403);

  const session = await sessionFrom(req);
  if (!session) return signedOut();

  const upload = isUpload(req.method, path);
  const type = req.headers.get("Content-Type") ?? "";
  // Files go to the upload route only, and the upload route takes nothing else.
  if (req.method !== "GET" && upload !== type.startsWith("multipart/form-data")) return withSession(fail(415), session);
  const limit = upload ? MAX_UPLOAD : MAX_BODY;
  if (Number(req.headers.get("Content-Length") ?? 0) > limit) return withSession(fail(413), session);
  const body = req.method === "GET" ? undefined : upload ? await req.arrayBuffer() : await req.text();
  const size = typeof body === "string" ? body.length : (body?.byteLength ?? 0);
  if (size > limit) return withSession(fail(413), session);

  const target = `/api/v1/${path.map(encodeURIComponent).join("/")}${req.nextUrl.search}`;
  let res: Response;
  try {
    res = await callApi(target, session.accessToken, {
      method: req.method,
      body: size > 0 ? body : undefined,
      // Multipart keeps its boundary; everything else is JSON.
      headers: upload ? { "Content-Type": type } : undefined,
      signal: AbortSignal.timeout(upload ? 30_000 : 10_000),
    });
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

export { forward as GET, forward as POST, forward as PATCH, forward as PUT, forward as DELETE };
