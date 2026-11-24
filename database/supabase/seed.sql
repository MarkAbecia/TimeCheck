-- DEV-ONLY seed (supabase db reset runs this). Never run against staging/production.
-- Demo login password for every account below: TimeCheck-dev-123!
-- Encrypted columns were produced with the dev keys in backend/src/TimeCheck.Api/appsettings.Development.json
-- by misc/scripts/gen-seed-users.mjs. Re-run that script if those keys change.

-- Local login for the API, member of the least-privilege group role.
do $$
begin
  if not exists (select 1 from pg_roles where rolname = 'timecheck_api') then
    create role timecheck_api login password 'timecheck-dev-only' in role timecheck_app;
  end if;
end $$;


-- @@SEED_USERS@@
do $$
declare cid uuid := public.create_church('Demo Church');
begin

  insert into auth.users (instance_id, id, aud, role, email, encrypted_password, email_confirmed_at,
    raw_app_meta_data, raw_user_meta_data, created_at, updated_at, confirmation_token, email_change, email_change_token_new, recovery_token)
  values ('00000000-0000-0000-0000-000000000000', '30000000-0000-0000-0000-000000000001', 'authenticated', 'authenticated', 'super@timecheck.test',
    extensions.crypt('TimeCheck-dev-123!', extensions.gen_salt('bf')), now(),
    '{"provider":"email","providers":["email"]}', '{}', now(), now(), '', '', '', '');
  insert into auth.identities (id, user_id, provider_id, identity_data, provider, last_sign_in_at, created_at, updated_at)
  values (gen_random_uuid(), '30000000-0000-0000-0000-000000000001', '30000000-0000-0000-0000-000000000001', jsonb_build_object('sub', '30000000-0000-0000-0000-000000000001', 'email', 'super@timecheck.test'), 'email', now(), now(), now());
  insert into public.app_users (id, church_id, auth_user_id, role, first_name, last_name, gender, birthdate_enc, name_birthdate_bidx,
    mobile_enc, mobile_bidx, email, status, joined_on, must_change_password, privacy_consent_version, privacy_consent_at)
  values ('10000000-0000-0000-0000-000000000001', cid, '30000000-0000-0000-0000-000000000001', 'SUPER_ADMIN', 'Grace', 'Santos', 'FEMALE', 'v1:VsKGfM/goY3PGUImzu9bmiG40WbSa7jW+/hiYALZhDFhRguYwss=', '9cf5feac4a0e3cc48525a58823fdd4a2dfc6440a532bd09cecc0bd77cb74fbd5',
    'v1:FJ58zwYlNt+ssWxhJH3puv1TubchYuM1Tyzar7eW7vq1Oukf78Ed', 'df0d51e048365ab7e66960d3a5b548504fd7fa8e1490aadde00beab500a4bc27', 'super@timecheck.test', 'DISCIPLE', '2025-01-05', false, '2026-11', now());

  insert into auth.users (instance_id, id, aud, role, email, encrypted_password, email_confirmed_at,
    raw_app_meta_data, raw_user_meta_data, created_at, updated_at, confirmation_token, email_change, email_change_token_new, recovery_token)
  values ('00000000-0000-0000-0000-000000000000', '30000000-0000-0000-0000-000000000002', 'authenticated', 'authenticated', 'admin@timecheck.test',
    extensions.crypt('TimeCheck-dev-123!', extensions.gen_salt('bf')), now(),
    '{"provider":"email","providers":["email"]}', '{}', now(), now(), '', '', '', '');
  insert into auth.identities (id, user_id, provider_id, identity_data, provider, last_sign_in_at, created_at, updated_at)
  values (gen_random_uuid(), '30000000-0000-0000-0000-000000000002', '30000000-0000-0000-0000-000000000002', jsonb_build_object('sub', '30000000-0000-0000-0000-000000000002', 'email', 'admin@timecheck.test'), 'email', now(), now(), now());
  insert into public.app_users (id, church_id, auth_user_id, role, first_name, last_name, gender, birthdate_enc, name_birthdate_bidx,
    mobile_enc, mobile_bidx, email, status, joined_on, must_change_password, privacy_consent_version, privacy_consent_at)
  values ('10000000-0000-0000-0000-000000000002', cid, '30000000-0000-0000-0000-000000000002', 'ADMIN', 'Paolo', 'Reyes', 'MALE', 'v1:J3R277xljiddf4ueOLPS5sIIFoZ8Ne0yrAUK/1XXe7jCrgpB80k=', 'bc39cf8cab039e24650cbb3cac18d7f895031c6cff1c12c2544c356b82abef64',
    'v1:d1rozNY1X20QtoHNYehR+dcQPQs+9FdBkqzBnh9ka9ayDYDIn5ij', '6a9296fd233b35c8e0bfc955d78e75b85e1da75a407aa497512315adafd24c68', 'admin@timecheck.test', 'DISCIPLE', '2025-01-05', false, '2026-11', now());

  insert into auth.users (instance_id, id, aud, role, email, encrypted_password, email_confirmed_at,
    raw_app_meta_data, raw_user_meta_data, created_at, updated_at, confirmation_token, email_change, email_change_token_new, recovery_token)
  values ('00000000-0000-0000-0000-000000000000', '30000000-0000-0000-0000-000000000003', 'authenticated', 'authenticated', 'leader@timecheck.test',
    extensions.crypt('TimeCheck-dev-123!', extensions.gen_salt('bf')), now(),
    '{"provider":"email","providers":["email"]}', '{}', now(), now(), '', '', '', '');
  insert into auth.identities (id, user_id, provider_id, identity_data, provider, last_sign_in_at, created_at, updated_at)
  values (gen_random_uuid(), '30000000-0000-0000-0000-000000000003', '30000000-0000-0000-0000-000000000003', jsonb_build_object('sub', '30000000-0000-0000-0000-000000000003', 'email', 'leader@timecheck.test'), 'email', now(), now(), now());
  insert into public.app_users (id, church_id, auth_user_id, role, first_name, last_name, gender, birthdate_enc, name_birthdate_bidx,
    mobile_enc, mobile_bidx, email, status, joined_on, must_change_password, privacy_consent_version, privacy_consent_at)
  values ('10000000-0000-0000-0000-000000000003', cid, '30000000-0000-0000-0000-000000000003', 'LEADER', 'Joy', 'Dela Cruz', 'FEMALE', 'v1:AoTDhcdj/nz0NLjat9FIYDgky6FablJen6Yqig+3ntDbEUic9ps=', '3f90a920fd1185d2425e5c7feb0f30f7ba62e2202469c10c90a904d13d7c1033',
    'v1:KCg/RXB+6l22GfbN2NaIup+ztgyeX7pDJYejS5KDrMwFo6Vbv241', '918a17c1b9261925c3f44a09a0a4df814eb85b10db2d43c1b7b9ddef4198674c', 'leader@timecheck.test', 'DISCIPLE', '2025-01-05', false, '2026-11', now());

  insert into auth.users (instance_id, id, aud, role, email, encrypted_password, email_confirmed_at,
    raw_app_meta_data, raw_user_meta_data, created_at, updated_at, confirmation_token, email_change, email_change_token_new, recovery_token)
  values ('00000000-0000-0000-0000-000000000000', '30000000-0000-0000-0000-000000000004', 'authenticated', 'authenticated', 'member@timecheck.test',
    extensions.crypt('TimeCheck-dev-123!', extensions.gen_salt('bf')), now(),
    '{"provider":"email","providers":["email"]}', '{}', now(), now(), '', '', '', '');
  insert into auth.identities (id, user_id, provider_id, identity_data, provider, last_sign_in_at, created_at, updated_at)
  values (gen_random_uuid(), '30000000-0000-0000-0000-000000000004', '30000000-0000-0000-0000-000000000004', jsonb_build_object('sub', '30000000-0000-0000-0000-000000000004', 'email', 'member@timecheck.test'), 'email', now(), now(), now());
  insert into public.app_users (id, church_id, auth_user_id, role, first_name, last_name, gender, birthdate_enc, name_birthdate_bidx,
    mobile_enc, mobile_bidx, email, status, joined_on, must_change_password, privacy_consent_version, privacy_consent_at)
  values ('10000000-0000-0000-0000-000000000004', cid, '30000000-0000-0000-0000-000000000004', 'MEMBER', 'Nico', 'Bautista', 'MALE', 'v1:mJEka9OhYZQZiwfTGTtmrkFj5oIITszqNoUfjFeVwp1R7XwmW7c=', 'ca7de8b88adadc0b94d11cc4443f49691d1ff02e3654a70d1d2e015fda4340a4',
    'v1:04dhrS0Lcmq29K4xvj+JfhdBavs5iMHBymk/UckOQFTFbEktdT8/', 'd33042d994ea7bf4031f260876b89da9e405f6add4c186d84a7c1d3ccc8101e9', 'member@timecheck.test', 'NEW_MEMBER', '2025-01-05', false, '2026-11', now());

  insert into auth.users (instance_id, id, aud, role, email, encrypted_password, email_confirmed_at,
    raw_app_meta_data, raw_user_meta_data, created_at, updated_at, confirmation_token, email_change, email_change_token_new, recovery_token)
  values ('00000000-0000-0000-0000-000000000000', '30000000-0000-0000-0000-000000000005', 'authenticated', 'authenticated', 'leader2@timecheck.test',
    extensions.crypt('TimeCheck-dev-123!', extensions.gen_salt('bf')), now(),
    '{"provider":"email","providers":["email"]}', '{}', now(), now(), '', '', '', '');
  insert into auth.identities (id, user_id, provider_id, identity_data, provider, last_sign_in_at, created_at, updated_at)
  values (gen_random_uuid(), '30000000-0000-0000-0000-000000000005', '30000000-0000-0000-0000-000000000005', jsonb_build_object('sub', '30000000-0000-0000-0000-000000000005', 'email', 'leader2@timecheck.test'), 'email', now(), now(), now());
  insert into public.app_users (id, church_id, auth_user_id, role, first_name, last_name, gender, birthdate_enc, name_birthdate_bidx,
    mobile_enc, mobile_bidx, email, status, joined_on, must_change_password, privacy_consent_version, privacy_consent_at)
  values ('10000000-0000-0000-0000-000000000005', cid, '30000000-0000-0000-0000-000000000005', 'LEADER', 'Ben', 'Garcia', 'MALE', 'v1:h/U5IdRtHSYmxPIBx34hT6ymWwG1iuBDMcNv0dBR9lfhji/0MFM=', '2c38ee7e4517ae39ea1f0f788c383c891a6497659b5f95d0f0ed5b1a334ca607',
    'v1:U8yaO3RNdXe8KIs0wOX7EKVZ9TAWTxcVdcDxCqp/xjBc4aAfhLhs', '3994c6a9aef623b73d00c00a241a8743b3ddca78d5b9c9c9079906dd0c920eb2', 'leader2@timecheck.test', 'DISCIPLE', '2025-01-05', false, '2026-11', now());

  insert into auth.users (instance_id, id, aud, role, email, encrypted_password, email_confirmed_at,
    raw_app_meta_data, raw_user_meta_data, created_at, updated_at, confirmation_token, email_change, email_change_token_new, recovery_token)
  values ('00000000-0000-0000-0000-000000000000', '30000000-0000-0000-0000-000000000006', 'authenticated', 'authenticated', 'member2@timecheck.test',
    extensions.crypt('TimeCheck-dev-123!', extensions.gen_salt('bf')), now(),
    '{"provider":"email","providers":["email"]}', '{}', now(), now(), '', '', '', '');
  insert into auth.identities (id, user_id, provider_id, identity_data, provider, last_sign_in_at, created_at, updated_at)
  values (gen_random_uuid(), '30000000-0000-0000-0000-000000000006', '30000000-0000-0000-0000-000000000006', jsonb_build_object('sub', '30000000-0000-0000-0000-000000000006', 'email', 'member2@timecheck.test'), 'email', now(), now(), now());
  insert into public.app_users (id, church_id, auth_user_id, role, first_name, last_name, gender, birthdate_enc, name_birthdate_bidx,
    mobile_enc, mobile_bidx, email, status, joined_on, must_change_password, privacy_consent_version, privacy_consent_at)
  values ('10000000-0000-0000-0000-000000000006', cid, '30000000-0000-0000-0000-000000000006', 'MEMBER', 'Lia', 'Ramos', 'FEMALE', 'v1:O+AfBLizYphQQNvtlIwbK5HIdUPN5v4Y/5A3wS4F2PvG5r0jdS0=', '38b142f7aa85d530a4d4e88d55f83759aba569c5d1b85e3319799ee4ae18b3aa',
    null, null, 'member2@timecheck.test', 'INCONSISTENT', '2025-01-05', false, '2026-11', now());

  insert into public.cell_groups (id, church_id, name, leader_id) values ('20000000-0000-0000-0000-000000000001', cid, 'Joshua Cell', '10000000-0000-0000-0000-000000000003');
  insert into public.cell_groups (id, church_id, name, leader_id) values ('20000000-0000-0000-0000-000000000002', cid, 'Caleb Cell', '10000000-0000-0000-0000-000000000005');
  insert into public.group_memberships (church_id, group_id, user_id, joined_on) values (cid, '20000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000003', '2025-01-05');
  insert into public.group_memberships (church_id, group_id, user_id, joined_on) values (cid, '20000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000004', '2025-01-05');
  insert into public.group_memberships (church_id, group_id, user_id, joined_on) values (cid, '20000000-0000-0000-0000-000000000002', '10000000-0000-0000-0000-000000000005', '2025-01-05');
  insert into public.group_memberships (church_id, group_id, user_id, joined_on) values (cid, '20000000-0000-0000-0000-000000000002', '10000000-0000-0000-0000-000000000006', '2025-01-05');
end $$;
