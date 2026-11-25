-- Force logout (REQUIREMENTS §6.10). Supabase Auth has no admin "sign this user out" endpoint, so:
--  1. the API deletes the person's Auth sessions through this one narrow function (refresh tokens cascade with them), and
--  2. stamps app_users.sessions_revoked_at; the API rejects any access token issued before it, so the up-to-an-hour JWTs
--     already handed out stop working on the next request (within the 60 s user cache).

alter table public.app_users add column sessions_revoked_at timestamptz;

create function public.revoke_auth_sessions(p_auth_user_id uuid) returns integer
language plpgsql security definer set search_path = '' as $$
declare removed integer;
begin
  delete from auth.sessions where user_id = p_auth_user_id;
  get diagnostics removed = row_count;
  return removed;
end $$;

revoke all on function public.revoke_auth_sessions(uuid) from public, anon, authenticated;
grant execute on function public.revoke_auth_sessions(uuid) to timecheck_app;
