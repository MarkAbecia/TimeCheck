# TimeCheck — Product & Technical Requirements

> **For Claude Code.** This is the source of truth for building TimeCheck: a cell group attendance and member management system for a church.
> Read the whole file before writing code. Build in the phase order in §14. When something here is ambiguous, check §15 (Open Questions). If it isn't covered there, pick the more secure and simpler option and log the decision in `misc/docs/adr/`.

---

## 1. Overview

TimeCheck tracks attendance for church cell groups (daily devotions, morning prayer, weekly meetings, and scheduled trainings), manages members, and produces reports for leaders and admins.

### 1.1 Goals
- Leaders can record attendance for their group in under 30 seconds per event on a phone.
- Members can upload their daily devotion (text or image) in under 3 taps.
- Admins get accurate reports by group, event type, and member demographics.
- The system is **secure by default** (it holds personal data, possibly of minors) and **scales horizontally** without a redesign.

### 1.2 Non-goals (v1)
- Native mobile apps (the web app must be mobile-first and installable as a PWA instead).
- Payments, giving, or donations.
- Public-facing church website.

---

## 2. Tech Stack (fixed)

| Layer | Choice | Notes |
|---|---|---|
| Frontend | **Next.js** (latest stable, App Router, TypeScript strict) | Check the current stable version before scaffolding. Do **not** use an end-of-life version or one with open CVEs. |
| UI | Tailwind CSS, shadcn/ui, react-hook-form + Zod, sonner, TanStack Query | |
| Design skills | **Impeccable** + **Hallmark** (Claude Code skills) | See §11 for how to use them. |
| Backend | **.NET** (latest LTS), ASP.NET Core Web API, C# | Clean Architecture + DDD |
| ORM | EF Core + Npgsql | |
| Database | **Supabase** Postgres | Also Supabase Auth and Supabase Storage |
| Cache / rate limit / locks | **Redis** (Render Key Value or another managed Redis) | |
| Background jobs | Hangfire (Postgres storage) running in a separate worker service | |
| Source control & CI | **GitHub** + GitHub Actions | |
| Hosting | **Render**: Web Service (frontend), Web Service (API), Background Worker, Key Value (Redis) | Docker-based deploys |
| Email | A transactional provider behind an `IEmailSender` interface (e.g. Resend, Postmark, or SMTP) | |
| AI detection | External provider(s) behind an `IAiContentDetector` interface | See §6.3 |

---

## 3. Repository Structure

The repository has exactly four top-level project folders. The only exceptions at the root are files that tools require there.

```
/
├── frontend/          # Next.js app
├── backend/           # .NET solution (API + Worker + tests)
├── database/          # Supabase project: SQL migrations, RLS policies, seed data
├── misc/              # Docs, ADRs, scripts, Docker/infra helpers
│
├── .github/           # (required at root) workflows, dependabot, CODEOWNERS, PR template
├── render.yaml        # (required at root) Render Blueprint
├── .editorconfig
├── .gitignore
├── CLAUDE.md          # Short pointer: "Read misc/docs/REQUIREMENTS.md first"
└── README.md
```

### 3.1 `frontend/`
```
frontend/
├── src/
│   ├── app/
│   │   ├── (auth)/login, forgot-password, reset-password
│   │   ├── (app)/dashboard, attendance, devotions, members, groups,
│   │   │         events, invites, reports, notifications, profile, admin
│   │   └── api/             # BFF route handlers ONLY (auth cookie handling, proxy to API)
│   ├── components/ui/       # shadcn primitives
│   ├── components/          # feature components
│   ├── features/<feature>/  # hooks, schemas (Zod), api clients per feature
│   ├── lib/                 # api client, auth helpers, utils
│   ├── middleware.ts        # route protection + security headers
│   └── styles/
├── public/
├── tests/ (unit: Vitest, e2e: Playwright)
├── DESIGN.md / PRODUCT.md   # produced by Impeccable init (§11)
├── Dockerfile
└── package.json
```

### 3.2 `backend/`
```
backend/
├── TimeCheck.sln
├── src/
│   ├── TimeCheck.Domain/          # Entities, value objects, domain events, enums. No dependencies.
│   ├── TimeCheck.Application/     # Use cases (CQRS via MediatR or plain handlers), DTOs, validators (FluentValidation), interfaces
│   ├── TimeCheck.Infrastructure/  # EF Core, Supabase Storage, Redis, email, AI detectors, Hangfire
│   ├── TimeCheck.Api/             # Controllers or minimal API endpoints, auth, middleware, OpenAPI
│   └── TimeCheck.Worker/          # Hangfire server: reminders, notifications, AI detection, report jobs
├── tests/
│   ├── TimeCheck.Domain.Tests/
│   ├── TimeCheck.Application.Tests/
│   ├── TimeCheck.Api.IntegrationTests/   # Testcontainers Postgres + Redis
│   └── TimeCheck.ArchitectureTests/      # NetArchTest: enforce layer rules
├── Directory.Build.props   # nullable enable, warnings as errors, analyzers
├── Dockerfile.api
└── Dockerfile.worker
```

DDD conventions: private setters, factory methods (`Member.Create(...)`), a `BaseEntity` (Id, CreatedAt, UpdatedAt, CreatedBy, UpdatedBy), and soft delete (`DeletedAt`) where §7 says so. Domain logic lives in entities and domain services, not in controllers.

### 3.3 `database/`
```
database/
├── supabase/
│   ├── config.toml
│   ├── migrations/        # Timestamped plain-SQL migrations (source of truth for schema)
│   ├── seed.sql           # Dev-only seed: roles, event types, demo church/groups
│   └── tests/             # pgTAP tests for RLS policies and constraints
└── README.md              # How to run locally (supabase start), migrate, reset
```
**Rule:** The schema is defined by SQL migrations here, **not** EF Core migrations. EF Core maps onto the existing schema using Fluent configuration. This keeps RLS policies, indexes, enums, and functions in one reviewable place.

