# TimeCheck

Cell group attendance and member management for a church.

| Folder | What |
|---|---|
| `frontend/` | Next.js app (App Router, TypeScript) |
| `backend/` | .NET 10 solution: API, Worker, tests |
| `database/` | Supabase project: SQL migrations, RLS, seed, pgTAP tests |
| `misc/` | Docs, ADRs, scripts, local Docker compose |

## Local dev

Full guides: [run it locally](misc/docs/guides/local-setup.md) · [deploy and change configuration](misc/docs/guides/deployment.md).

Prereqs: Node 24+, .NET 10 SDK, Docker Desktop.

```bash
cd database && npx supabase start          # Postgres + Auth + Storage on :55421/:55422
docker compose -f misc/docker/docker-compose.yml up -d redis
cd backend && dotnet run --project src/TimeCheck.Api
cd frontend && npm install && npm run dev
```

Frontend: copy `frontend/.env.example` to `frontend/.env.development.local` and fill in the publishable key from `npx supabase status`.
Backend: the API needs the local Supabase **secret** key for the admin API (password changes). Keep it out of the repo:

```bash
cd backend/src/TimeCheck.Api && dotnet user-secrets set Auth:ServiceRoleKey <SECRET_KEY from supabase status>
```

Dev accounts (seed only): `super@`, `admin@`, `leader@`, `member@`, `leader2@`, `member2@timecheck.test`, password `TimeCheck-dev-123!`. Admins enrol TOTP on first sign-in. Reset emails land in Mailpit at http://127.0.0.1:55424.
Requirements: [misc/docs/REQUIREMENTS.md](misc/docs/REQUIREMENTS.md). Progress: [misc/docs/progress.md](misc/docs/progress.md).

## End-to-end tests

With the whole local stack running (Supabase, Redis, API), from `frontend/`:

```bash
npm run e2e                         # flows + axe at the five §13 viewports, light and dark
npx playwright test --project=flows # just the journeys
```

The tests use your installed Chrome (`PW_CHANNEL=` with `npx playwright install chromium` for the bundled one) and start `npm run dev` on :3400 unless it's already running. They read invite emails from Mailpit. Teardown deletes the people they registered (emails `@e2e.timecheck.test`) through `docker exec` into `supabase_db_timecheck`.
After changing an email template in `database/supabase/config.toml`, run `npx supabase stop` then `npx supabase start`. Restarting the containers keeps the old templates.
