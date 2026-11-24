# 0006 — AI content detection for devotions

Status: Accepted in part (2026-11-24). The pipeline is decided; **the provider is still open (§15 Q6).**

**Decision: detection runs in the Worker behind `IAiContentDetector`, and production ships with detection off (`Ai:Provider = none`)
until the product owner picks a provider.**

How it works:
- Upload (`PUT /me/devotions/{date}/{slot}`) never waits for detection and never fails because of it. The devotion is saved as `PENDING`.
- The Worker's `check-devotions` job runs every minute (so a score shows within 60 s, §6.3) and takes up to 50 pending devotions.
  - Identical content (same SHA-256 over text and cleaned image) already scored in the church reuses that score, so it isn't billed twice.
  - A provider error leaves the devotion `PENDING` for the next run; after an hour it becomes `FAILED`.
    Three errors in a row end the run, a simple circuit breaker.
  - If the church's `ai.enabled` setting is off or no provider is configured, devotions become `SKIPPED`.
- Scores are advisory only (§6.3 fairness rule). Nothing reads them except the leader's view and its "Likely AI" filter (§15 Q5).
  Bands use the church's thresholds (default under 40 likely human, over 75 likely AI).
- Only the content needed is sent: the text, or the re-encoded image with EXIF/GPS already removed. No names or ids.

Providers:
- `none`: the production default. Devotions are `SKIPPED`.
- `fake`: development only, and refused unless `Ai:FakeAllowed` is true (set only in `appsettings.Development.json`).
  It derives a meaningless score from the content hash so every band can be seen locally. It sends nothing anywhere.
- A real provider is an `IAiContentDetector` implementation in Infrastructure, using a typed `HttpClient` with
  `Microsoft.Extensions.Http.Resilience` (retries with backoff, timeout, circuit breaker).

Choosing the provider (open, owner's decision):
- It must contractually **not retain or train on** submitted content, and say so in its API terms. Record the clause here.
- One for text and one for images, or one that does both. If it offers OCR, use it for handwritten devotions (§6.3);
  until then, photos get only the image score.
- Note the cost per check. Members submit up to two devotions a day, and the hash cache removes repeats.
- Detectors are unreliable on short texts and non-English writing (many devotions mix English and Filipino).
  That is why the score is advisory and the UI carries a disclaimer.

Images:
- The API decodes uploads by content (SkiaSharp, MIT), accepts JPEG/PNG/WebP only, rotates them upright, caps the long side at
  2048 px and re-encodes as JPEG. That drops EXIF/GPS and anything appended to the file.
- HEIC from iPhones is converted to JPEG in the browser before upload, because the server can't decode it.
- Images live in the private `devotions` bucket under `{church}/{user}/{sha256}.jpg`. Keys are never user-controlled.
  Viewers get 5-minute signed URLs.
