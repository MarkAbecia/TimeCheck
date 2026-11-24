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

Copy each `.env.example` to `.env` (frontend) or use `dotnet user-secrets` (backend).
Requirements: [misc/docs/REQUIREMENTS.md](misc/docs/REQUIREMENTS.md). Progress: [misc/docs/progress.md](misc/docs/progress.md).
