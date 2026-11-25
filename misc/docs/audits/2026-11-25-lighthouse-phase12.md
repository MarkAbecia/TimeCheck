# Lighthouse: production build (2026-11-25)

- **Build:** `npm run build`, served with `next start` on localhost (gzip on), against the local API and Supabase.
- **Lighthouse:** CLI, default mobile profile (Moto G Power, simulated slow 4G, 4× CPU slowdown). Signed-in pages used the
  seeded leader's session cookies.
- **Budget (§9):** under 200 KB of JavaScript (gzipped transfer) on the main routes, LCP under 2.5 s on 4G.

## Results: before the fixes

| Page | Perf | A11y | Best practices | LCP | TBT | CLS | JS (gz) | Total |
|---|---|---|---|---|---|---|---|---|
| `/login` | 93 | 100 | 96 | 0.94 s | 317 ms | 0 | **284 KB** | 373 KB |
| `/` (Today) | 95 | 98 | 100 | 0.95 s | 247 ms | 0 | **238 KB** | 338 KB |
| `/members` | 85 | 100 | 100 | 3.45 s | 308 ms | 0 | **238 KB** | 339 KB |
| `/reports/attendance` | 71 → 95 | 100 | 96 | 3.65 → 0.85 s | 763 → 248 ms | 0 | **241 KB** | 337 KB |
| `/devotions` | 96 | 100 | 100 | 0.82 s | 222 ms | 0 | **243 KB** | 342 KB |

The report page's first run was cold (the report cache was empty and the server compiled its first request); a warm rerun gave
the second figures. One further rerun failed to record a paint and was discarded. `/members` (3.45 s LCP) wasn't rerun then; it measured
0.87 s after the fixes, which is the run-to-run variation described below rather than a change from the fixes.

**Verdict before the fixes:** LCP, CLS and accessibility met the targets. JavaScript was over budget on every route, by 38 to
84 KB.

## Results: after the fixes (same day)

| Page | Perf | A11y | Best practices | LCP | TBT | CLS | JS (gz) | Total |
|---|---|---|---|---|---|---|---|---|
| `/login` | 90 | 100 | 100 | 0.95 s | 392 ms | 0 | **195 KB** | 283 KB |
| `/` (Today) | 95 | 98 | 100 | 0.96 s | 268 ms | 0 | **186 KB** | 288 KB |
| `/members` | 94 | 100 | 100 | 0.87 s | 283 ms | 0 | **190 KB** | 292 KB |
| `/reports/attendance` | 98 | 100 | 100 | 0.82 s | 166 ms | 0 | **196 KB** | 305 KB |
| `/devotions` | 93 | 100 | 100 | 0.78 s | 333 ms | 0 | **198 KB** | 296 KB |

**All five routes are under the 200 KB budget**, with 2 to 14 KB to spare. What changed:
- The header's Appearance button and the phone's More tab are plain buttons until first tapped; the tap loads the real menu or
  sheet (Base UI and Floating UI, about 48 KB) and shows it already open. Checked by hand: the menu opens on one tap with focus
  inside, Escape returns focus to the button, Dark applies; the More sheet opens with its links, and a link navigates and closes
  it.
- The login form checks its two fields with plain React and the forgot-password form with react-hook-form's own rules, so
  `zod` (43 KB) no longer loads in the browser there; react-hook-form (12 KB) is gone from `/login` too. The server routes still
  validate with the same `zod` schemas, and the error messages are the same keys.

**Margins are thin.** Devotions is 2 KB under and Reports 4 KB under, so any new client library on those pages will break the
budget. Measure again whenever a page gains a client component.

**LCP varies between runs.** The same page scored 0.8 s in one run and 3.2 to 3.6 s in another, before and after the fixes.
The observed timings were small each time (TTFB under 100 ms, render delay under 500 ms); the swing comes from Lighthouse's
simulated 4G model. Judge LCP on staging with several runs, not from one local run.

## Where the JavaScript went (before the fixes)

Shared by every page:

| Chunk (gz) | Contents | Avoidable? |
|---|---|---|
| 71 KB | React DOM | No |
| 44 KB | Next.js router and hydration | No |
| 48 KB | Base UI and Floating UI | **Yes**, and now done: the app shell imported the dropdown menu and the More sheet, so every page loaded them before anyone opened a menu. |
| ~13 KB each | Next.js client runtime, lucide icons, page code | Mostly no |

Only on `/login` (and the forgot-password form): **43 KB of `zod`**, used to check two fields in the browser. Now removed.

Not checked: real phones, a deployed environment (CDN, HTTP/2 or 3, Render's compression), and the pages not listed above.
