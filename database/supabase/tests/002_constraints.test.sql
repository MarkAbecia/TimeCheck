begin;
create extension if not exists pgtap with schema extensions;
select plan(9);

-- create_church runs in its own statement so the follow-up lookups can see its rows.
create temp table ctx as select public.create_church('Test Church') as church_id, null::uuid as mp_type, null::uuid as cc_type, null::uuid as user_id;
update ctx set
  mp_type = (select id from public.event_types et where et.church_id = ctx.church_id and code = 'MORNING_PRAYER'),
  cc_type = (select id from public.event_types et where et.church_id = ctx.church_id and code = 'CELL_CELEBRATION');
with u as (
  insert into public.app_users (church_id, first_name, last_name, gender, birthdate_enc, name_birthdate_bidx, email)
  select church_id, 'Adult', 'Test', 'FEMALE', 'v1:x', 'x', 'adult@example.test' from ctx returning id)
update ctx set user_id = (select id from u);

select is((select count(*)::int from public.event_types where church_id = (select church_id from ctx)), 13, 'create_church seeds 13 system event types');

-- Morning Prayer never on Sunday (2026-11-29 is a Sunday)
select throws_ok($$
  insert into public.events (church_id, event_type_id, title, starts_at, ends_at, occurs_on, is_generated)
  select church_id, mp_type, 'MP', '2026-11-28T21:00Z', '2026-11-28T22:00Z', '2026-11-29', true from ctx $$,
  '23514', null, 'Morning Prayer on Sunday is rejected');
select lives_ok($$
  insert into public.events (church_id, event_type_id, title, starts_at, ends_at, occurs_on, is_generated)
  select church_id, mp_type, 'MP', '2026-11-29T21:00Z', '2026-11-29T22:00Z', '2026-11-30', true from ctx $$,
  'Morning Prayer on Monday is allowed');

-- Generated events are idempotent, also when group_id is null (church-wide)
insert into public.events (church_id, event_type_id, title, starts_at, ends_at, occurs_on, is_generated)
select church_id, cc_type, 'CC', '2026-11-29T01:00Z', '2026-11-29T04:00Z', '2026-11-29', true from ctx;
select throws_ok($$
  insert into public.events (church_id, event_type_id, title, starts_at, ends_at, occurs_on, is_generated)
  select church_id, cc_type, 'CC', '2026-11-29T01:00Z', '2026-11-29T04:00Z', '2026-11-29', true from ctx $$,
  '23505', null, 'duplicate generated church-wide event is rejected');

select throws_ok($$
  insert into public.events (church_id, event_type_id, title, starts_at, ends_at, occurs_on)
  select church_id, cc_type, 'Bad', '2026-11-29T04:00Z', '2026-11-29T01:00Z', '2026-11-29' from ctx $$,
  '23514', null, 'event must end after it starts');

-- One attendance row per (event, user)
insert into public.attendance (church_id, event_id, user_id, status)
select ctx.church_id, e.id, ctx.user_id, 'PRESENT' from ctx join public.events e on e.title = 'CC' and e.church_id = ctx.church_id;
select throws_ok($$
  insert into public.attendance (church_id, event_id, user_id, status)
  select ctx.church_id, e.id, ctx.user_id, 'LATE' from ctx join public.events e on e.title = 'CC' and e.church_id = ctx.church_id $$,
  '23505', null, 'duplicate attendance is rejected');

-- One devotion per user/day/slot
insert into public.devotions (church_id, user_id, devotion_date, slot, text_content, content_hash)
select church_id, user_id, '2026-11-24', 'DAY', 'text', repeat('a', 64) from ctx;
select throws_ok($$
  insert into public.devotions (church_id, user_id, devotion_date, slot, text_content, content_hash)
  select church_id, user_id, '2026-11-24', 'DAY', 'again', repeat('b', 64) from ctx $$,
  '23505', null, 'second DAY devotion on the same date is rejected');

-- Audit log is append-only
insert into public.audit_logs (church_id, action) select church_id, 'test' from ctx;
select throws_ok($$update public.audit_logs set action = 'tampered' where action = 'test'$$,
  'P0001', 'audit_logs is append-only', 'audit_logs cannot be updated');

-- Minors need guardian + consent
select throws_ok($$
  insert into public.app_users (church_id, first_name, last_name, gender, birthdate_enc, name_birthdate_bidx, email, is_minor)
  select church_id, 'Kid', 'Test', 'MALE', 'v1:x', 'x', 'kid@example.test', true from ctx $$,
  '23514', null, 'minor without guardian consent is rejected');

select * from finish();
rollback;
