-- Security (REQUIREMENTS §8.2): RLS deny-all for anon/authenticated, least-privilege app role, private buckets.

-- ---------- application role ----------
-- NOLOGIN group role. Each environment creates its own LOGIN user that is a member of it
-- (see misc/docs/runbooks/database-roles.md). Never connect the API as postgres.
do $$
begin
  if not exists (select 1 from pg_roles where rolname = 'timecheck_app') then
    create role timecheck_app nologin;
  end if;
end $$;

grant usage on schema public to timecheck_app;
grant usage on schema extensions to timecheck_app;
grant usage on all sequences in schema public to timecheck_app;

grant select, insert, update, delete on all tables in schema public to timecheck_app;
-- Audit log: append-only for the app.
revoke update, delete, truncate on public.audit_logs from timecheck_app;
-- History tables: never rewritten by the app.
revoke update, delete on public.role_changes, public.member_status_changes from timecheck_app;

-- Hangfire stores its job tables in its own schema.
create schema if not exists hangfire;
grant usage, create on schema hangfire to timecheck_app;

-- ---------- lock out the Data API roles ----------
revoke all on all tables in schema public from anon, authenticated;
revoke all on all sequences in schema public from anon, authenticated;
revoke all on all functions in schema public from anon, authenticated, public;
alter default privileges in schema public revoke all on tables from anon, authenticated;
alter default privileges in schema public revoke all on sequences from anon, authenticated;
alter default privileges in schema public revoke all on functions from anon, authenticated, public;
alter default privileges in schema public grant select, insert, update, delete on tables to timecheck_app;

-- ---------- RLS on every table ----------
-- No policies for anon/authenticated => deny-all. timecheck_app gets a permissive policy;
-- church scoping is enforced in the API (global query filter on church_id).
do $$
declare t record;
begin
  for t in select tablename from pg_tables where schemaname = 'public' loop
    execute format('alter table public.%I enable row level security', t.tablename);
    execute format('alter table public.%I force row level security', t.tablename);
    execute format('create policy app_all on public.%I for all to timecheck_app using (true) with check (true)', t.tablename);
  end loop;
end $$;

-- Audit log: insert + read only, even through RLS.
drop policy app_all on public.audit_logs;
create policy app_insert on public.audit_logs for insert to timecheck_app with check (true);
create policy app_select on public.audit_logs for select to timecheck_app using (true);

-- Event triggers can forget new tables; this check fails CI (pgTAP) if any table lacks RLS.
create function public.tables_without_rls() returns setof text
language sql stable as $$
  select c.relname::text from pg_class c join pg_namespace n on n.oid = c.relnamespace
  where n.nspname = 'public' and c.relkind = 'r' and not c.relrowsecurity
$$;

-- ---------- storage ----------
insert into storage.buckets (id, name, public, file_size_limit, allowed_mime_types) values
  ('devotions', 'devotions', false, 10485760, array['image/jpeg','image/png','image/webp']),
  ('avatars',   'avatars',   false,  5242880, array['image/jpeg','image/png','image/webp']),
  ('exports',   'exports',   false, 52428800, array['text/csv','application/pdf','application/vnd.openxmlformats-officedocument.spreadsheetml.sheet'])
on conflict (id) do update set public = false;
-- No storage.objects policies for anon/authenticated: only the backend (service role) reads/writes,
-- and clients get short-lived signed URLs.
