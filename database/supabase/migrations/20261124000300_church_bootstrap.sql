-- create_church(): inserts a church and its system event types (§5.1). Used by seed.sql and by ops for production.
create function public.create_church(p_name text, p_timezone text default 'Asia/Manila')
returns uuid
language plpgsql as $$
declare cid uuid;
begin
  insert into public.churches (name, timezone, settings)
  values (p_name, p_timezone, jsonb_build_object(
    'attendanceEditWindowDays', 7,
    'devotion', jsonb_build_object(
      'dayOpens', '00:00', 'dayCloses', '23:59', 'dayReminder', '07:00',
      'nightOpens', '18:00', 'nightCloses', '05:00', 'nightReminder', '20:00'),
    'ai', jsonb_build_object('enabled', true, 'likelyHumanBelow', 40, 'likelyAiAbove', 75, 'membersSeeOwnScore', false),
    'retention', jsonb_build_object('guestInviteDays', 90, 'deactivatedUserYears', 3, 'auditLogDays', 400),
    'ageBrackets', jsonb_build_array(12, 17, 24, 35),
    'weekOfFireWeekday', 5))
  returning id into cid;

  insert into public.event_types (church_id, code, name, category, cadence, default_scope, is_system, weekday, default_start, default_end) values
    (cid, 'DEVOTION_DAY',     'Devotion (Day)',          'DAILY',            'DAILY',         'INDIVIDUAL',  true, null, null, null),
    (cid, 'DEVOTION_NIGHT',   'Devotion (Night)',        'DAILY',            'DAILY',         'INDIVIDUAL',  true, null, null, null),
    (cid, 'MORNING_PRAYER',   'Morning Prayer',          'DAILY',            'DAILY_MON_SAT', 'GROUP',       true, null, '05:00', '06:00'),
    (cid, 'WEEK_OF_FIRE',     'Week of Fire',            'WEEKLY',           'WEEKLY',        'CHURCH_WIDE', true, 5,    '19:00', '21:00'),
    (cid, 'CELL_CELEBRATION', 'Sunday Cell Celebration', 'WEEKLY',           'WEEKLY',        'CHURCH_WIDE', true, 0,    '09:00', '12:00'),
    (cid, 'CELL_MEETING',     'Cell Meeting',            'WEEKLY',           'WEEKLY',        'GROUP',       true, null, null, null),
    (cid, 'PID_ILD',          'PID/ILD',                 'LEADER_SCHEDULED', 'AD_HOC',        'GROUP',       true, null, null, null),
    (cid, 'MENTORING',        'Mentoring',               'LEADER_SCHEDULED', 'AD_HOC',        'GROUP',       true, null, null, null),
    (cid, 'PEPSOL',           'PEPSOL Training',         'ADMIN_SCHEDULED',  'SCHEDULED',     'CHURCH_WIDE', true, null, null, null),
    (cid, 'RFN_MENTORING',    'RFN Mentoring',           'ADMIN_SCHEDULED',  'SCHEDULED',     'CHURCH_WIDE', true, null, null, null),
    (cid, 'CONFERENCE',       'Conference',              'ADMIN_SCHEDULED',  'SCHEDULED',     'CHURCH_WIDE', true, null, null, null),
    (cid, 'SPECIAL_TRAINING', 'Special Training',        'ADMIN_SCHEDULED',  'SCHEDULED',     'CHURCH_WIDE', true, null, null, null),
    (cid, 'OTHER',            'Other',                   'ADMIN_SCHEDULED',  'SCHEDULED',     'CHURCH_WIDE', true, null, null, null);
  return cid;
end $$;

revoke all on function public.create_church(text, text) from public, anon, authenticated;
