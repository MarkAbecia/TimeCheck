// Liveness for Render and the Docker HEALTHCHECK; the only unauthenticated route (CLAUDE.md). `version` is the deployed commit
// (Render sets RENDER_GIT_COMMIT), so the deploy workflow can wait for the new build before running the smoke tests.
export function GET() {
  return Response.json({ status: "ok", version: process.env.RENDER_GIT_COMMIT ?? null }, { headers: { "Cache-Control": "no-store" } });
}
