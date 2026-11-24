# Database roles (per environment)

The API and Worker never connect as `postgres`. Migrations create the NOLOGIN group role `timecheck_app`
(table DML, no UPDATE/DELETE on `audit_logs`, `role_changes`, `member_status_changes`).

Once per Supabase project (SQL editor, as `postgres`):

```sql
create role timecheck_api login password '<generate 32+ random chars>' in role timecheck_app;
```

Store the password only in Render's `timecheck-secrets` group.

Connection strings:
- API (`ConnectionStrings__Default`): Supavisor **transaction** mode (port 6543), add `Max Auto Prepare=0;No Reset On Close=true`.
- Worker (`ConnectionStrings__Default` and `ConnectionStrings__Hangfire`): **session** mode (port 5432) or direct.

Production bootstrap (once): `select public.create_church('<Church name>', 'Asia/Manila');`
then create the first Super Admin through the API's bootstrap command (Phase 4).

Rotate: create `timecheck_api_v2` with a new password, update Render, deploy, then `drop role timecheck_api`.
