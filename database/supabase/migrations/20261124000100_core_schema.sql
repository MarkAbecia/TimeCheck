-- TimeCheck core schema (REQUIREMENTS §7).
-- Sensitive PII columns (*_enc) hold application-level ciphertext "v{keyId}:{base64}" (§8.4, ADR 0003).
-- *_bidx columns hold HMAC blind indexes used for dedup/search.

create extension if not exists citext with schema extensions;

-- ---------- enums ----------
create type public.user_role as enum ('SUPER_ADMIN', 'ADMIN', 'LEADER', 'MEMBER');
create type public.member_status as enum ('NEW_MEMBER', 'DISCIPLE', 'INCONSISTENT', 'INACTIVE');
create type public.gender as enum ('MALE', 'FEMALE');
create type public.event_category as enum ('DAILY', 'WEEKLY', 'LEADER_SCHEDULED', 'ADMIN_SCHEDULED');
create type public.event_cadence as enum ('DAILY', 'DAILY_MON_SAT', 'WEEKLY', 'AD_HOC', 'SCHEDULED');
create type public.event_scope as enum ('INDIVIDUAL', 'GROUP', 'CHURCH_WIDE');
create type public.attendance_status as enum ('PRESENT', 'LATE', 'ABSENT', 'EXCUSED', 'UNMARKED');
create type public.devotion_slot as enum ('DAY', 'NIGHT');
create type public.ai_status as enum ('PENDING', 'DONE', 'FAILED', 'SKIPPED');
create type public.invite_status as enum ('EXPECTED', 'CAME', 'DID_NOT_COME');
create type public.report_job_status as enum ('QUEUED', 'RUNNING', 'DONE', 'FAILED', 'EXPIRED');

-- ---------- helpers ----------
create function public.set_updated_at() returns trigger
language plpgsql as $$
begin
  new.updated_at := now();
  return new;
end $$;

-- ---------- tables ----------
create table public.churches (
  id          uuid primary key default gen_random_uuid(),
  name        text not null check (char_length(name) between 1 and 200),
  timezone    text not null default 'Asia/Manila',
  settings    jsonb not null default '{}'::jsonb,
  created_at  timestamptz not null default now(),
  updated_at  timestamptz not null default now()
);

create table public.app_users (
  id                    uuid primary key default gen_random_uuid(),
  church_id             uuid not null references public.churches(id),
  auth_user_id          uuid unique references auth.users(id) on delete set null,
  role                  public.user_role not null default 'MEMBER',
  first_name            text not null check (char_length(first_name) between 1 and 100),
  last_name             text not null check (char_length(last_name) between 1 and 100),
  nickname              text check (char_length(nickname) <= 100),
  gender                public.gender not null,
  birthdate_enc         text not null,
  name_birthdate_bidx   text not null,
  grade_level           text check (char_length(grade_level) <= 50),
  mobile_enc            text,
  mobile_bidx           text,
  email                 extensions.citext not null check (char_length(email) <= 320),
  address_enc           text,
  status                public.member_status not null default 'NEW_MEMBER',
  joined_on             date not null default current_date,
  photo_path            text,
  is_minor              boolean not null default false,  -- refreshed by the app on save and by a daily job (birthdate is encrypted)
  guardian_name         text check (char_length(guardian_name) <= 200),
  guardian_contact_enc  text,
  guardian_consent_at   timestamptz,
  settings              jsonb not null default '{}'::jsonb,
  privacy_consent_version text,
  privacy_consent_at    timestamptz,
  must_change_password  boolean not null default true,
  deactivated_at        timestamptz,
  deleted_at            timestamptz,
  created_at            timestamptz not null default now(),
  updated_at            timestamptz not null default now(),
  created_by            uuid references public.app_users(id),
  updated_by            uuid references public.app_users(id),
  constraint minors_need_guardian check (
    not is_minor or (guardian_name is not null and guardian_contact_enc is not null and guardian_consent_at is not null)
  )
);

