# Deploy TimeCheck (Render + Supabase) and change its configuration

There are two environments, **staging** and **production**. Each one has its own Supabase project, Render services, Redis and
keys. Set up staging completely, deploy it and test it, then repeat for production with the `-production` names and **new**
keys.

What runs where:

| Piece | Where | Built from |
|---|---|---|
| Database, sign-in, file storage | Supabase project | `database/supabase/migrations` |
| Web app (Next.js) | Render web service `timecheck-web-<env>` | `frontend/Dockerfile` |
| API (.NET) | Render web service `timecheck-api-<env>` (private to the web app) | `backend/Dockerfile.api` |
| Worker (background jobs) | Render worker `timecheck-worker-<env>` | `backend/Dockerfile.worker` |
| Cache and rate limits | Render Key Value `timecheck-redis-<env>` | — |

Render builds the Docker images itself from the repository. To check an image before deploying, build and run it locally
(`local-setup.md` §9). The quick-reference version of this page is `../runbooks/deploy-and-rollback.md`.

---

## Part 1 — Supabase project

1. **Create the project.** At supabase.com, choose New project. Name it `timecheck-staging`, pick region **Southeast Asia
   (Singapore)**, and set a strong database password. Save the password in the password manager.
2. **Turn on asymmetric JWT signing keys.** In Project Settings → JWT Keys, migrate to the new signing keys. The API checks
   sign-in tokens against Supabase's published public keys (JWKS), so it rejects tokens signed with the old shared secret.
3. **Copy these values** (Project Settings → API Keys, and the Connect button):
   - Project URL (`https://<ref>.supabase.co`)
   - Publishable key (`sb_publishable_…`)
   - Secret / service role key. It is server-only and goes only into Render.
   - Connection strings: the **direct** connection, the **transaction pooler** (port 6543) and the **session pooler** (port 5432).
4. **Apply the migrations**, from `database/`, using the direct connection:
   ```powershell
   npx supabase db push --db-url "postgresql://postgres:<db-password>@db.<ref>.supabase.co:5432/postgres"
   ```
   After this one manual run, every deploy applies new migrations itself (Part 5).
5. **Create the API's login role and the church.** In the SQL editor, run:
   ```sql
   create role timecheck_api login password '<32+ random characters>' in role timecheck_app;
   select public.create_church('<Church name>', 'Asia/Manila');
   ```
   Never run `seed.sql` on a hosted project. It holds the dev accounts.
6. **Authentication settings.** These are set in the dashboard. `database/supabase/config.toml` only configures your local
   Supabase.

   | Dashboard page | Setting |
   |---|---|
   | Sign In / Providers | Email on. **Allow new users to sign up: off.** Anonymous sign-ins: off. |
   | Sign In / Providers → Email | Minimum password length 12, letters and digits required |
   | Multi-Factor | TOTP (authenticator app): enabled |
   | URL Configuration | Site URL = the web URL. Redirect URLs: `<web URL>/auth/confirm` |
   | Emails → SMTP Settings | Turn on custom SMTP with a real mail provider. The built-in sender allows only a few emails an hour, and invites depend on email. |
   | Emails → Templates → Invite user | Subject "You're invited to TimeCheck". Body: `database/supabase/templates/invite.html` |
   | Emails → Templates → Reset password | Subject "Reset your TimeCheck password". Body: `database/supabase/templates/recovery.html` |

## Part 2 — Render secrets group (before the Blueprint)

1. **Generate the keys.** Generate each key fresh for each environment and save a copy in the password manager. If the encryption
   keys are lost, the encrypted member data is lost with them.
   ```powershell
   [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
   ```
   Run that twice, once for `Encryption__Keys__1` and once for `Encryption__BlindIndexKey`. Then generate the web push pair:
   ```powershell
   npx web-push generate-vapid-keys
   ```
2. **Create the group.** In Render, go to Environment Groups → New group, named exactly `timecheck-secrets-staging`, with these
   keys:

   | Key | Value |
   |---|---|
   | `Auth__SupabaseUrl` | Project URL |
   | `Auth__ServiceRoleKey` | Secret / service role key |
   | `ConnectionStrings__Default` | Transaction pooler string with user `timecheck_api.<ref>` and its password, in Npgsql form, ending `;Max Auto Prepare=0;No Reset On Close=true` |
   | `ConnectionStrings__Hangfire` | Session pooler string (port 5432), same user |
   | `Encryption__ActiveKeyId` | `1` |
   | `Encryption__Keys__1` | First random value |
   | `Encryption__BlindIndexKey` | Second random value |
   | `Notifications__Push__PublicKey` / `Notifications__Push__PrivateKey` | The VAPID pair |
   | `Notifications__Push__Subject` | `mailto:` an address someone reads |

   A connection string in Npgsql form looks like this:
   `Host=aws-0-ap-southeast-1.pooler.supabase.com;Port=6543;Database=postgres;Username=timecheck_api.<ref>;Password=<pw>;SSL Mode=Require;Max Auto Prepare=0;No Reset On Close=true`