### 3.4 `misc/`
```
misc/
├── docs/
│   ├── REQUIREMENTS.md      # this file
│   ├── adr/                 # Architecture Decision Records (0001-*.md)
│   ├── api/                 # exported OpenAPI spec
│   ├── security/            # threat model, data inventory, incident runbook
│   └── runbooks/            # deploy, rollback, rotate secrets, restore backup
├── scripts/                 # dev helpers (bootstrap.sh, gen-api-client.sh)
└── docker/                  # docker-compose.yml for local API + worker + Redis
```

---

## 4. Roles & Permissions

### 4.1 Role definitions
| Role | Purpose |
|---|---|
| **Super Admin** | Owns the system. Manages Admins, global settings (event types, AI detection thresholds, retention), and audit logs. Can do everything an Admin can. Should be 1–3 people. |
| **Admin** | Runs church-wide operations. Promotes/demotes leaders, schedules church-wide events, sees all groups and reports. |
| **Leader** | Runs one cell group (or more, see §15). Registers members, takes attendance, schedules group events, reviews devotions, sees their group's reports. A leader is also a member and uploads their own devotions. |
| **Member** | Uploads devotions, logs invites, views their own attendance and profile. |

### 4.2 Permission matrix
Enforce this **on the backend** with policy-based authorization (`[Authorize(Policy = "...")]`) plus resource checks (for example, "is this leader the leader of this group?"). The frontend only hides UI; it is never the security boundary.

| Capability | Super Admin | Admin | Leader | Member |
|---|:-:|:-:|:-:|:-:|
| Log in, edit own profile | ✓ | ✓ | ✓ | ✓ |
| View own attendance history | ✓ | ✓ | ✓ | ✓ |
| Upload own devotion (day/night) | ✓ | ✓ | ✓ | ✓ |
| Log invites (expected / came) | ✓ | ✓ | ✓ | ✓ |
| Receive notifications | ✓ | ✓ | ✓ | ✓ |
| Register members into **own** group | ✓ | ✓ | ✓ | – |
| Take attendance for **own** group | ✓ | ✓ | ✓ | – |
| View devotions + AI score of **own** group | ✓ | ✓ | ✓ | – |
| Schedule Cell Meeting / PID/ILD / Mentoring for **own** group | ✓ | ✓ | ✓ | – |
| Member Status report (own group) | ✓ | ✓ | ✓ | – |
| Schedule PEPSOL, RFN Mentoring, Conferences, Special Trainings, other church-wide events | ✓ | ✓ | – | – |
| Schedule PID/ILD / Mentoring for **any** group | ✓ | ✓ | – | – |
| Promote / demote Member ↔ Leader | ✓ | ✓ | – | – |
| Create / archive cell groups, reassign members | ✓ | ✓ | – | – |
| All reports (Leaders & Members list, Cell Status, Group Monitoring, Attendance, Member demographics) | ✓ | ✓ | – | – |
| Promote / demote Admin | ✓ | – | – | – |
| Manage event types, thresholds, system settings | ✓ | – | – | – |
| View audit log | ✓ | – | – | – |

Role changes are recorded in `role_changes` and the audit log, and they trigger an email + in-app notification to the affected user. A user's active sessions are re-evaluated on the next request (roles are read from the database or cache, never trusted from a stale token; see §8.1).

---

## 5. Attendance Model

Every attendable thing is an **Event** (one occurrence) of an **Event Type**. Attendance is a row per (event, user).

### 5.1 Event types (seeded; Super Admin can add more)
| Code | Name | Cadence | Scope | Created by | How attendance is recorded |
|---|---|---|---|---|---|
| `DEVOTION_DAY` | Devotion (Day) | Daily | Individual | System (implicit) | Member uploads a devotion → counts as present |
| `DEVOTION_NIGHT` | Devotion (Night) | Daily | Individual | System (implicit) | Same as above |
| `MORNING_PRAYER` | Morning Prayer | Daily **Mon–Sat** (never Sunday) | Group | System generates daily | Leader marks (see §15 Q2) |
| `WEEK_OF_FIRE` | Week of Fire | Weekly | Group or church-wide (see §15 Q3) | System generates weekly | Leader marks |
| `CELL_CELEBRATION` | Sunday Cell Celebration | Weekly (Sunday) | Church-wide, tracked per group | System generates weekly | Leader marks; invites tracked |
| `CELL_MEETING` | Cell Meeting | Weekly | Group | Leader schedules (day/time per group) | Leader marks; invites tracked |
| `PID_ILD` | PID/ILD | Ad hoc | Group | Leader (own group) or Admin | Leader/Admin marks |
| `MENTORING` | Mentoring | Ad hoc, if necessary | Group or individual | Leader (own group) or Admin | Leader/Admin marks |
| `PEPSOL` | PEPSOL Training | Scheduled | Church-wide | Admin | Admin marks, or leaders mark their own members |
| `RFN_MENTORING` | RFN Mentoring | Scheduled | Church-wide | Admin | Same |
| `CONFERENCE` | Conference | Scheduled | Church-wide | Admin | Same |
| `SPECIAL_TRAINING` | Special Training (incl. outside church) | Scheduled | Church-wide | Admin | Same |
| `OTHER` | Other (named by Admin) | Scheduled | Church-wide or group | Admin | Same |

Categories for reporting: **Daily**, **Weekly**, **Leader-scheduled**, **Admin-scheduled**.