create table public.cell_groups (
  id               uuid primary key default gen_random_uuid(),
  church_id        uuid not null references public.churches(id),
  name             text not null check (char_length(name) between 1 and 120),
  leader_id        uuid references public.app_users(id),
  parent_group_id  uuid references public.cell_groups(id),
  archived_at      timestamptz,
  created_at       timestamptz not null default now(),
  updated_at       timestamptz not null default now(),
  created_by       uuid references public.app_users(id),
  updated_by       uuid references public.app_users(id),
  check (parent_group_id is distinct from id)
);

create table public.group_memberships (
  id          uuid primary key default gen_random_uuid(),
  church_id   uuid not null references public.churches(id),
  group_id    uuid not null references public.cell_groups(id),
  user_id     uuid not null references public.app_users(id),
  joined_on   date not null default current_date,
  left_on     date,
  created_at  timestamptz not null default now(),
  updated_at  timestamptz not null default now(),
  check (left_on is null or left_on >= joined_on)
);

create table public.event_types (
  id             uuid primary key default gen_random_uuid(),
  church_id      uuid not null references public.churches(id),
  code           text not null check (code ~ '^[A-Z][A-Z0-9_]{1,39}$'),
  name           text not null check (char_length(name) between 1 and 120),
  category       public.event_category not null,
  cadence        public.event_cadence not null,
  default_scope  public.event_scope not null,
  is_system      boolean not null default false,
  weekday        smallint check (weekday between 0 and 6),          -- 0 = Sunday, for WEEKLY generation
  default_start  time,
  default_end    time,
  is_active      boolean not null default true,
  created_at     timestamptz not null default now(),
  updated_at     timestamptz not null default now(),
  unique (church_id, code)
);

create table public.events (
  id               uuid primary key default gen_random_uuid(),
  church_id        uuid not null references public.churches(id),
  event_type_id    uuid not null references public.event_types(id),
  group_id         uuid references public.cell_groups(id),
  title            text not null check (char_length(title) between 1 and 200),
  description      text check (char_length(description) <= 4000),
  starts_at        timestamptz not null,
  ends_at          timestamptz not null,
  occurs_on        date not null,                                   -- local date in church timezone
  location         text check (char_length(location) <= 300),
  online_link      text check (char_length(online_link) <= 2000 and online_link ~ '^https://'),
  recurrence_rule  text check (char_length(recurrence_rule) <= 500),
  series_id        uuid,
  is_generated     boolean not null default false,
  created_by       uuid references public.app_users(id),
  updated_by       uuid references public.app_users(id),
  cancelled_at     timestamptz,
  created_at       timestamptz not null default now(),
  updated_at       timestamptz not null default now(),
  check (ends_at > starts_at)
);

create table public.event_audiences (
  id        uuid primary key default gen_random_uuid(),
  event_id  uuid not null references public.events(id) on delete cascade,
  group_id  uuid references public.cell_groups(id),
  user_id   uuid references public.app_users(id),
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  check (num_nonnulls(group_id, user_id) = 1)                       -- no rows = whole church
);

create table public.attendance (
  id          uuid primary key default gen_random_uuid(),
  church_id   uuid not null references public.churches(id),
  event_id    uuid not null references public.events(id),
  user_id     uuid not null references public.app_users(id),
  status      public.attendance_status not null,
  marked_by   uuid references public.app_users(id),
  marked_at   timestamptz not null default now(),
  note        text check (char_length(note) <= 500),
  created_at  timestamptz not null default now(),
  updated_at  timestamptz not null default now(),
  unique (event_id, user_id)
  -- optimistic concurrency uses the system column xmin
);

