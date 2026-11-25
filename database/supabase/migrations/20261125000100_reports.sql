-- Reports (REQUIREMENTS §6.8). One definition of "who was expected at an event, and how it went", so every report counts the
-- same way and equals the raw rows (§6.8 AC). A plain view, not materialized: answers stay exact, and the API caches report
-- results in Redis per church until the next write (ADR 0008).
--
-- Expected at a started, uncancelled event:
--   group event          → the group's members on that day
--   church-wide, audience → members of the audience groups on that day, plus people named in it
--   church-wide, none     → everyone in the church who had joined and wasn't deactivated by then
--   anyone marked at it  → always (a guest leader, someone moved since)
-- Unmarked counts as absent; excused doesn't count (§5.2). group_id is the person's group on that day.
-- A membership's left_on is the day they moved: from then on they belong to the new group.

-- The branches never overlap, so no de-duplicating sort over a year of rows (~0.4 s for 1,000 members, ADR 0008).
-- Each carries church_id and occurs_on, and ev is inlined, so a report's filters reach every branch.
create view public.attendance_facts with (security_invoker = true) as
with ev as not materialized (
  select e.id, e.church_id, e.event_type_id, e.group_id, e.occurs_on,
         exists (select 1 from public.event_audiences x where x.event_id = e.id) as has_audience
  from public.events e
  where e.cancelled_at is null and e.starts_at <= now()
),
expected as (
  -- group event: the group's members that day
  select ev.church_id, ev.id as event_id, ev.event_type_id, ev.occurs_on, m.user_id, m.group_id
  from ev join public.group_memberships m on m.group_id = ev.group_id and m.joined_on <= ev.occurs_on and (m.left_on is null or m.left_on > ev.occurs_on)
  where ev.group_id is not null
  union all
  -- church-wide, no audience: everyone, with the group they were in that day
  select ev.church_id, ev.id, ev.event_type_id, ev.occurs_on, u.id, m.group_id
  from ev join public.app_users u on u.church_id = ev.church_id and u.deleted_at is null and u.joined_on <= ev.occurs_on and (u.deactivated_at is null or u.deactivated_at::date > ev.occurs_on)
          left join public.group_memberships m on m.user_id = u.id and m.joined_on <= ev.occurs_on and (m.left_on is null or m.left_on > ev.occurs_on)
  where ev.group_id is null and not ev.has_audience
  union all
  -- church-wide with an audience (rare): its groups' members that day, and the people it names
  select aud.church_id, aud.event_id, aud.event_type_id, aud.occurs_on, aud.user_id,
         (select m.group_id from public.group_memberships m
           where m.user_id = aud.user_id and m.joined_on <= aud.occurs_on and (m.left_on is null or m.left_on > aud.occurs_on)
           order by m.joined_on desc limit 1)
  from (
    select ev.church_id, ev.id as event_id, ev.event_type_id, ev.occurs_on, m.user_id
    from ev join public.event_audiences x on x.event_id = ev.id and x.group_id is not null
            join public.group_memberships m on m.group_id = x.group_id and m.joined_on <= ev.occurs_on and (m.left_on is null or m.left_on > ev.occurs_on)
    where ev.group_id is null
    union
    select ev.church_id, ev.id, ev.event_type_id, ev.occurs_on, x.user_id
    from ev join public.event_audiences x on x.event_id = ev.id and x.user_id is not null
    where ev.group_id is null
  ) aud
  union all
  -- marked, but not expected by the rules above (a visitor from another group, someone since moved): one branch per kind
  select ev.church_id, ev.id, ev.event_type_id, ev.occurs_on, a.user_id, (select m.group_id from public.group_memberships m where m.user_id = a.user_id and m.joined_on <= ev.occurs_on and (m.left_on is null or m.left_on > ev.occurs_on)
          order by m.joined_on desc limit 1)
  from public.attendance a join ev on ev.id = a.event_id
  where ev.group_id is not null
    and not exists (select 1 from public.group_memberships m where m.group_id = ev.group_id and m.user_id = a.user_id and m.joined_on <= ev.occurs_on and (m.left_on is null or m.left_on > ev.occurs_on))
  union all
  select ev.church_id, ev.id, ev.event_type_id, ev.occurs_on, a.user_id, (select m.group_id from public.group_memberships m where m.user_id = a.user_id and m.joined_on <= ev.occurs_on and (m.left_on is null or m.left_on > ev.occurs_on)
          order by m.joined_on desc limit 1)
  from public.attendance a join ev on ev.id = a.event_id
  where ev.group_id is null and not ev.has_audience
    and not exists (select 1 from public.app_users u where u.id = a.user_id and u.church_id = ev.church_id and u.deleted_at is null and u.joined_on <= ev.occurs_on and (u.deactivated_at is null or u.deactivated_at::date > ev.occurs_on))
  union all
  select ev.church_id, ev.id, ev.event_type_id, ev.occurs_on, a.user_id, (select m.group_id from public.group_memberships m where m.user_id = a.user_id and m.joined_on <= ev.occurs_on and (m.left_on is null or m.left_on > ev.occurs_on)
          order by m.joined_on desc limit 1)
  from public.attendance a join ev on ev.id = a.event_id
  where ev.group_id is null and ev.has_audience
    and not exists (select 1 from public.event_audiences x where x.event_id = ev.id and x.user_id = a.user_id)
    and not exists (select 1 from public.event_audiences x join public.group_memberships m on m.group_id = x.group_id
                    where x.event_id = ev.id and m.user_id = a.user_id and m.joined_on <= ev.occurs_on and (m.left_on is null or m.left_on > ev.occurs_on))
)
select x.church_id, x.event_id, x.event_type_id, x.occurs_on, x.user_id, x.group_id,
       coalesce(a.status, 'UNMARKED'::public.attendance_status) as status
from expected x
left join public.attendance a on a.event_id = x.event_id and a.user_id = x.user_id;

revoke all on public.attendance_facts from anon, authenticated;
grant select on public.attendance_facts to timecheck_app;

-- What the view and the reports filter on.
create index events_church_occurs_idx on public.events (church_id, occurs_on) where cancelled_at is null;
create index group_memberships_user_idx on public.group_memberships (user_id, joined_on);
create index report_jobs_queued_idx on public.report_jobs (created_at) where status = 'QUEUED';
create index report_jobs_expiry_idx on public.report_jobs (expires_at) where status = 'DONE';