### 5.2 Rules
- Recurring events are generated by a scheduled job 14 days ahead (idempotent: unique on `(event_type, group_id, occurs_on)`).
- All times are stored in UTC and displayed in the church's timezone (default `Asia/Manila`). "Daily" boundaries use the church timezone.
- Attendance statuses: `PRESENT`, `LATE`, `ABSENT`, `EXCUSED`. Unmarked = `UNMARKED`. It counts as absent in rates but is shown separately so leaders see what they forgot.
- Attendance can be edited for 7 days after the event (configurable). After that only Admins can edit. Every edit goes to the audit log with old and new values.
- Bulk marking: "Mark all present" then toggle the exceptions. This is the primary mobile flow.
- Optimistic concurrency (`xmin` / row version) so two people marking the same event don't overwrite each other silently.

---

## 6. Features & Acceptance Criteria

### 6.1 Authentication (Login)
- Email + password via **Supabase Auth**. Magic link and password reset are supported.
- No public sign-up. Accounts are created only through Member Registration (§6.4) or by an Admin. Disable sign-ups in Supabase Auth settings.
- Session is held in **httpOnly, Secure, SameSite=Lax cookies** set by the Next.js BFF route handlers. Tokens are never put in `localStorage` or exposed to client JS.
- Account lockout / backoff after repeated failures (Supabase rate limits + our own Redis-backed limiter on the BFF).
- **MFA (TOTP) required for Super Admin and Admin**, optional for others.
- First login after registration forces a password set/change.
- AC: An unauthenticated request to any `(app)` route redirects to `/login`. Expired sessions refresh silently. Logout revokes the refresh token.

### 6.2 Attendance Monitoring
- **Leader view:** today's events for their group at the top, each with a one-tap roster. Past events list with completion status ("3 events not yet marked").
- **Member view:** own calendar/history with streaks for devotions and morning prayer.
- **Admin view:** all events, filter by group, type, category, and date range.
- AC: Marking a 20-person roster takes ≤ 3 interactions with "mark all present". Works on a 360px-wide screen. Morning Prayer is never generated for Sunday.

### 6.3 Devotion Upload + AI Detection
- Each member submits **one Day and one Night devotion per day** (church timezone). They can edit until the slot closes (configurable; default: Day closes 23:59, Night closes 05:00 the next day; see §15 Q4).
- Content: **text** (max 5,000 chars) **or image** (JPEG/PNG/WebP/HEIC, max 10 MB), or both.
- **AI detection** runs **asynchronously** in the Worker after upload:
  - Text → an AI-text detection provider. Image → an AI-image detection provider. Each sits behind `IAiContentDetector` so providers can be swapped or combined.
  - Store `ai_score` (0–100 %, the probability that it is AI-generated), `ai_provider`, `ai_model_version`, `ai_checked_at`, `ai_status` (`PENDING`, `DONE`, `FAILED`, `SKIPPED`).
  - For images that contain handwriting or text, also run OCR and send the extracted text through text detection. Show both scores.
  - Retries with exponential backoff, and a circuit breaker if the provider is down. Upload must never fail because detection failed.
  - Cache results by content hash (SHA-256) in Redis or the database so the same content isn't re-billed.
- **Display:** Leaders and Admins see the percentage with a band label (Likely human < 40 %, Uncertain 40–75 %, Likely AI > 75 %; thresholds configurable by Super Admin). Members see their own score only if the Super Admin enables it.
- **Fairness rule (required):** AI detection is **advisory only**. It never auto-rejects a devotion or marks someone absent. The UI shows a short disclaimer that detectors produce false positives. Leaders can mark "Reviewed" with an optional note.
- Reminders: a push/email/in-app reminder at configurable times (default 07:00 Day, 20:00 Night) if not yet submitted.
- Privacy: images are stored in a **private** Supabase Storage bucket and served only through short-lived signed URLs (≤ 5 min). EXIF/GPS metadata is stripped on upload. Content is sent to the detection provider only with the minimum needed, and the provider must not retain or train on it (check its terms; record the choice in an ADR).
- AC: Upload returns in < 2 s. The score appears within 60 s under normal load. Uploading a second Day devotion on the same day edits the first instead of creating a duplicate.

### 6.4 Member Registration (by Leader)
- Leader fills in: first name, last name, nickname (optional), gender, birthdate, grade level (optional), mobile, email, address (optional), date joined, initial status (default `NEW_MEMBER`), and guardian name + contact **if the member is under 18**.
- The backend creates the Supabase Auth user via the Admin API (service role key, **server-side only**) and sends an invite email.
- Duplicate detection: warn on matching email, or mobile, or (name + birthdate).
- Member is assigned to the leader's group. Only Admins can move members between groups.
- Member statuses: `NEW_MEMBER`, `DISCIPLE`, `INCONSISTENT`, `INACTIVE` (+ `LEADER` is a role, not a status). Status changes are logged.
  - Optional automation (configurable, off by default): suggest `INCONSISTENT` when attendance in the last 4 weeks is below X %, and `INACTIVE` after N weeks with no attendance. Suggestions only. A leader confirms.
- **Minors:** if birthdate gives age < 18, a guardian consent checkbox (with consent date) is required before saving. See §8.4.

### 6.5 Invites (Member)
- Members log people they invited to **Cell Meeting** or **Cell Celebration**: guest name, optional contact, event.
- Status flow: `EXPECTED` → `CAME` (marked by the member or leader) or `DID_NOT_COME`.
- A guest who came can be converted into a registered member by the leader. This links the invite to the new member for "invites converted" stats.
- Guest contact data is PII: it is visible only to the inviter, their leader, and Admins, and is purged after 90 days if never converted (configurable).

### 6.6 Scheduling
- Create/edit/cancel events with: type, title, description, start/end, location or online link, target audience (whole church / specific groups / specific members), and recurrence (for Cell Meeting).
- Cancelling an event notifies invitees and removes it from attendance rates.
- Conflict warning (not a block) when a group already has an event at that time.
- Calendar view (month/week/agenda) and a list view.

