# Deploy and roll back

Not yet rehearsed on Render; Phase 12 does the first staging and production deploys and updates this page with what was learned.

## Before any deploy
- CI is green on the commit (`ci-*.yml`, `security.yml`).
- New migrations are backward-compatible with the running app (expand → migrate → contract, §12). A migration that drops or
  renames something waits until the release after the code stopped using it.

## Deploy (staging, then production)
1. `deploy.yml` applies migrations: `supabase migration up` against the target project (session or direct connection).
2. Render deploys `timecheck-api`, `timecheck-worker` and `timecheck-web` from the Blueprint. The API must pass `/health/ready`
   (database and Redis) before it takes traffic.
3. Smoke test (5 minutes):
   - `GET /health/ready` on the API and `/api/health` on the web service return 200.
   - Sign in as a test leader; Today loads; open a report.
   - Worker logs show `deliver-notifications` and `report-exports` running each minute without errors.
4. Production only: deploy on a tagged release, in a quiet hour (not Sunday morning, the peak).

## Roll back
- **App:** in Render, redeploy the previous successful deploy of each service (API, Worker, web). Roll the Worker back with the
  API: they share the Application code.
- **Migrations are not rolled back.** Because they are backward-compatible, the previous app runs on the new schema. If a
  migration itself is wrong, write a new forward migration.
- **Bad data written by a release:** stop the Worker if it is the source, fix with a reviewed SQL script, and restore from
  PITR only as a last resort (see `restore-backup.md`).
- After a rollback: note it in the incident log, and keep `main` red (revert the commit) until the fix lands.
