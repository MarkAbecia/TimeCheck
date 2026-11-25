# Incident response

For security incidents and personal data breaches. The church's Data Protection Officer (DPO) owns the decisions below;
the developer on call does the technical steps.

## 1. Contain (first hour)
- Leaked secret: rotate it now (`../runbooks/rotate-secrets.md`).
- Compromised account: deactivate it (admin screen); for an admin, also reset their MFA in Supabase Auth.
- Ongoing abuse: tighten the rate limits or take the web service down in Render; the API keeps the data safe without it.
- Keep evidence: export the relevant audit log rows and Render logs before anything ages out.

## 2. Assess (within 24 hours)
- What data, whose, how many people, minors involved? Use `data-inventory.md` to see what each table holds and whether it was
  encrypted (encrypted columns are useless without the keys, which live outside the database).
- How it happened, and whether it is still possible.

## 3. Notify (RA 10173)
A personal data breach that likely puts people at real risk must be reported to the **National Privacy Commission within
72 hours** of knowing about it, and the affected people told, per NPC Circular 16-03. The DPO decides and sends both.
Record why if the decision is not to notify.

## 4. Recover and learn
- Fix the cause, deploy, and check the fix with a test.
- Write a short post-incident note (timeline, cause, what changed) in this folder, and update `threat-model.md`.
