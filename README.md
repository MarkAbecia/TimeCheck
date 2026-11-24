# TimeCheck

Cell group attendance and member management for a church.

| Folder | What |
|---|---|
| `frontend/` | Next.js app (App Router, TypeScript) |
| `backend/` | .NET 10 solution: API, Worker, tests |
| `database/` | Supabase project: SQL migrations, RLS, seed, pgTAP tests |
| `misc/` | Docs, ADRs, scripts, local Docker compose |

## Local dev

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
