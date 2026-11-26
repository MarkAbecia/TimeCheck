# Run TimeCheck on your machine

Everything runs locally: Supabase (Postgres, Auth, Storage, Mailpit) and Redis in Docker, the API and Worker with
`dotnet run`, and the web app with `npm run dev`. Commands are PowerShell, run from the repo root unless a step says otherwise.

## 1. Install the tools (once)

| Tool | Version | Check |
|---|---|---|
| Git | any recent | `git --version` |
| Node.js | 24 or newer | `node --version` |
| .NET SDK | 10 | `dotnet --version` |
| Docker Desktop | running, WSL 2 backend | `docker info --format "{{.ServerVersion}}"` |

The Supabase CLI isn't installed globally; every step runs it with `npx supabase` from `database/`.

## 2. Get the code and install packages (once)

```powershell
git clone https://github.com/MarkAbecia/TimeCheck.git
cd TimeCheck
npm --prefix frontend install
dotnet restore backend
```

## 3. Start Supabase and Redis (every session)

```powershell
cd database; npx supabase start; cd ..
```

The first start downloads the images (several minutes). Supabase applies every file in `database/supabase/migrations` and then
`seed.sql`, which creates the church, the dev accounts and the `timecheck_api` login role the API uses.

| Service | Address |
|---|---|
| Supabase API (Auth, Storage) | http://127.0.0.1:55421 |
| Postgres | 127.0.0.1:55422, user `postgres`, password `postgres` |
| Studio (database browser) | http://127.0.0.1:55423 |
| Mailpit (every email lands here) | http://127.0.0.1:55424 |

Then Redis:

```powershell
docker compose -f misc/docker/docker-compose.yml up -d redis
```

Print the keys you need in the next two steps:

```powershell
cd database; npx supabase status; cd ..
```

## 4. Backend secrets (once)

The API and Worker share one user-secrets store (`timecheck-api-dev`), outside the repo. Everything else they need for local
development is already in `appsettings.Development.json`.

```powershell
cd backend/src/TimeCheck.Api
dotnet user-secrets set Auth:ServiceRoleKey "<Secret key from supabase status>"
```

Web push (optional; without it push is just off). Generate a VAPID pair, then store both halves:

```powershell
npx web-push generate-vapid-keys
dotnet user-secrets set Notifications:Push:PublicKey "<Public Key>"
dotnet user-secrets set Notifications:Push:PrivateKey "<Private Key>"
cd ../../..
```

Never put these values in `appsettings*.json` or commit them.

## 5. Frontend settings (once)

```powershell
Copy-Item frontend/.env.example frontend/.env.development.local
```

Open `frontend/.env.development.local` and set `SUPABASE_PUBLISHABLE_KEY` to the **Publishable** key from `supabase status`
(it starts `sb_publishable_`, never the secret key). Keep the other lines as they are:

```
SUPABASE_URL=http://127.0.0.1:55421
API_URL=http://localhost:5170
APP_URL=http://localhost:3400
REDIS_URL=redis://localhost:6379
CHURCH_NAME=TimeCheck
```

## 6. Run the three apps (every session, each in its own terminal)

```powershell
dotnet run --project backend/src/TimeCheck.Api
```
```powershell
dotnet run --project backend/src/TimeCheck.Worker
```
```powershell
npm --prefix frontend run dev -- --port 3400
```

- Web: http://localhost:3400. Use port 3400: Supabase's redirect list and the API's CORS list expect it.
- API: http://localhost:5170. `/health/ready` should say Healthy, and the API reference is at http://localhost:5170/scalar.
- The Worker has no page. Its console shows the jobs (notifications, report exports, devotion checks, event generation) every
  minute.

## 7. Sign in

All seed accounts use the password `TimeCheck-dev-123!`:

| Email | Role |
|---|---|
| `super@timecheck.test` | Super Admin |
| `admin@timecheck.test` | Admin |
| `leader@timecheck.test`, `leader2@timecheck.test` | Leaders |
| `member@timecheck.test`, `member2@timecheck.test` | Members |

Admins and Super Admins enrol an authenticator app (TOTP) on first sign-in. Invites and password resets arrive in Mailpit.

## 8. Run the tests

| What | Command | Needs running |
|---|---|---|
| Database (pgTAP) | `cd database; npx supabase test db` | Supabase |
| Backend (unit, integration, architecture) | `dotnet test backend` | Supabase, Redis |
| Frontend unit tests | `npm --prefix frontend test` | nothing |
| Type check and lint | `npm --prefix frontend run typecheck`, `npm --prefix frontend run lint` | nothing |
| End-to-end (flows plus accessibility) | `cd frontend; npm run e2e` | Supabase, Redis, API, Worker |

The e2e run starts the web app on :3400 itself if it isn't running. It uses your installed Chrome. To use Playwright's own
browser instead, run `npx playwright install chromium` and set `$env:PW_CHANNEL=""`. Its teardown deletes the people, events
and groups the tests created.