## Part 3 — Render Blueprint

1. Push `main` to GitHub.
2. In Render, go to **New → Blueprint**, connect `MarkAbecia/TimeCheck` and choose `render.yaml`. Optionally, check it first
   with `render blueprints validate render.yaml` (Render CLI 2.7+).
3. Render asks once for the values marked `sync: false`. Until you add a custom domain, the web URL is
   `https://timecheck-web-staging.onrender.com`.

   | Service | Key | Value |
   |---|---|---|
   | web | `APP_URL` | web URL |
   | web | `SUPABASE_URL` | Project URL |
   | web | `SUPABASE_PUBLISHABLE_KEY` | Publishable key |
   | api | `Cors__AllowedOrigins__0` | web URL |
   | api, worker | `Notifications__AppUrl` | web URL |

4. Apply. Render creates Redis and the three services and builds the images. The API counts as up once `/health/ready` passes,
   meaning the database and Redis are both reachable. The web app counts as up once `/api/health` passes.
5. **Copy each service's deploy hook URL** from Settings → Deploy Hook, for web, API and Worker. The hooks are secrets.

## Part 4 — First accounts

1. **First Super Admin.** In the API service, open **Shell** and run:
   ```
   dotnet TimeCheck.Api.dll create-super-admin --email <email> --first-name <name> --last-name <name> --gender MALE|FEMALE --birthdate yyyy-MM-dd
   ```
   The invite email arrives. Set a password from the link, accept the privacy notice and enrol an authenticator. The command
   refuses once the church has a Super Admin, so add everyone else in the app.
2. **Smoke-test accounts.** Register one leader and one admin who aren't real members. Sign in as that admin. On the
   authenticator step, type the key the page shows instead of scanning it, and save the key. That key is
   `SMOKE_ADMIN_TOTP_SECRET`.

## Part 5 — Automatic deploys (GitHub)

1. In repo Settings → Environments, create `staging`, and `production` with you as a **required reviewer**.
2. Add these per environment:

   | Kind | Name | Value |
   |---|---|---|
   | secret | `SUPABASE_DB_URL` | Direct connection URL, as `postgres` |
   | secret | `RENDER_DEPLOY_HOOK_WEB`, `RENDER_DEPLOY_HOOK_API`, `RENDER_DEPLOY_HOOK_WORKER` | Hook URLs from Part 3 |
   | secret | `SMOKE_LEADER_EMAIL`, `SMOKE_LEADER_PASSWORD` | Smoke leader |
   | secret | `SMOKE_ADMIN_EMAIL`, `SMOKE_ADMIN_PASSWORD`, `SMOKE_ADMIN_TOTP_SECRET` | Smoke admin |
   | variable | `SMOKE_BASE_URL` | web URL |

3. From now on, `.github/workflows/deploy.yml` runs these steps in order: **migrate** (`supabase db push`), **deploy** (the three
   hooks) and **smoke** (it waits until the web app reports the new commit, then signs in as the leader and the admin). Auto-deploy
   is off on every service, so code never goes out ahead of its migrations.
   - Staging: every push to `main`.
   - Production: push a tag, then approve the run in GitHub:
     ```powershell
     git tag v1.0.0; git push origin v1.0.0
     ```
4. After the first deploy, check the Worker's logs for 2 minutes: `deliver-notifications` and `report-exports` should run every
   minute without errors.

## Part 6 — Custom domain (production)

1. Web service → Settings → Custom Domains → add `timecheck.<your domain>`, then create the DNS record Render shows.
2. Change every place that holds the web URL to the new domain: `APP_URL` (web), `Cors__AllowedOrigins__0` and
   `Notifications__AppUrl` (API), `Notifications__AppUrl` (Worker), the Supabase Site URL and redirect URL, and the GitHub
   variable `SMOKE_BASE_URL`.

---

## Changing configuration

First find where the setting lives. There are five places:

