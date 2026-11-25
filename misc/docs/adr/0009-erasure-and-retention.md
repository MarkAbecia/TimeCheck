# 0009 — Data-subject erasure and retention

Status: Accepted (2026-11-25).

**Decision: erasing a person anonymizes their row instead of deleting it. Attendance and history stay, counted under
"Removed member". Retention runs nightly from church settings, and audit rows can only be purged through a database
function that keeps at least a year.**

Erasure (`POST /members/{id}/erase`, admins, audited as `member.erase`):
- Only a deactivated person can be erased, so the irreversible step always follows a reversible one.
- Removed: names (become "Removed member"), nickname, grade level, mobile, address, guardian details, email (replaced with a
  unique placeholder so the address can be registered again), settings, devotion text and photos, notifications, push
  subscriptions, their report exports and files, and the Supabase sign-in account.
- Kept: the row id, gender and birth year (for gender and age aggregates), role and status history, group membership dates,
  attendance marks and the devotion days (content emptied), so totals in past reports don't change.
- The audit row says only that the erasure happened.
- Files and the sign-in account are deleted after the database commit, best effort. A failure leaves an orphaned file or a
  sign-in that can't reach any data (the app user is gone), never personal data in the database.

Export (`GET /members/{id}/data-export`, admins, audited as `member.data_export`): one JSON document with the profile
(decrypted), consent records, group history, attendance, devotions, invites made, role and status changes and notifications.
JSON only; the PDF form in §8.4 is left out because a person's copy is rarely printed and JSON covers the right of access.

Retention (Worker `retention`, 02:45 Manila time), per church `settings.retention`:
- `deactivatedUserYears` (default 3, at least 1): people deactivated longer than this are erased as above.
- `auditLogDays` (default 400, never under 365): older audit rows are deleted through `purge_audit_logs`, a `security
  definer` function. The app role still can't delete audit rows directly, and the function refuses anything under a year.
- Guest invites keep their own purge (`purge-invites`, 90 days, §6.5).

Consequences:
- Backups keep erased data until they age out; a restore must re-apply erasures (runbook `restore-backup.md`).
- No settings screen for retention yet; values are changed in `churches.settings`.
