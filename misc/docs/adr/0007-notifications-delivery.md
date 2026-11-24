# 0007 — Notification delivery: email provider and web push

Status: Accepted in part (2026-11-24). Delivery is decided; **the production email provider is still open.**

**Decision: every notification is a row in `notifications` (the outbox). In-app is that row and is always on. The Worker's
`deliver-notifications` job sends email and web push from it, and production ships with email off
(`Notifications:Email:Provider = none`) until the product owner picks a provider.**

How it works:
- Features only insert rows; nothing sends inside a request. The job runs every minute, claims up to 100 due rows for 5 minutes,
  and sends each on the channels the user chose (`app_users.settings.notifications`, per category, with defaults).
- An email failure retries with backoff (2^attempts minutes). After 6 attempts the row is marked delivered in-app only.
  Push is tried once, on the first attempt, so a retry never repeats a push.
- A push service answering 404/410 means the subscription is gone; its row is deleted.
- Deactivated or deleted users get nothing outside the app.
- Shortcut: the claim is an `available_at` bump, not a row lock. Safe with one Worker; use `FOR UPDATE SKIP LOCKED` before running two.

Email providers (`Notifications:Email:Provider`):
- `none`: the production default. The preferences page says email isn't set up and its checkboxes are disabled.
- `mailpit`: local development only (`MailpitUrl`, `From`). Mail lands in Mailpit at :55424.
- Anything else fails at startup, so a typo can't silently drop mail. Adding a real provider is one `IEmailSender`.

Web push:
- Standard Web Push with VAPID (Lib.Net.Http.WebPush), payload encrypted (aes128gcm), TTL 12 hours.
- Push is on only when both `Notifications:Push:PublicKey` and `Notifications:Push:PrivateKey` are set. The private key lives in
  user-secrets (dev) or the host's secret store (prod), never in the repo. Generate a pair once per environment; changing it
  invalidates every browser subscription.
- Signing out unsubscribes the browser, so a shared phone doesn't keep getting the last person's notifications.

Open:
- Which email provider for production, and its sending domain (SPF/DKIM).