### 6.7 Notifications
- Channels: **in-app** (bell + list), **email**, and **web push** (PWA). Users can choose per category.
- Triggers: role promotion/demotion, new event scheduled for you/your group, event changed/cancelled, devotion reminders, attendance not yet marked (leader, 2 h after event end), invite reminders, report ready.
- All sends go through the Worker (outbox pattern: write a `notification` row in the same transaction, the worker delivers). Idempotent and retried.

### 6.8 Report Generation
All reports: filters (date range, group, event type/category), on-screen table + chart, export **CSV, XLSX, PDF**. Large exports run as background jobs and notify when ready. Exports are stored privately with signed download links that expire in 24 h.

| Report | Who | Contents |
|---|---|---|
| **Attendance Report** (per subcategory) | Admin; Leader (own group) | Per event type and per member: attended / total, rate %, trend over time, late/excused counts. |
| **Member Report** | Admin; Leader (own group) | Member list with grouping and counts by **gender**, **grade level**, **age bracket** (configurable: e.g. ≤12, 13–17, 18–24, 25–35, 36+), status. |
| **Group Monitoring** | Admin | Per group: size, active %, average attendance per event type, devotion submission rate, invites expected vs came, unmarked events. |
| **Leaders & Members List** | Admin | Hierarchy of leaders and their members. |
| **Cell Status** | Admin | Health of each cell: status mix (New/Disciple/Inconsistent/Inactive), growth over the period, attendance trend. |
| **Member Status** | Leader | Own group's members with status, last attendance, devotion streak, and flags. |

AC: Report numbers must equal the numbers you get from querying raw attendance rows (cover this with integration tests). Reports for one year of data and 1,000 members render in < 3 s, or are queued.

### 6.9 Profile Alteration
- Users edit: display name, nickname, photo, mobile, address, notification preferences, password, MFA.
- Changing email requires re-verification.
- Fields like birthdate, gender, and group are editable only by their leader or an Admin (to keep reports trustworthy). Members can request a correction.
- Profile photos go through the same upload pipeline (type/size validation, EXIF stripping, private bucket).

### 6.10 Admin & Super Admin tools
- User management: search, deactivate/reactivate (soft delete), reset MFA, force logout.
- Group management: create, rename, archive, assign leader, move members.
- Settings (Super Admin): event types, devotion windows and reminder times, AI thresholds and provider config, retention periods, age brackets, timezone.
- Audit log viewer (Super Admin): filter by actor, action, entity, and date.

---

## 7. Data Model (initial)

Postgres in Supabase. Use `uuid` primary keys (`gen_random_uuid()`), `timestamptz` everywhere, and snake_case names. Every table has `created_at` and `updated_at`. Tables holding people data also have `deleted_at` (soft delete).

**Multi-tenancy from day one:** every business table has `church_id` (FK → `churches`), and every query and RLS policy is scoped by it. The v1 UI assumes one church, but this keeps the option to host more churches or networks without a migration.

Core tables (Claude Code: refine columns, add indexes, and write them as migrations):

- `churches` (id, name, timezone, settings jsonb)
- `app_users` (id, church_id, auth_user_id → auth.users, role enum, first_name, last_name, nickname, gender, birthdate, grade_level, mobile, email, address, status enum, joined_on, photo_path, is_minor (generated), guardian_name, guardian_contact, guardian_consent_at, deactivated_at)
- `cell_groups` (id, church_id, name, leader_id → app_users, parent_group_id nullable, archived_at)
- `group_memberships` (id, group_id, user_id, joined_on, left_on). History is kept, never overwritten.
- `event_types` (id, church_id, code, name, category enum, cadence, default_scope, is_system)
- `events` (id, church_id, event_type_id, group_id nullable, title, starts_at, ends_at, location, created_by, cancelled_at, recurrence_rule nullable, occurs_on date). Unique `(event_type_id, group_id, occurs_on)` for generated events.
- `event_audiences` (event_id, group_id nullable, user_id nullable)
- `attendance` (id, event_id, user_id, status enum, marked_by, marked_at, note, row_version). Unique `(event_id, user_id)`.
- `devotions` (id, church_id, user_id, devotion_date, slot enum DAY/NIGHT, text_content, image_path, content_hash, ai_status, ai_text_score, ai_image_score, ai_provider, ai_model_version, ai_checked_at, reviewed_by, reviewed_at, review_note). Unique `(user_id, devotion_date, slot)`.
- `invites` (id, church_id, inviter_id, event_id, guest_name, guest_contact, status enum, converted_user_id, purge_after)
- `role_changes` (id, user_id, from_role, to_role, changed_by, reason, changed_at)
- `member_status_changes` (id, user_id, from_status, to_status, changed_by, changed_at)
- `notifications` (id, user_id, type, payload jsonb, channels, read_at, delivered_at, attempts)
- `report_jobs` (id, requested_by, type, params jsonb, status, file_path, expires_at)
- `audit_logs` (id, church_id, actor_id, action, entity_type, entity_id, before jsonb, after jsonb, ip_hash, user_agent, occurred_at). **Append-only**: no UPDATE/DELETE grants.

Indexes to start with: `attendance(event_id)`, `attendance(user_id, marked_at)`, `events(church_id, starts_at)`, `events(group_id, starts_at)`, `devotions(user_id, devotion_date)`, `group_memberships(group_id) where left_on is null`, plus a partial index on `app_users(church_id) where deleted_at is null`.

Scale note: `attendance` and `devotions` grow fastest. Design queries with keyset pagination, and keep the option to partition by month later (record this in an ADR). Reporting aggregates can come from materialized views refreshed by the Worker.

---

