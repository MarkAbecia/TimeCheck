begin;
create extension if not exists pgtap with schema extensions;
select plan(8);

select is_empty('select * from public.tables_without_rls()', 'every public table has RLS enabled');

-- anon / authenticated cannot read or write anything (seed data exists, so a visible row would be a leak)
set local role anon;
select throws_ok('select count(*) from public.app_users', '42501', null, 'anon cannot read app_users');
select throws_ok('select count(*) from public.devotions', '42501', null, 'anon cannot read devotions');
select throws_ok($$insert into public.audit_logs(action) values ('x')$$, '42501', null, 'anon cannot write audit_logs');
reset role;

set local role authenticated;
select throws_ok('select count(*) from public.app_users', '42501', null, 'authenticated cannot read app_users');
select throws_ok('select count(*) from public.attendance', '42501', null, 'authenticated cannot read attendance');
select throws_ok('select public.create_church(''x'')', '42501', null, 'authenticated cannot call create_church');
reset role;

select is(
  (select count(*)::int from storage.buckets where id in ('devotions','avatars','exports') and public),
  0, 'storage buckets are private');

select * from finish();
rollback;
