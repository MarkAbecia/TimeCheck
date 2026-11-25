# Data inventory (RA 10173, REQUIREMENTS §8.4)

Every piece of personal data TimeCheck stores: why, who can see it, how it's protected, and when it goes.
"Leader" means the person's own cell leader. Admins include Super Admins. Members always see their own data.
Encrypted = AES-256-GCM in the app (ADR 0003); the database only ever sees ciphertext.

## People (`app_users`)

| Field | Purpose | Who sees it | Protection | Retention |
|---|---|---|---|---|
| first name, last name, nickname | Identify the person in rosters and reports | Leader, admins; names shown to their group in attendance | Plain | Until erased |
| gender | Reports (counts by gender) | Leader, admins | Plain | Kept after erasure (aggregates) |
| birthdate | Minor check, age-bracket reports, duplicate check | Leader, admins | Encrypted + HMAC blind index (with name) | Erasure keeps birth year only |
| grade level (optional) | Student grouping | Leader, admins | Plain | Until erased |
| mobile (optional) | Contact, duplicate check | Leader, admins | Encrypted + HMAC blind index | Until erased |
| email | Sign-in, notifications | Leader, admins | Plain (needed for sign-in); unique per church | Replaced on erasure |
| address (optional) | Pastoral visits | Leader, admins | Encrypted | Until erased |
| guardian name, guardian contact, consent time | Minors only: guardian consent (§8.4) | Leader, admins | Contact encrypted | Until erased |
| privacy consent version and time | Proof of consent | Admins | Plain | Until erased |
| settings (notification choices) | Preferences | The person | Plain JSON | Cleared on erasure |
| role, status, joined on, group memberships | Running the church's cell groups | Leader, admins | Plain | Kept after erasure (history) |

Minors' records are visible only to their leader and admins. Reports show minors by name only to the same people.

## Activity

| Data | Purpose | Who sees it | Protection | Retention |
|---|---|---|---|---|
| Attendance marks (`attendance`) | Attendance monitoring and reports | The person, leader, admins | Plain | Kept; after erasure counted under "Removed member" |
| Devotion text and photo (`devotions`, bucket `devotions`) | Devotion tracking (§6.3) | The person, leader, admins (Q9) | Private bucket, signed links (minutes); photos re-encoded, EXIF stripped | Content removed on erasure; the day stays counted |
| AI detection scores | Flag possibly generated devotions | Leader, admins | Content sent only to providers with no-retention terms (ADR 0006) | With the devotion |
| Invites: guest name and contact (`invites`) | Follow up guests | The inviter, their leader, admins | Contact encrypted | Erased 90 days after the event unless the guest joins |
| Role and status changes | Accountability | Admins | Append-only | Kept (ids and reasons only) |
| Notifications, push subscriptions | Reminders and alerts | The person | Push endpoints are browser-issued URLs | Deleted on erasure; gone subscriptions removed |
| Report exports (bucket `exports`) | Downloaded reports | The requester | Private bucket, signed links (≤ 10 min) | Deleted after 24 hours |

## Logs

| Data | Purpose | Who sees it | Protection | Retention |
|---|---|---|---|---|
| Audit log (`audit_logs`) | Who did what (§8.5) | Super Admins | Append-only for the app; IP stored as a hash; no passwords, tokens or devotion content | `retention.auditLogDays` (default 400, never under 365) |
| Application logs (Serilog → stdout) | Troubleshooting | Operators | PII masked; tokens, passwords and devotion content never logged | The host's log retention |

## Erasure and retention

- **Data export:** an admin downloads one person's data as JSON (`GET /members/{id}/data-export`). Audited.
- **Erasure:** an admin deactivates the person, then erases them (`POST /members/{id}/erase`). Names, contact details,
  address, guardian details, devotion content, notifications, exports and the sign-in account are removed. Attendance and
  group history stay, under "Removed member", with gender and birth year for aggregates. Audited without personal details.
- **Retention job** (nightly, `retention`): erases people deactivated longer than `retention.deactivatedUserYears`
  (default 3) and deletes audit rows older than `retention.auditLogDays`.
- **Backups** keep erased data until they age out of the backup window (Supabase PITR). Restores must re-run erasures
  recorded in the audit log since the backup (see the restore runbook).
