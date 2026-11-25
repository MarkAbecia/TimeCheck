# Restore a backup

Production uses Supabase PITR (§8.2). The restore drill is part of Phase 12; record its date and duration here when done.

## When
- Data loss or corruption that a forward SQL fix can't repair.
- Never to undo a bad deploy alone; roll the app back first (`deploy-and-rollback.md`).

## Steps
1. Stop the Worker in Render, so jobs don't write during the restore. Put a notice up for leaders (expect attendance to be
   re-marked for the lost window).
2. Pick the restore point: just before the incident, from the audit log (`occurred_at` of the first bad action) and logs.
   Then save the people erased since that point, because the restore removes these audit rows too:
   `select entity_id, occurred_at from audit_logs where action = 'member.erase' and occurred_at > '<restore point>';`
3. Supabase dashboard → Database → Backups → Point in time → restore to that time. Supabase restores in place; the project
   is unavailable while it runs.
4. Check: `select max(occurred_at) from audit_logs;` is before the incident; row counts for `attendance`, `app_users`,
   `devotions` look right.
5. **Re-apply erasures.** Everyone on the list saved in step 2 is back. Erase them again from the admin screen (deactivate,
   then erase).
6. Storage is not part of PITR. Devotion photos uploaded after the restore point remain in the bucket without a row; export files
   expire on their own.
7. Flush the report cache and user cache: `FLUSHDB` on the production Redis (only cache and rate-limit keys live there).
8. Start the Worker. Smoke test as in the deploy runbook.

## Drill (every 6 months)
Restore production's backup into a scratch Supabase project, run the step 4 checks there, and note the time taken.
