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

Production bootstrap (once):
1. SQL editor: `select public.create_church('<Church name>', 'Asia/Manila');`
2. Once the API service is deployed, open its **Shell** in Render and create the first Super Admin (their real details;
   the birthdate is encrypted with this environment's keys, which is why plain SQL can't do this):
   ```
   dotnet TimeCheck.Api.dll create-super-admin --email <email> --first-name <name> --last-name <name> --gender MALE|FEMALE --birthdate yyyy-MM-dd
   ```
   Add `--church "<name>"` if the database has more than one church. Supabase emails them an invite (custom SMTP must be
   set up); they set a password from the link, accept the privacy notice and enrol a sign-in code. The command refuses
   once the church has a Super Admin, and it is audited as `member.first_super_admin`. Further admins are added in the app.

Rotate: create `timecheck_api_v2` with a new password, update Render, deploy, then `drop role timecheck_api`.
