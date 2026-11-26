# Deploy and roll back

Not yet rehearsed on Render. The Blueprint (`render.yaml`), the deploy workflow and the smoke tests exist; the first staging
deploy will update this page with what was learned.

## First-time setup (once per environment, staging first)

1. **Supabase:** create a project for the environment (region Singapore). Note its URL, publishable key, service role key, the
   pooler connection strings (transaction mode for the API, session mode for Hangfire) and the direct database URL.
2. **Secrets group in Render:** create `timecheck-secrets-staging` (or `-production`) by hand, before syncing the Blueprint,
   with:
   - `Auth__SupabaseUrl`, `Auth__ServiceRoleKey`
   - `ConnectionStrings__Default` (transaction pooler), `ConnectionStrings__Hangfire` (session pooler or direct)
   - `Encryption__ActiveKeyId` = `1`, `Encryption__Keys__1` and `Encryption__BlindIndexKey`: 32 random bytes each, base64.
     New values per environment; keep them in the password manager too, since losing them loses the encrypted data.
   - `Notifications__Push__PublicKey`, `Notifications__Push__PrivateKey` (a new VAPID pair), `Notifications__Push__Subject`
     (`mailto:` an address someone reads)
3. **Blueprint:** in Render, New → Blueprint → this repository. Render prompts once for the `sync: false` values:
   - web: `APP_URL` (the web service's public URL), `SUPABASE_URL`, `SUPABASE_PUBLISHABLE_KEY`
   - API: `Cors__AllowedOrigins__0` and `Notifications__AppUrl` (both the web URL)
   - Worker: `Notifications__AppUrl`
   Validate first with `render blueprints validate render.yaml` (Render CLI 2.7+).
4. **Migrations:** apply them once by hand or let the first `deploy.yml` run do it. Then the `timecheck_api` login role and
   the church (`database-roles.md`).
5. **First Super Admin:** after the first API deploy, run `create-super-admin` from the API service's Shell
   (`database-roles.md`), and accept the invite.
6. **Smoke accounts:** register a leader and an admin in the new environment (not real members). Sign in as the admin, enrol an
   authenticator by hand-entering the key the MFA page shows, and save that key.
7. **GitHub environment** (`staging` / `production`; production needs a required reviewer):
   - secrets: `SUPABASE_DB_URL`, `RENDER_DEPLOY_HOOK_WEB`, `RENDER_DEPLOY_HOOK_API`, `RENDER_DEPLOY_HOOK_WORKER`,
     `SMOKE_LEADER_EMAIL`, `SMOKE_LEADER_PASSWORD`, `SMOKE_ADMIN_EMAIL`, `SMOKE_ADMIN_PASSWORD`, `SMOKE_ADMIN_TOTP_SECRET`
   - variable: `SMOKE_BASE_URL` (the web URL)
8. **Domain:** add the custom domain to the web service, then update `APP_URL`, `Cors__AllowedOrigins__0` and
   `Notifications__AppUrl` to it, and the Supabase Auth site URL and redirect list.

## Before any deploy
- CI is green on the commit (`ci-*.yml`, `security.yml`).
- New migrations are backward-compatible with the running app (expand → migrate → contract, §12). A migration that drops or
  renames something waits until the release after the code stopped using it.

## Deploy (staging, then production)
`deploy.yml` does it: staging on every push to `main`, production on a `v*` tag after approval. Production deploys go out in a
quiet hour, not on Sunday morning (the peak).
1. `migrate`: `supabase db push` against the environment's database.
2. `deploy`: calls the three Render deploy hooks (the services have auto-deploy off so they never run ahead of migrations).
   The API takes traffic only after `/health/ready` (database and Redis) passes.
3. `smoke`: waits until the web health check reports the new commit, then runs `npm run smoke`: security headers, signed-out
   redirect and 401, a leader signs in and opens a report, the admin signs in with a TOTP code and opens Members. Read-only
   apart from signing in. Traces are uploaded when it fails.
4. By hand, 2 minutes: the Worker logs show `deliver-notifications` and `report-exports` running each minute without errors.

Run the smoke tests against any environment from a laptop:
`SMOKE_BASE_URL=https://… SMOKE_LEADER_EMAIL=… SMOKE_LEADER_PASSWORD=… npm run smoke` (in `frontend/`; the admin test is
skipped without its three variables).

## Roll back
- **App:** in Render, redeploy the previous successful deploy of each service (API, Worker, web). Roll the Worker back with the
  API: they share the Application code.
- **Migrations are not rolled back.** Because they are backward-compatible, the previous app runs on the new schema. If a
  migration itself is wrong, write a new forward migration.
- **Bad data written by a release:** stop the Worker if it is the source, fix with a reviewed SQL script, and restore from
  PITR only as a last resort (see `restore-backup.md`).
- After a rollback: note it in the incident log, and keep `main` red (revert the commit) until the fix lands.
