# Threat model

Scope: TimeCheck v1 (REQUIREMENTS §8). Reviewed 2026-11-25 at the end of Phase 11. Method: STRIDE over each trust boundary.
Status: **mitigated** (control in place and tested), **partial**, or **open** (accepted for now, with an owner decision or a later phase).

## System and trust boundaries

```
Phone browser ──(1)── Next.js (BFF, CSP, cookies) ──(2)── .NET API ──(3)── Postgres (Supabase, RLS)
                                │                          │  └──────(4)── Redis (cache, rate limits)
                                └──(5)── Supabase Auth     ├──(6)── Supabase Storage (private buckets)
                                                            Worker ──(7)── AI detection, email, web push
```

1. Browser ↔ BFF: the internet. Session tokens live in httpOnly, SameSite cookies; the browser never holds a JWT.
2. BFF ↔ API: server to server, bearer JWT. Only listed resource roots are proxied.
3. API/Worker ↔ Postgres: least-privilege role `timecheck_app`; RLS denies `anon` and `authenticated`.
4. Redis: user cache (60 s), rate-limit counters, report cache. No secrets, no PII beyond ids and roles.
5. Supabase Auth: password, reset and MFA flows. The service role key is only in the API and Worker.
6. Storage: private buckets `devotions`, `exports`; signed links measured in minutes.
7. Third parties: AI detection (content, no-retention providers only), email, push services (encrypted payloads).

## Assets
Member PII (more sensitive for minors), devotion content, attendance history, admin accounts, the encryption and HMAC keys,
the Supabase service role key, the audit log.

## Threats

| # | Threat (STRIDE) | Where | Controls | Status |
|---|---|---|---|---|
| S1 | Stolen password used to sign in (S) | 1, 5 | Login rate limit 5/min per account and per IP; MFA required for admins (aal2 policy); audit of failures | Mitigated. Alerts on repeated failures are open (Phase 12 monitoring) |
| S2 | Forged or replayed JWT (S) | 2 | Signature via Supabase JWKS, issuer, audience and expiry checked; role read from `app_users`, never from claims | Mitigated (ADR 0002, tests) |
| S3 | Deactivated or erased user keeps access (S, E) | 2 | User cache evicted on deactivation; deactivated or deleted users fail authorization; erasure deletes the auth account | Mitigated; up to 60 s if Redis eviction fails |
| T1 | CSRF on cookie-authenticated mutations (T) | 1 | SameSite cookies, Origin check on every non-GET BFF route | Mitigated |
| T2 | Attendance or devotion records changed by the wrong person (T, E) | 2, 3 | Resource checks on every request (leader → own groups only), 404 for out-of-reach ids; IDOR integration tests | Mitigated |
| T3 | Audit log tampering (T, R) | 3 | App role can't update or delete `audit_logs` (grants + RLS + trigger); purge only via `purge_audit_logs`, which refuses rows under a year | Mitigated |
| T4 | Malicious upload (polyglot, script, huge file) (T, D) | 1, 6 | Magic-byte allow-list, 10 MB cap at BFF and API, images re-encoded (EXIF stripped), random keys | Partial: the malware-scanning hook in §8.3 doesn't exist yet |
| T5 | Formula injection in exported reports (T) | 6 | CSV cells starting `= + - @` tab or CR get a leading `'`; XLSX uses inline strings, never formulas | Mitigated (unit tests) |
| R1 | An admin denies an action (R) | 3 | One audit row per state change with actor, IP hash, user agent, correlation id | Mitigated |
| I1 | PII read straight from the database through a leaked anon key (I) | 3 | RLS deny-all for `anon`/`authenticated` (pgTAP); sensitive columns encrypted in the app | Mitigated |
| I2 | Database backup or dump leaked (I) | 3 | Birthdate, mobile, address, guardian and guest contact are ciphertext; keys stored apart from backups | Partial: names and emails are plain |
| I3 | Signed link shared or leaked (I) | 6 | Links last minutes (devotion photos, exports ≤ 10 min); exports issued to the requester only and deleted after 24 h | Mitigated |
| I4 | PII in logs (I) | all | Serilog masking policy in the API and the Worker; tokens, passwords, devotion content never logged | Mitigated |
| I5 | Cross-church data leak (I) | 2, 3 | Global church filter; Worker jobs filter by church explicitly; single church in v1 (Q8) | Mitigated for v1 |
| I6 | Devotion content kept by an AI provider (I) | 7 | Providers chosen for no-retention terms; feature flag off by default (ADR 0006) | Open until the provider is chosen (Q6) |
| D1 | Login, export or upload flooding (D) | 1, 2 | Redis sliding-window limits: login, reset, registration, uploads, exports (10/h); 429 with Retry-After | Mitigated |
| D2 | Expensive report queries (D) | 3 | 366-day cap, cached results, large exports queued to the Worker; year-long report ≈ 0.6 s at 1,000 members | Mitigated |
| E1 | Leader acts as admin (E) | 2 | Policy per endpoint (`Leader`, `Admin` + aal2); architecture test that every endpoint requires auth | Mitigated |
| E2 | Service role key exposure (E) | 5 | Key only in API/Worker env (user-secrets locally); never in the frontend bundle or repo; gitleaks in CI | Mitigated |

## Follow-ups
- S1: alerting on repeated login failures and failed jobs (Phase 12, with the production log sink).
- T4: add the pluggable malware-scanning hook (§8.3) and a scanner, or accept the risk in an ADR.
- I6: pick the AI providers (Q6) and record their retention terms.