create table public.devotions (
  id                uuid primary key default gen_random_uuid(),
  church_id         uuid not null references public.churches(id),
  user_id           uuid not null references public.app_users(id),
  devotion_date     date not null,
  slot              public.devotion_slot not null,
  text_content      text check (char_length(text_content) <= 5000),
  image_path        text,
  content_hash      text not null check (content_hash ~ '^[0-9a-f]{64}$'),
  ai_status         public.ai_status not null default 'PENDING',
  ai_text_score     numeric(5,2) check (ai_text_score between 0 and 100),
  ai_image_score    numeric(5,2) check (ai_image_score between 0 and 100),
  ai_provider       text,
  ai_model_version  text,
  ai_checked_at     timestamptz,
  reviewed_by       uuid references public.app_users(id),
  reviewed_at       timestamptz,
  review_note       text check (char_length(review_note) <= 1000),
  submitted_at      timestamptz not null default now(),
  created_at        timestamptz not null default now(),
  updated_at        timestamptz not null default now(),
  unique (user_id, devotion_date, slot),
  check (text_content is not null or image_path is not null)
);

create table public.invites (
  id                 uuid primary key default gen_random_uuid(),
  church_id          uuid not null references public.churches(id),
  inviter_id         uuid not null references public.app_users(id),
  event_id           uuid not null references public.events(id),
  guest_name         text not null check (char_length(guest_name) between 1 and 200),
  guest_contact_enc  text,
  status             public.invite_status not null default 'EXPECTED',
  converted_user_id  uuid references public.app_users(id),
  purge_after        date not null default (current_date + 90),
  deleted_at         timestamptz,
  created_at         timestamptz not null default now(),
  updated_at         timestamptz not null default now()
);

create table public.role_changes (
  id          uuid primary key default gen_random_uuid(),
  church_id   uuid not null references public.churches(id),
  user_id     uuid not null references public.app_users(id),
  from_role   public.user_role not null,
  to_role     public.user_role not null,
  changed_by  uuid not null references public.app_users(id),
  reason      text check (char_length(reason) <= 500),
  changed_at  timestamptz not null default now(),
  created_at  timestamptz not null default now(),
  updated_at  timestamptz not null default now()
);

create table public.member_status_changes (
  id           uuid primary key default gen_random_uuid(),
  church_id    uuid not null references public.churches(id),
  user_id      uuid not null references public.app_users(id),
  from_status  public.member_status not null,
  to_status    public.member_status not null,
  changed_by   uuid not null references public.app_users(id),
  reason       text check (char_length(reason) <= 500),
  changed_at   timestamptz not null default now(),
  created_at   timestamptz not null default now(),
  updated_at   timestamptz not null default now()
);

create table public.notifications (
  id               uuid primary key default gen_random_uuid(),
  church_id        uuid not null references public.churches(id),
  user_id          uuid not null references public.app_users(id),
  type             text not null check (char_length(type) <= 60),
  payload          jsonb not null default '{}'::jsonb,
  channels         text[] not null default '{in_app}',
  read_at          timestamptz,
  delivered_at     timestamptz,
  attempts         integer not null default 0,
  next_attempt_at  timestamptz not null default now(),
  last_error       text,
  created_at       timestamptz not null default now(),
  updated_at       timestamptz not null default now()
);

create table public.report_jobs (
  id            uuid primary key default gen_random_uuid(),
  church_id     uuid not null references public.churches(id),
  requested_by  uuid not null references public.app_users(id),
  type          text not null check (char_length(type) <= 60),
  params        jsonb not null default '{}'::jsonb,
  status        public.report_job_status not null default 'QUEUED',
  file_path     text,
  error         text,
  completed_at  timestamptz,
  expires_at    timestamptz,
  created_at    timestamptz not null default now(),
  updated_at    timestamptz not null default now()
);

create table public.audit_logs (
  id              uuid primary key default gen_random_uuid(),
  church_id       uuid references public.churches(id),
  actor_id        uuid references public.app_users(id),
  action          text not null check (char_length(action) <= 100),
  entity_type     text check (char_length(entity_type) <= 100),
  entity_id       text check (char_length(entity_id) <= 100),
  before          jsonb,
  after           jsonb,
  ip_hash         text,
  user_agent      text check (char_length(user_agent) <= 500),
  correlation_id  text check (char_length(correlation_id) <= 100),
  occurred_at     timestamptz not null default now()
);

