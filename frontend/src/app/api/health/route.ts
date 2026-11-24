// Liveness for Render and the Docker HEALTHCHECK; the only unauthenticated route (CLAUDE.md).
export function GET() {
  return Response.json({ status: "ok" }, { headers: { "Cache-Control": "no-store" } });
}
