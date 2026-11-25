# Rotate secrets

All secrets live in Render's `timecheck-secrets` group (and GitHub Actions secrets for CI). Rotate on a schedule (yearly),
when someone with access leaves, and at once after a suspected leak. Record each rotation in the audit/incident log.

| Secret | Where | How to rotate | Impact |
|---|---|---|---|
| Database password (`timecheck_api` role) | `ConnectionStrings__*` | See `database-roles.md`: new role, update Render, deploy, drop the old role | None if done in that order |
| Supabase service role key | `Auth__ServiceRoleKey` | Supabase dashboard → API → roll the key; update Render; redeploy API and Worker | Sign-in admin calls fail between roll and redeploy (minutes) |
| JWT signing keys | Supabase Auth | Supabase rotates asymmetric keys with overlap; the API reads JWKS, nothing to change | Sessions survive |
| Field encryption key | `Encryption__Keys__{id}`, `Encryption__ActiveKeyId` | Add `Encryption__Keys__2`, set `ActiveKeyId=2`, deploy. New writes use key 2; rows written under key 1 still read with it | **Keep key 1 configured.** There is no re-encryption job yet, so removing key 1 makes old rows unreadable |
| Blind index key | `Encryption__BlindIndexKey` | Don't rotate without a job that recomputes `mobile_bidx` and `name_birthdate_bidx` for every user (not built) | Duplicate detection and search break for existing rows |
| VAPID keys (web push) | `Notifications__Push__*` | Generate a new pair, deploy | Every browser must subscribe again (users turn push back on) |
| Email provider key | `Notifications__Email__*` | Provider dashboard; update Render; redeploy Worker | Email retries cover the gap |
| AI detection keys | detection config | Provider dashboard; update Render; redeploy Worker | Pending detections retry |

Losing the field encryption key loses the encrypted data. Keep a copy of every encryption key in the church's password
manager, separate from database backups.

After a suspected leak of the service role key or the database password: rotate first, then check the audit log and Supabase
logs for activity since the leak (see `../security/incident-response.md`).
