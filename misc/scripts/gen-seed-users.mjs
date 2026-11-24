// Regenerates the demo users block of database/supabase/seed.sql using the DEV-ONLY keys in
// backend/src/TimeCheck.Api/appsettings.Development.json. Mirrors FieldEncryptor/BlindIndexer in C#.
// Usage: node misc/scripts/gen-seed-users.mjs
import { createCipheriv, createHmac, randomBytes } from 'node:crypto';
import { readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const root = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const settings = JSON.parse(readFileSync(join(root, 'backend/src/TimeCheck.Api/appsettings.Development.json'), 'utf8'));
const keyId = settings.Encryption.ActiveKeyId;
const dataKey = Buffer.from(settings.Encryption.Keys[keyId], 'base64');
const bidxKey = Buffer.from(settings.Encryption.BlindIndexKey, 'base64');

const enc = (plain) => {
  const nonce = randomBytes(12);
  const c = createCipheriv('aes-256-gcm', dataKey, nonce);
  c.setAAD(Buffer.from(`timecheck:v${keyId}`));
  const ct = Buffer.concat([c.update(plain, 'utf8'), c.final()]);
  return `v${keyId}:${Buffer.concat([nonce, ct, c.getAuthTag()]).toString('base64')}`;
};
const hmac = (purpose, value) => createHmac('sha256', bidxKey).update(`${purpose}:${value}`).digest('hex');
const mobileBidx = (m) => {
  let d = m.replace(/\D/g, '');
  if (d.startsWith('63') && d.length === 12) d = '0' + d.slice(2);
  return hmac('mobile', d);
};
const nameBidx = (f, l, b) => hmac('name_birthdate', `${f.trim().toLowerCase()}|${l.trim().toLowerCase()}|${b}`);

// Fixed ids so integration tests and docs can refer to them.
const users = [
  { id: '10000000-0000-0000-0000-000000000001', email: 'super@timecheck.test', role: 'SUPER_ADMIN', first: 'Grace', last: 'Santos', gender: 'FEMALE', birth: '1980-03-10', mobile: '09170000001', status: 'DISCIPLE' },
  { id: '10000000-0000-0000-0000-000000000002', email: 'admin@timecheck.test', role: 'ADMIN', first: 'Paolo', last: 'Reyes', gender: 'MALE', birth: '1985-07-21', mobile: '09170000002', status: 'DISCIPLE' },
  { id: '10000000-0000-0000-0000-000000000003', email: 'leader@timecheck.test', role: 'LEADER', first: 'Joy', last: 'Dela Cruz', gender: 'FEMALE', birth: '1995-01-15', mobile: '09170000003', status: 'DISCIPLE', group: 'g1' },
  { id: '10000000-0000-0000-0000-000000000004', email: 'member@timecheck.test', role: 'MEMBER', first: 'Nico', last: 'Bautista', gender: 'MALE', birth: '2001-09-02', mobile: '09170000004', status: 'NEW_MEMBER', group: 'g1' },
  { id: '10000000-0000-0000-0000-000000000005', email: 'leader2@timecheck.test', role: 'LEADER', first: 'Ben', last: 'Garcia', gender: 'MALE', birth: '1992-11-30', mobile: '09170000005', status: 'DISCIPLE', group: 'g2' },
  { id: '10000000-0000-0000-0000-000000000006', email: 'member2@timecheck.test', role: 'MEMBER', first: 'Lia', last: 'Ramos', gender: 'FEMALE', birth: '2003-04-18', mobile: null, status: 'INCONSISTENT', group: 'g2' },
];
const groups = {
  g1: { id: '20000000-0000-0000-0000-000000000001', name: 'Joshua Cell', leader: users[2].id },
  g2: { id: '20000000-0000-0000-0000-000000000002', name: 'Caleb Cell', leader: users[4].id },
};
const q = (s) => (s === null ? 'null' : `'${String(s).replace(/'/g, "''")}'`);

let sql = `do $$
declare cid uuid := public.create_church('Demo Church');
begin
`;
for (const u of users) {
  const authId = u.id.replace(/^1/, '3');
  sql += `
  insert into auth.users (instance_id, id, aud, role, email, encrypted_password, email_confirmed_at,
    raw_app_meta_data, raw_user_meta_data, created_at, updated_at, confirmation_token, email_change, email_change_token_new, recovery_token)
  values ('00000000-0000-0000-0000-000000000000', '${authId}', 'authenticated', 'authenticated', ${q(u.email)},
    extensions.crypt('TimeCheck-dev-123!', extensions.gen_salt('bf')), now(),
    '{"provider":"email","providers":["email"]}', '{}', now(), now(), '', '', '', '');
  insert into auth.identities (id, user_id, provider_id, identity_data, provider, last_sign_in_at, created_at, updated_at)
  values (gen_random_uuid(), '${authId}', '${authId}', jsonb_build_object('sub', '${authId}', 'email', ${q(u.email)}), 'email', now(), now(), now());
  insert into public.app_users (id, church_id, auth_user_id, role, first_name, last_name, gender, birthdate_enc, name_birthdate_bidx,
    mobile_enc, mobile_bidx, email, status, joined_on, must_change_password, privacy_consent_version, privacy_consent_at)
  values ('${u.id}', cid, '${authId}', '${u.role}', ${q(u.first)}, ${q(u.last)}, '${u.gender}', ${q(enc(u.birth))}, ${q(nameBidx(u.first, u.last, u.birth))},
    ${u.mobile ? q(enc(u.mobile)) : 'null'}, ${u.mobile ? q(mobileBidx(u.mobile)) : 'null'}, ${q(u.email)}, '${u.status}', '2025-01-05', false, '2026-11', now());
`;
}
for (const g of Object.values(groups)) {
  sql += `
  insert into public.cell_groups (id, church_id, name, leader_id) values ('${g.id}', cid, ${q(g.name)}, '${g.leader}');`;
}
for (const u of users.filter((x) => x.group)) {
  sql += `
  insert into public.group_memberships (church_id, group_id, user_id, joined_on) values (cid, '${groups[u.group].id}', '${u.id}', '2025-01-05');`;
}
sql += `
end $$;
`;

const seedPath = join(root, 'database/supabase/seed.sql');
const seed = readFileSync(seedPath, 'utf8');
const marker = '-- @@SEED_USERS@@';
const head = seed.slice(0, seed.indexOf(marker) + marker.length);
writeFileSync(seedPath, `${head}\n${sql}`);
console.log(`Wrote ${users.length} users and ${Object.keys(groups).length} groups to seed.sql`);
