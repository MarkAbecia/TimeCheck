# 0003 — Application-level encryption of sensitive columns

Status: Accepted (2026-11-24)

## Decision
- birthdate, mobile, address, guardian contact and guest contact are stored as `*_enc text` holding
  `v{keyId}:base64(nonce12 | ciphertext | tag16)`, AES-256-GCM, AAD `timecheck:v{keyId}` (`FieldEncryptor`).
- Keys come from config (`Encryption:Keys:{id}`, `Encryption:ActiveKeyId`). New writes use the active key; old ids stay
  readable while configured, so rotation = add key, switch active id, re-encrypt in a background job, then remove old key.
- Search/dedup uses HMAC-SHA256 blind indexes with a separate key (`mobile_bidx`, `name_birthdate_bidx`), recomputed
  by `AppDbContext.SaveChangesAsync` whenever a user row is written. Mobile numbers are normalized (09xx == +639xx).
- EF value converters do the encryption, so domain code sees plaintext.

## Consequences
- `is_minor` cannot be a generated column (the DB can't read the birthdate). It is a stored flag set by the domain on
  save and refreshed by a daily Worker job; a DB check constraint still enforces "minor ⇒ guardian + consent".
- Age-bracket reports decrypt birthdates in the app. Fine at the v1 scale (≤ 2,000 users); revisit with a stored
  age bracket if it gets slow.
- Losing the key loses the data: keys must be backed up in the secret store, separate from DB backups.
- Envelope encryption via a KMS can replace the env key later without changing the ciphertext format (keyId → KMS key).