## 8. Security & Compliance (mandatory)

Target: **OWASP ASVS Level 2**, **OWASP Top 10 (2021+) / API Security Top 10**, and the **Philippine Data Privacy Act of 2012 (RA 10173)** with its IRR and NPC circulars. This is a church app that may hold data about minors, so treat all member data as personal and some as sensitive.

### 8.1 Authentication & authorization
- Supabase Auth issues JWTs. The .NET API validates them (issuer, audience, expiry, signature via Supabase JWKS; asymmetric signing keys preferred).
- **The role and group membership come from our `app_users` table** (cached in Redis ≤ 60 s and invalidated on role change), never from client-editable claims or `user_metadata`.
- Deny by default: every endpoint requires auth unless explicitly marked `[AllowAnonymous]` (health check only). An architecture test enforces this.
- Resource-level checks on every request (IDOR prevention): a leader can only touch their own group's data. Write integration tests that try cross-group access and expect `403`/`404`.
- The Supabase **service role key exists only in the backend/worker environment**, never in the frontend or the repo.

### 8.2 Supabase hardening
- **RLS enabled on every table** in the `public` schema, with deny-all for `anon` and `authenticated`. The frontend never queries Postgres or the Data API directly; all data flows through the .NET API. RLS is defense in depth in case a key leaks. Add pgTAP tests proving `anon`/`authenticated` can't read.
- The backend connects with a dedicated least-privilege Postgres role (not `postgres`), through the Supavisor pooler (transaction mode for the API, with prepared statements configured accordingly for Npgsql; session/direct for migrations).
- Storage buckets are private. Access goes only through backend-issued signed URLs.
- Enable PITR/backups on the production project. Document and test the restore procedure in `misc/docs/runbooks/`.

### 8.3 Application security
- **Input validation:** Zod on the frontend (UX) **and** FluentValidation on the backend (authoritative). Reject unknown fields.
- **Output:** React escaping. No `dangerouslySetInnerHTML`. If rich text is ever added, sanitize server-side.
- **File uploads:** allow-list by magic bytes (not extension), size caps, re-encode images (which strips EXIF and neutralizes polyglots), random storage keys, no user-controlled paths. Malware scanning hook in the Worker (pluggable; ADR).
- **Rate limiting:** Redis-backed sliding window per IP and per user. Stricter on login, password reset, registration, uploads, and report exports. Return `429` with `Retry-After`.
- **CSRF:** SameSite cookies + an Origin/Referer check on BFF mutating routes + anti-CSRF token for cookie-authenticated POSTs.
- **CORS:** API allows only the frontend origin(s). No wildcards with credentials.
- **Security headers** (Next.js middleware + API): strict CSP with nonces, HSTS (preload-ready), `X-Content-Type-Options: nosniff`, `Referrer-Policy: strict-origin-when-cross-origin`, `Permissions-Policy` (camera only where needed), `frame-ancestors 'none'`.
- **Errors:** RFC 7807 Problem Details. No stack traces or internal IDs leaked in production.
- **Secrets:** only in Render environment groups and GitHub Actions secrets. `.env*` files are gitignored, with `.env.example` committed. Enable GitHub secret scanning + push protection.
- **Dependencies:** Dependabot for npm, NuGet, Docker, and Actions. Fail CI on high/critical vulnerabilities.

### 8.4 Personal data & privacy (RA 10173)
- Maintain a **data inventory** in `misc/docs/security/data-inventory.md`: each PII field, purpose, who can see it, retention.
- **Encrypt sensitive columns at the application level** (birthdate, mobile, address, guardian contact, guest contact) using envelope encryption with a key from the environment (rotatable, versioned key ID stored with the ciphertext). Store a separate blind index (HMAC) where search or dedup is needed.
- **Minors:** guardian consent is required and recorded. Minors' data is visible only to their leader and Admins. Minors' reports use aggregates where possible.
- **Data minimization:** the address and grade level fields are optional. Do not collect religion-unrelated sensitive data.
- **Data subject rights:** an Admin action to export one person's data (JSON/PDF) and to erase/anonymize them (keeping aggregate attendance counts with the person anonymized).
- **Retention:** configurable purge jobs (guest invites 90 days, deactivated users after N years, audit logs ≥ 1 year).
- A privacy notice and consent screen on first login, with consent version and timestamp stored.
- PII is masked in logs (Serilog destructuring policy). Never log tokens, passwords, or devotion content.

### 8.5 Auditing & monitoring
- Audit log for: login success/failure, role changes, status changes, attendance edits, member create/edit/deactivate, exports, settings changes, data-subject requests.
- Structured logging (Serilog → stdout; Render collects it) with correlation IDs propagated frontend → API → worker.
- OpenTelemetry traces and metrics (exporter configurable). Health endpoints: `/health/live`, `/health/ready` (DB + Redis).
- Alerts for error-rate spikes, failed jobs, and repeated login failures.

