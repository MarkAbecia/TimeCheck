import { execFileSync } from "node:child_process";
import { ADMIN, E2E_DOMAIN } from "./helpers";

// Removes the people the specs registered (only @e2e.timecheck.test), the groups ("E2E " names) and events they made ("E2E " titles), devotions they sent ("E2E " text, photo already removed) and guests invited to those events, with everything that points at them.
// shortcut: goes through `docker exec` into the local Supabase db container; use a direct connection when e2e runs in CI.
const SQL = `
begin;
create temp table gone as
  select a.id, a.auth_user_id from public.app_users a join auth.users u on u.id = a.auth_user_id where u.email like '%@${E2E_DOMAIN}'
  -- Erasure replaces the email and drops the sign-in, so people the admin spec erased are found by their placeholder.
  union select id, auth_user_id from public.app_users where email like 'erased-%@erased.invalid';
-- Guests logged for E2E events, or converted into E2E people.
delete from public.audit_logs where entity_type = 'invite' and entity_id in (select i.id::text from public.invites i join public.events e on e.id = i.event_id where e.title like 'E2E %');
delete from public.invites where event_id in (select id from public.events where title like 'E2E %') or converted_user_id in (select id from gone);
delete from public.group_memberships where user_id in (select id from gone);
delete from public.member_status_changes where user_id in (select id from gone);
delete from public.role_changes where user_id in (select id from gone);
delete from public.notifications where user_id in (select id from gone);
delete from public.attendance where user_id in (select id from gone);
delete from public.audit_logs where actor_id in (select id from gone) or entity_id in (select id::text from gone);
delete from public.app_users where id in (select id from gone);
delete from auth.users where id in (select auth_user_id from gone) or email like '%@${E2E_DOMAIN}';
create temp table e2e_events as select id from public.events where title like 'E2E %';
delete from public.attendance where event_id in (select id from e2e_events);
delete from public.event_audiences where event_id in (select id from e2e_events);
delete from public.notifications where payload->>'title' like 'E2E %';
delete from public.audit_logs where entity_type = 'event' and entity_id in (select id::text from e2e_events);
delete from public.events where id in (select id from e2e_events);
delete from public.audit_logs where entity_type = 'devotion' and entity_id in (select id::text from public.devotions where text_content like 'E2E %');
delete from public.devotions where text_content like 'E2E %' and image_path is null;
-- Groups the groups spec created ("E2E " names); their people are already gone above.
create temp table e2e_groups as select id from public.cell_groups where name like 'E2E %';
delete from public.group_memberships where group_id in (select id from e2e_groups);
delete from public.audit_logs where entity_type = 'cell_group' and entity_id in (select id::text from e2e_groups);
delete from public.cell_groups where id in (select id from e2e_groups);
-- The admin spec enrols a throwaway authenticator; the seed has none.
delete from auth.mfa_factors where user_id = (select id from auth.users where email = '${ADMIN}');
commit;`;

export default function teardown() {
  const container = process.env.E2E_DB_CONTAINER ?? "supabase_db_timecheck";
  try {
    execFileSync("docker", ["exec", "-i", container, "psql", "-U", "postgres", "-v", "ON_ERROR_STOP=1", "-q"], {
      input: SQL,
      stdio: ["pipe", "ignore", "inherit"],
    });
  } catch (e) {
    console.warn(`e2e teardown could not remove test users: ${(e as Error).message}`);
  }
}