-- ---------- updated_at triggers ----------
do $$
declare t text;
begin
  foreach t in array array['churches','app_users','cell_groups','group_memberships','event_types','events',
    'event_audiences','attendance','devotions','invites','role_changes','member_status_changes','notifications','report_jobs']
  loop
    execute format('create trigger %I_updated_at before update on public.%I for each row execute function public.set_updated_at()', t, t);
  end loop;
end $$;

-- ---------- invariants the database also enforces ----------
-- Audit log is append-only.
create function public.audit_logs_block_update() returns trigger
language plpgsql as $$
begin
  raise exception 'audit_logs is append-only';
end $$;
create trigger audit_logs_no_update before update on public.audit_logs
  for each row execute function public.audit_logs_block_update();

-- Morning Prayer never happens on Sunday (§5.1).
create function public.events_no_sunday_morning_prayer() returns trigger
language plpgsql as $$
begin
  if extract(dow from new.occurs_on) = 0
     and exists (select 1 from public.event_types et where et.id = new.event_type_id and et.code = 'MORNING_PRAYER') then
    raise exception 'MORNING_PRAYER cannot occur on Sunday' using errcode = 'check_violation';
  end if;
  return new;
end $$;
create trigger events_no_sunday_morning_prayer before insert or update of occurs_on, event_type_id on public.events
  for each row execute function public.events_no_sunday_morning_prayer();

-- ---------- indexes ----------
create unique index events_generated_unique on public.events (event_type_id, group_id, occurs_on) nulls not distinct where is_generated;
create unique index app_users_email_unique on public.app_users (church_id, email) where deleted_at is null;
create unique index group_memberships_one_active on public.group_memberships (user_id) where left_on is null;
create unique index cell_groups_name_unique on public.cell_groups (church_id, lower(name)) where archived_at is null;

create index attendance_event_idx on public.attendance (event_id);
create index attendance_user_marked_idx on public.attendance (user_id, marked_at);
create index events_church_starts_idx on public.events (church_id, starts_at);
create index events_group_starts_idx on public.events (group_id, starts_at);
create index event_audiences_event_idx on public.event_audiences (event_id);
create index devotions_user_date_idx on public.devotions (user_id, devotion_date);
create index devotions_church_date_idx on public.devotions (church_id, devotion_date);
create index devotions_pending_idx on public.devotions (created_at) where ai_status = 'PENDING';
create index devotions_content_hash_idx on public.devotions (content_hash);
create index group_memberships_active_idx on public.group_memberships (group_id) where left_on is null;
create index app_users_church_active_idx on public.app_users (church_id) where deleted_at is null;
create index app_users_mobile_bidx_idx on public.app_users (church_id, mobile_bidx) where deleted_at is null;
create index app_users_name_birthdate_bidx_idx on public.app_users (church_id, name_birthdate_bidx) where deleted_at is null;
create index cell_groups_leader_idx on public.cell_groups (leader_id) where archived_at is null;
create index invites_inviter_idx on public.invites (inviter_id, created_at);
create index invites_event_idx on public.invites (event_id);
create index invites_purge_idx on public.invites (purge_after) where converted_user_id is null and deleted_at is null;
create index notifications_user_idx on public.notifications (user_id, created_at desc);
create index notifications_outbox_idx on public.notifications (next_attempt_at) where delivered_at is null;
create index role_changes_user_idx on public.role_changes (user_id, changed_at);
create index member_status_changes_user_idx on public.member_status_changes (user_id, changed_at);
create index report_jobs_requested_idx on public.report_jobs (requested_by, created_at desc);
create index audit_logs_church_time_idx on public.audit_logs (church_id, occurred_at desc);
create index audit_logs_actor_idx on public.audit_logs (actor_id, occurred_at desc);
create index audit_logs_entity_idx on public.audit_logs (entity_type, entity_id);
