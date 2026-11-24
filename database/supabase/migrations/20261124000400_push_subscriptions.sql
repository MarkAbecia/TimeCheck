-- Web push (REQUIREMENTS §6.7): one row per browser a person allowed notifications on.
-- The endpoint is the push service's URL for that browser; p256dh/auth are its public encryption keys (not secrets of ours).

create table public.push_subscriptions (
  id            uuid primary key default gen_random_uuid(),
  church_id     uuid not null references public.churches(id),
  user_id       uuid not null references public.app_users(id),
  endpoint      text not null unique check (char_length(endpoint) <= 1000),
  p256dh        text not null check (char_length(p256dh) <= 200),
  auth          text not null check (char_length(auth) <= 100),
  created_at    timestamptz not null default now(),
  updated_at    timestamptz not null default now()
);
create index push_subscriptions_user_idx on public.push_subscriptions (user_id);

create trigger push_subscriptions_updated_at before update on public.push_subscriptions
  for each row execute function public.set_updated_at();

-- Same lock-down as every other table (see 20261124000200_security.sql).
alter table public.push_subscriptions enable row level security;
alter table public.push_subscriptions force row level security;
revoke all on public.push_subscriptions from anon, authenticated;
grant select, insert, update, delete on public.push_subscriptions to timecheck_app;
create policy app_all on public.push_subscriptions for all to timecheck_app using (true) with check (true);
