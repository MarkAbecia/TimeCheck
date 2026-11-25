-- Retention (REQUIREMENTS §8.4): the app can't delete audit rows, so the purge goes through this one narrow function.
-- It only removes one church's rows older than a year (the floor in §8.4), whatever the caller asks for.

create function public.purge_audit_logs(p_church uuid, p_before timestamptz) returns integer
language plpgsql security definer set search_path = public as $$
declare removed integer;
begin
  if p_before > now() - interval '365 days' then
    raise exception 'audit logs are kept for at least a year';
  end if;
  delete from public.audit_logs where church_id = p_church and occurred_at < p_before;
  get diagnostics removed = row_count;
  return removed;
end $$;

revoke all on function public.purge_audit_logs(uuid, timestamptz) from public, anon, authenticated;
grant execute on function public.purge_audit_logs(uuid, timestamptz) to timecheck_app;

-- The retention job looks for people deactivated long ago.
create index app_users_deactivated_idx on public.app_users (church_id, deactivated_at) where deactivated_at is not null and deleted_at is null;