## 9. Check the production images (optional, before a deploy)

Render builds the same three Dockerfiles. Building and running them here catches a broken image before it reaches Render.

**Build:**

```powershell
docker build -t tc-api:check -f backend/Dockerfile.api backend
docker build -t tc-worker:check -f backend/Dockerfile.worker backend
docker build -t tc-web:check frontend
```

**Settings.** Create three files in `misc/docker/`. The `.env.*` names are git-ignored, so the values stay off GitHub.

- `misc/docker/.env.api`: `ASPNETCORE_ENVIRONMENT=Production`, `PORT=8080`, then every value from
  `backend/src/TimeCheck.Api/appsettings.Development.json` and your user-secrets, written as `Section__Key=value`
  (for example `Auth__SupabaseUrl=http://127.0.0.1:55421`, `Encryption__Keys__1=…`, `Cors__AllowedOrigins__0=http://localhost:13000`,
  `Notifications__AppUrl=http://localhost:13000`, `Redis__Configuration=127.0.0.1:6379`). Set
  `Notifications__Email__Provider=none` and `Ai__Provider=none`, as Render does.
- `misc/docker/.env.worker`: the same as `.env.api`, plus `ConnectionStrings__Hangfire` (same value as `Default`) and
  `ASPNETCORE_URLS=http://+:8081`, so it doesn't take the API's port.
- `misc/docker/.env.web`: `PORT=3000`, `APP_URL=http://localhost:13000`, `SUPABASE_URL=http://127.0.0.1:55421`,
  `SUPABASE_PUBLISHABLE_KEY=…`, `API_URL=127.0.0.1:8080`, `REDIS_URL=redis://127.0.0.1:6379`, `CHURCH_NAME=TimeCheck`.

**Run.** The relay gives the containers one shared network in which `127.0.0.1:55421` and the other Supabase and Redis ports
reach your machine. Sign-in tokens name `http://127.0.0.1:55421` as their issuer, so the API must see Supabase at exactly that
address.

```powershell
docker run -d --name tc-relay -p 13000:3000 -p 18080:8080 -v "${PWD}/misc/docker:/r" node:24-alpine node /r/relay.js
docker run -d --name tc-api --network container:tc-relay --env-file misc/docker/.env.api tc-api:check
docker run -d --name tc-worker --network container:tc-relay --env-file misc/docker/.env.worker tc-worker:check
docker run -d --name tc-web --network container:tc-relay --env-file misc/docker/.env.web tc-web:check
```

**Check.** Both health checks should answer 200, then run the deploy smoke tests against the containers:

```powershell
curl.exe -i http://localhost:18080/health/ready
curl.exe -i http://localhost:13000/api/health
cd frontend; $env:SMOKE_BASE_URL="http://localhost:13000"; $env:SMOKE_LEADER_EMAIL="leader@timecheck.test"; $env:SMOKE_LEADER_PASSWORD="TimeCheck-dev-123!"; npm run smoke; cd ..
```

**The setup command.** This should refuse with "already has a Super Admin", because the seed church has one:

```powershell
docker run --rm --network container:tc-relay --env-file misc/docker/.env.api tc-api:check create-super-admin --email x@example.test --first-name A --last-name B --gender MALE --birthdate 1990-01-01
```

**Clean up:**

```powershell
docker rm -f tc-web tc-worker tc-api tc-relay
```

## 10. Everyday tasks

| Task | How |
|---|---|
| Reset the database to the seed | `cd database; npx supabase db reset` (re-runs migrations and `seed.sql`; your local data is lost) |
| Add a schema change | A new SQL file in `database/supabase/migrations`, then `npx supabase db reset`. Never add EF migrations. |
| Change an email template | Edit `database/supabase/templates/*.html` or `config.toml`, then `npx supabase stop` and `npx supabase start`. A container restart keeps the old templates. |
| Stop everything | Ctrl+C the three apps, `cd database; npx supabase stop`, `docker compose -f misc/docker/docker-compose.yml down` |

## 11. Troubleshooting

| Symptom | Fix |
|---|---|
| Sign-in shows a 502, or requests hang after Docker has been running a while | `docker restart supabase_kong_timecheck supabase_db_timecheck`, then restart the API and Worker (their pooled database connections broke). |
| `docker` commands answer "500 Internal Server Error" | Quit Docker Desktop from the tray icon (not just its window), start it again, then `npx supabase start`. If it won't quit, `wsl --shutdown` first. That stops every container on the machine. |
| A port is already in use | TimeCheck uses 55420 to 55429, 5170, 3400 and 6379. Stop whatever else holds the port. |
| API logs 401s on admin actions | `Auth:ServiceRoleKey` isn't set, or is from an older `supabase start`. Set it again (§4). |
| The web app fails on start with a settings error | A line in `frontend/.env.development.local` is missing or isn't a valid URL (§5). |
| An admin can't get past the code step | Their authenticator was enrolled against another database. A Super Admin resets their MFA from Members. |