### 8.6 CI security gates (GitHub Actions)
On every PR: build, lint, type-check, unit + integration tests, **CodeQL** (C# + TS), dependency audit (`dotnet list package --vulnerable`, `npm audit` / OSV), **Trivy** scan of Docker images, secret scan (gitleaks), and optionally **SonarQube/SonarCloud** quality gate. A PR cannot merge if any gate fails.

---

## 9. Scalability & Performance

- **Stateless services.** API and frontend keep no in-memory session state, so Render can run multiple instances. Anything shared (rate-limit counters, cache, distributed locks) lives in Redis.
- **Separate Worker service** for jobs, so heavy work (AI detection, exports, reminders) never slows API requests. Scale it independently.
- **Caching (Redis):** user role/permissions (60 s), group rosters (5 min, invalidated on change), report aggregates (keyed by filters, invalidated by attendance writes for that group/date). Use a cache-aside pattern with stampede protection.
- **Database:** connection pooling via Supavisor, keyset pagination on all lists (no `OFFSET` on large tables), N+1 prevention (`AsNoTracking`, projections to DTOs), materialized views for heavy reports, and a partitioning plan for `attendance`.
- **Idempotency:** `Idempotency-Key` header supported on attendance-marking and upload endpoints, because phones retry on flaky mobile data.
- **Frontend:** Server Components for data-heavy pages, streaming with Suspense, images through `next/image` with signed URLs, and a performance budget of LCP < 2.5 s on 4G and JS < 200 KB gzipped for the main routes.
- **API versioning:** `/api/v1/...` from the start. The OpenAPI spec is generated and the TypeScript client is generated from it (`misc/scripts/gen-api-client.sh`). No hand-written fetch types.
- **Load target for v1:** 2,000 users, 300 concurrent at peak (Sunday), p95 API latency < 300 ms on core endpoints. Add a k6 script in `misc/scripts/load/`.

---

## 10. API Design Conventions
- REST, JSON, camelCase, `/api/v1`. Resource-oriented (`/groups/{id}/events`, `/events/{id}/attendance`).
- Problem Details for errors, with validation errors keyed by field.
- Pagination: `?limit=&cursor=` returning `{ items, nextCursor }`.
- Dates are ISO 8601 UTC. The frontend converts to the church timezone.
- Every mutating endpoint writes an audit entry via a MediatR pipeline behavior (or equivalent), not by hand in each handler.
- OpenAPI with security schemes documented. Swagger UI is enabled only outside production.

---

## 11. Design & UX (use the Impeccable and Hallmark skills)

Both skills are design skills for Claude Code. To avoid conflicting rules, use them in these roles:

| Step | Skill | Command | Output |
|---|---|---|---|
| 1. Design context (once, before any UI code) | **Impeccable** | `/impeccable init` (or `teach`, depending on installed version) | `frontend/PRODUCT.md`, `frontend/DESIGN.md` |
| 2. Plan each screen before coding | Impeccable | `/impeccable shape` | UX plan per feature |
| 3. Build screens | Impeccable (primary) | `/impeccable craft` | Components/pages using the DESIGN.md tokens |
| 4. Independent "does this look AI-generated?" check | **Hallmark** | `/hallmark audit ./frontend/src/app` | Punch list only (no edits) |
| 5. Fix and refine | Impeccable | `/impeccable critique`, `audit`, `harden`, `polish` | Final UI |

Rules:
- **Impeccable owns the design system** (tokens, type scale, spacing, components). Do **not** run Hallmark's default build or `redesign` on this app, because that would introduce a second visual system. Use Hallmark only for `audit` (and optionally `study` for inspiration).
- Map design tokens to Tailwind theme variables and shadcn/ui CSS variables so there's one source of truth.
- Brand direction to give Impeccable during init: **black, red and gold**. It's a church community app used by members of all ages (including students), mostly on phones. Tone: warm, encouraging, trustworthy; not corporate and not gamified. Pass it the palette in §11.1 as a fixed constraint. Impeccable may refine shades only if every contrast check in §11.1 still passes.
- Accessibility: **WCAG 2.2 AA**. Visible focus states, 44×44 px touch targets, labels on all inputs, `prefers-reduced-motion` respected.
- Every interactive component handles loading, empty, error, disabled, and success states. Empty states tell the user what to do next.
- The leader attendance screen and the member devotion screen are the two most important flows; design those first (see §11.3).

### 11.1 Color palette: black, red, gold

**Roles**
- **Black** is the base: backgrounds in dark mode; text, headers and the app bar in light mode.
- **Red** is the brand and primary-action color: primary buttons, active nav item, links, key highlights.
- **Gold** is the accent, used sparingly for meaning or celebration: streaks, achievements, "Disciple" status badge, leader/admin badges, selected-state underline, the logo mark, and small dividers. Never use gold for large fills or body text. Roughly **60% neutral / 30% black / ≤10% red + gold** on any screen.

**Tokens** (define as CSS variables in `globals.css`, wired to Tailwind `@theme` and the shadcn variable names: `--background`, `--foreground`, `--card`, `--primary`, `--accent`, `--muted`, `--border`, `--ring`, `--destructive`, etc.)

| Token | Light mode | Dark mode | Use |
|---|---|---|---|
| `--background` | `#FAF8F3` (warm off-white) | `#0B0B0C` (near-black) | Page |
| `--surface` / `--card` | `#FFFFFF` | `#161618` | Cards, sheets |
| `--surface-2` | `#F1EEE6` | `#1F1F22` | Inputs, hover rows, nested panels |
| `--border` | `#E2DED3` | `#2A2A2E` | Dividers, input borders |
| `--foreground` | `#141414` | `#F5F2EA` | Body text |
| `--muted-foreground` | `#5F5A52` | `#A39E93` | Secondary text |
| `--primary` (red) | `#B3121B` | `#C8102E` | Primary button fill |
| `--primary-foreground` | `#FFFFFF` | `#FFFFFF` | Text on primary |
| `--primary-text` (red as text/link) | `#B3121B` | `#E5484D` | Links, red text, active nav label |
| `--accent` (gold fill) | `#C9A227` | `#D4AF37` | Badges, streak chips, icons |
| `--accent-foreground` | `#0B0B0C` | `#0B0B0C` | Text on gold fills |
| `--accent-text` (gold as text/icon/border) | `#7A5C0E` | `#D4AF37` | Gold text, gold icons, gold borders |
| `--ring` (focus) | `#B3121B` | `#D4AF37` | Focus ring, 2 px + 2 px offset |
| `--destructive` | `#9F1239` (rose) | `#FB7185` | Errors and delete actions |
| `--success` | `#15803D` | `#4ADE80` | Present, saved |
| `--warning` | `#B45309` | `#FBBF24` | Late, uncertain AI score |

**Contrast (checked, WCAG 2.2 AA)**: body text ~17:1 in both modes; muted text 6.4:1 light / 6.8–7.4:1 dark; red text 6.5–7:1 light / 4.6–5:1 dark; white on red buttons 5.9–7:1; gold text 5.9–6.2:1 light / 8.6–9.4:1 dark; black on gold 9.4:1. Add an automated contrast test (a small script over the token pairs, run in CI) so later palette tweaks can't regress this.

**Rules**
- In light mode, the bright gold fill (`#C9A227`) is only 2.3:1 against the background. It must always carry dark text (`--accent-foreground`) **or** a `--accent-text` border, and must never be the only way a state is shown. Gold icons and borders on light backgrounds use `--accent-text`.
- Because the brand is red, **errors must not rely on red alone**. Use the separate `--destructive` (rose) token, always paired with an icon and a text message. Attendance status uses color **plus** a label/icon (Present ✓, Late ◷, Absent ✕, Excused –).
- AI-score bands: Likely human = `--success`, Uncertain = `--warning`, Likely AI = `--destructive`, each with a text label.
- Charts: series colors come from tokens (red, gold, black/off-white, then neutral greys), with a validated palette for each mode. Never use gold and warning amber in the same chart.

### 11.2 Light and dark mode
- Three options: **Light, Dark, System** (default: System, following `prefers-color-scheme`). Toggle in the app header and in Profile → Appearance.
- Implement with `next-themes` (class strategy on `<html>`). No flash of the wrong theme on first paint: the theme script runs before hydration, and `color-scheme` is set so native controls (scrollbars, date pickers) match.
- Persist the choice for signed-in users in their profile (`app_users.settings.theme`) so it follows them across devices; fall back to local storage for the login page.
- Every component, chart, empty-state illustration, PDF/print view and email template is checked in both modes. Report PDFs and emails always use the light palette for printing.
- Images and the logo have light and dark variants where needed (e.g. a gold-on-black and a black-on-off-white logo).
- Update `<meta name="theme-color">` per mode so the mobile browser bar and installed PWA match (`#0B0B0C` dark, `#FAF8F3` light).
- Playwright visual tests capture key screens in **both** modes.

### 11.3 Mobile responsiveness
Mobile-first: design and build at 360 px wide first, then enhance upward.

**Breakpoints** (Tailwind defaults): base < 640 (phones), `sm` 640, `md` 768 (tablets), `lg` 1024 (laptops), `xl` 1280+.

**Layout by size**
| | Phone (< 768) | Tablet (768–1023) | Desktop (≥ 1024) |
|---|---|---|---|
| Navigation | Bottom tab bar (max 5 items per role; the rest go under "More") | Collapsible side rail | Full sidebar |
| Primary action | Sticky bottom button or floating action button (e.g. "Save attendance", "Upload devotion") | Inline in header | Inline in header |
| Data tables (members, reports) | Stacked cards with the 2–3 key fields; tap for details | Table with priority columns, horizontal scroll inside the table only | Full table |
| Forms | Single column; full-screen sheet | Single column, max 640 px | Two columns where it helps |
| Dialogs | Bottom sheets (`Drawer`) | Centered dialog | Centered dialog |
| Charts | Simplified (fewer ticks, legend below), swipe between charts | Full | Full, side by side |

**Rules**
- No horizontal page scroll at any width from 320 px up. Content max width ~1280 px on large screens.
- Touch targets ≥ 44×44 px with ≥ 8 px spacing. The attendance roster rows are full-width tap targets.
- Respect safe areas (`env(safe-area-inset-*)`) for the bottom bar and sticky buttons on notched phones; use `100dvh`, not `100vh`.
- Body text ≥ 16 px on mobile (also prevents iOS zoom on input focus). Use fluid type (`clamp()`) for headings.
- Use the right input types and `inputmode` (tel, email, numeric) and `autocomplete` attributes.
- Devotion upload on mobile opens the camera or gallery directly (`accept="image/*"`, with `capture` offered as an option) and shows the image preview with compression before upload.
- Works in portrait and landscape; the attendance screen stays usable one-handed.
- Performance on mid-range Android over 4G is part of "responsive": meet the budget in §9.
- **Testing:** Playwright runs the e2e and visual tests at 360×800, 390×844, 768×1024, 1024×768 and 1440×900, in both light and dark mode. Include a manual check on a real Android and iPhone before each release.
- Language: English UI copy, written so it's easy to add Filipino/Tagalog later (use `next-intl` with message files from the start).
- PWA: installable, with an offline-tolerant attendance screen (queue marks locally and sync with idempotency keys when back online).

---

## 12. Deployment (Render + GitHub)

- `render.yaml` Blueprint at the repo root defines:
  - `timecheck-web`: Docker web service from `frontend/Dockerfile` (Next.js `output: 'standalone'`).
  - `timecheck-api`: Docker web service from `backend/Dockerfile.api`, health check `/health/ready`.
  - `timecheck-worker`: Docker background worker from `backend/Dockerfile.worker`.
  - `timecheck-redis`: Render Key Value (private network only, no public access, `maxmemory-policy` suited to cache + rate limiting).
  - Environment groups: `timecheck-shared`, `timecheck-secrets` (values set in the dashboard, never in YAML).
- Services talk over Render's private network where possible. Only `web` and `api` are public.
- **Environments:** `staging` (auto-deploy from `main`) and `production` (deploy on tagged release or manual promote). Each has its **own Supabase project** and its own Redis.
- **Database migrations:** a GitHub Actions job runs `supabase db push` (or `supabase migration up`) against staging, then production as a gated step before the app deploys. Migrations must be backward-compatible (expand → migrate → contract) so a rollback of the app doesn't break.
- Docker images: multi-stage, non-root user, pinned base image digests, `HEALTHCHECK`, no secrets in layers.
- **Branching:** trunk-based with short-lived feature branches, PRs required, branch protection on `main` (required checks from §8.6, 1 review, signed commits recommended), Conventional Commits.
- Workflows in `.github/workflows/`: `ci-frontend.yml`, `ci-backend.yml`, `ci-database.yml` (pgTAP + migration lint), `security.yml` (CodeQL, Trivy, gitleaks), `deploy.yml`. Use path filters so a frontend change doesn't rebuild the backend.

---

## 13. Testing Strategy

| Level | Tooling | Must cover |
|---|---|---|
| Domain unit | xUnit + FluentAssertions | Status transitions, attendance edit window, devotion slot rules, Sunday exclusion for Morning Prayer |
| Application | xUnit + NSubstitute | Handlers, validators, authorization rules |
| API integration | WebApplicationFactory + Testcontainers (Postgres, Redis) | Every endpoint's happy path + 401/403 + cross-group IDOR attempts + rate limiting |
| Database | pgTAP | RLS deny-all, constraints, unique keys |
| Architecture | NetArchTest | Layer dependencies, no `[AllowAnonymous]` except health |
| Frontend unit | Vitest + Testing Library | Forms, Zod schemas, components' states |
| E2E | Playwright | Login, leader marks attendance, member uploads devotion, admin promotes leader, report export |
| Accessibility | axe (Playwright) | All main pages |
| Load | k6 | Sunday peak scenario |

Coverage target: ≥ 80 % for Domain + Application. Critical security paths are 100 % covered by integration tests.

---

## 14. Build Plan (phases for Claude Code)

Work in this order. At the end of each phase: everything builds, tests pass, CI is green, and you've written a short summary in `misc/docs/progress.md`.

0. **Scaffold:** repo structure (§3), tooling configs, `CLAUDE.md`, Docker compose for local dev, CI skeleton with security gates, `render.yaml` skeleton, ADR 0001 (architecture) and 0002 (auth approach).
1. **Database foundation:** Supabase local project, core migrations (§7), RLS deny-all, seed data, pgTAP tests.
2. **Backend foundation:** Clean Architecture solution, EF Core mapping, Supabase JWT validation, role/permission policies, audit pipeline, Problem Details, rate limiting, health checks, Serilog + OpenTelemetry, OpenAPI.
3. **Design system:** run Impeccable init with the brand direction and palette in §11.1. Set up the Tailwind/shadcn tokens for both modes, the theme toggle (§11.2), the contrast test, the responsive app shell with role-based navigation (bottom bar on mobile, sidebar on desktop, §11.3), and the login pages. Run the Hallmark audit.
4. **Auth end to end:** BFF cookie flow, login, logout, password reset, MFA for admins, forced password change, consent screen.
5. **Members & groups:** registration by leader, duplicate detection, minors/guardian consent, group management, promotion/demotion with notifications, profile alteration.
6. **Events & attendance:** event types, recurring generation job, scheduling UI, leader attendance flow (mobile-first, bulk marking, offline queue), member history.
7. **Devotions & AI detection:** upload pipeline, storage, worker detection with providers behind interfaces, scoring display, reminders.
8. **Invites.**
9. **Notifications:** outbox, in-app, email, web push, preferences.
10. **Reports:** all reports in §6.8, exports, materialized views, caching.
11. **Hardening:** threat model, data inventory, encryption at rest for sensitive columns, retention jobs, data subject export/erase, load test, accessibility pass, `/impeccable audit` + `/hallmark audit` + `/impeccable polish`, runbooks.
12. **Deploy:** staging, then production via Blueprint. Smoke tests. Backup restore drill.

---

## 15. Open Questions (ask the product owner; until answered, use the default)

| # | Question | Default until answered |
|---|---|---|
| Q1 | Can one leader lead more than one cell group? Is there a leader-of-leaders hierarchy (e.g. G12-style)? | Support it in the schema (`parent_group_id`, leader can lead many groups). The v1 UI assumes one group per leader. |
| Q2 | Morning Prayer: do members self check-in (e.g. within a time window), or does the leader mark? | Leader marks. Self check-in is designed as a later toggle. |
| Q3 | Is Week of Fire per group or church-wide? Which day? | Church-wide, tracked per group, weekly on a configurable day. |
| Q4 | Exact Day/Night devotion windows and reminder times? | Day: 00:00–23:59 (reminder 07:00). Night: 18:00–05:00 next day (reminder 20:00). |
| Q5 | What happens with a high AI score: just visible, or should the leader be notified? | Visible only; leaders can filter "Likely AI". No automatic action. |
| Q6 | Which AI detection providers and budget? | Pick one text and one image provider with a no-retention/no-training API policy. Record in an ADR. Feature flag to disable. |
| Q7 | Is "grade level" school grade, or a discipleship level? | School grade level (optional field). |
| Q8 | Will other churches or branches use this? | Single church in v1. `church_id` tenancy is already in the schema. |
| Q9 | Who can see a member's devotion content: only their leader, or also Admins? | Leader + Admins. |

---

## 16. Definition of Done (every feature)
- Acceptance criteria in §6 met, with tests at the levels in §13.
- Authorization enforced on the backend and covered by a negative test.
- Inputs validated on both sides. No new high/critical vulnerabilities.
- UI handles all states, passes axe, uses only palette tokens (no hard-coded colors), and passes the visual tests at all five viewports in both light and dark mode.
- Audit entries written for state-changing actions.
- OpenAPI updated and TS client regenerated.
- Docs/ADR updated if a decision was made.