| Where | What lives there | How a change takes effect |
|---|---|---|
| **The app's Settings page** (Super Admin) | Age groups, retention periods, devotion open, close and reminder times, AI detection on/off and thresholds | At once. No deploy. Audited as `settings.update`. |
| **Render environment variables** (a service's Environment tab, or an Environment Group) | Secrets, URLs, providers, logging, feature switches | Saving offers "Save and deploy", which restarts the service with the new values. Changing a group restarts every service linked to it. |
| **`render.yaml`** (in git) | Services, instance plan and count, region, health checks, which variables exist | Commit and push to `main`. Render syncs the Blueprint (or use Blueprint → Manual sync). New `sync: false` keys ask for a value. |
| **Supabase dashboard** | Sign-in rules, MFA, email templates and SMTP, redirect URLs, backups | At once. No deploy. |
| **Code and migrations** (in git) | Schema, behaviour, texts | Push to `main` (staging), then tag (production). |

### Common changes

| I want to… | Do this |
|---|---|
| Change age groups, retention, devotion times or AI thresholds | Sign in as Super Admin → Settings. |
| Change the church name shown in the app | Web service → `CHURCH_NAME` → Save and deploy. |
| See more detail in the logs | API or Worker → add `Serilog__MinimumLevel__Default` = `Debug` → Save and deploy. Set it back to `Information` afterwards, because debug logging is noisy. |
| Send traces and metrics to a monitoring tool | API → add `OTEL_EXPORTER_OTLP_ENDPOINT` (plus `OTEL_EXPORTER_OTLP_HEADERS` if the tool needs a key). |
| Handle the Sunday peak | `render.yaml` → raise `numInstances` on `timecheck-web-*` and `timecheck-api-*` (or pick a bigger `plan`) → push. All three services are stateless. Keep a single Worker. |
| Move to a new domain | Part 6, step 2. |
| Make everyone accept a new privacy notice | Change the notice text in code. Set `Auth__PrivacyConsentVersion` on the API to a new value (for example `2027-03`) in the same release. Everyone is asked again at their next sign-in. |
| Change sign-in rules (password length, MFA) | Supabase → Authentication. Also update `database/supabase/config.toml`, so local matches. |
| Change an email's wording | Edit `database/supabase/templates/*.html` in git, then paste the new body into Supabase → Emails → Templates for each environment. |
| Change the schema | Add a new SQL file in `database/supabase/migrations` and push. The deploy runs it before the new code. It must not break the version that is running now: add first, and remove only in a later release. |
| Turn email notifications on | Not possible yet. The only production value is `Notifications__Email__Provider=none`, until a provider is chosen and built (ADR 0007). Invites and password resets don't depend on this: they go through Supabase's SMTP. |
| Turn AI detection on | Needs a provider built first (ADR 0006). Until then `Ai__Provider` stays `none`. The Settings switch then only records the church's preference. |
| Rotate a password or key | `../runbooks/rotate-secrets.md`. Never replace `Encryption__Keys__1`. Add `Encryption__Keys__2` and set `Encryption__ActiveKeyId=2`. Never rotate `Encryption__BlindIndexKey`. |

Settings that must stay as they are in production:

- `ASPNETCORE_ENVIRONMENT=Production`: it turns on HSTS and hides the API reference.
- `Auth__RequireAdminMfa`: leave it unset (it defaults to true).
- Never set `Ai__Provider=fake`, `Ai__FakeAllowed` or `Notifications__Email__Provider=mailpit`. They are for local use only.
  `fake` refuses to start without `Ai__FakeAllowed`. `mailpit` would start, but it sends to a mail catcher that doesn't exist
  on Render.

### Naming rule for variables

A .NET setting `Section:Key` becomes the environment variable `Section__Key`. List items use their index:
`Cors:AllowedOrigins[0]` becomes `Cors__AllowedOrigins__0`, and a second web origin would be `Cors__AllowedOrigins__1`. The web
app's variables are plain names (`APP_URL`, `SUPABASE_URL`, …). The web app checks them at start-up and refuses to run if one is
missing or isn't a URL.

## Roll back

1. **Code:** in Render, open each of API, Worker and web, and redeploy its previous successful deploy. Roll the API and the Worker
   back together.
2. **Migrations:** don't roll them back. They are written so that the previous release runs on the new schema. Fix a bad
   migration with a new forward one.
3. **Bad data:** use a reviewed SQL fix. Restore from a backup only as a last resort (`../runbooks/restore-backup.md`).
