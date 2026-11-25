# Impeccable audit — Phase 11 (2026-11-25)

Target: `frontend/src/app` (all app screens). Evidence: the impeccable detector over `src` (120 files) and over the rendered
sign-in pages at 390×844; the Playwright axe run (WCAG 2.2 AA tags) over 17 signed-in pages at 5 viewports in light and dark
(162 tests); code reading; gzipped chunk sizes from the production build. Signed-in pages weren't scanned by the detector's own
browser (it can't sign in); axe covers them.

## Audit Health Score

| # | Dimension | Score | Key Finding |
|---|-----------|-------|-------------|
| 1 | Accessibility | 3 | axe-clean everywhere, but 36 px small buttons break the product's own 44 px target rule |
| 2 | Performance | 3 | Server-rendered, lean pages; the 200 KB JS budget isn't measured per route yet |
| 3 | Theming | 4 | Tokens throughout, contrast gate in CI; only justified literals |
| 4 | Responsive Design | 3 | Phone-first layouts hold at 360–1440; small buttons are the gap |
| 5 | Implementation Integrity | 4 | 0 detector anti-patterns; 3 off-ramp font sizes (advisory) |
| **Total** | | **17/20** | **Good** |

## Implementation Integrity Verdict
**Pass.** The app reads as one product: the Event Pass system (black lanyard shell, pass cards, gold foil labels, Barlow
Condensed for names and numbers) carries from Today to Reports. The detector found 0 anti-patterns in 120 files and 0 on the
rendered sign-in pages. Its only notes are three literal font sizes off the DESIGN.md ramp (`text-[1.625rem]` in the auth layout
and the event pass, `text-[1.375rem]` in the app shell), which are deliberate display sizes the ramp doesn't list yet.

## Executive Summary
- Audit Health Score: **17/20** (Good)
- Issues: P0 0, P1 1, P2 3, P3 1
- Top issues:
  1. Small buttons (`size="sm"`, 36 px tall, 12.8 px labels) in 15 places, including back links, report exports and
     notification actions, against PRODUCT.md's 44×44 px commitment for a teens-to-seniors, phone-first audience.
  2. The shared error screen says "Today's events didn't load" on every page, including Reports and Members.
  3. Report titles, columns and notes come from the API in English and bypass next-intl.
- Next: fix the touch targets (one change in the Button component), reword the error screen, then measure JS per route in Phase 12.

## Detailed Findings by Severity

**[P1] Small buttons are below the 44 px touch target**
- Location: `src/components/ui/button.tsx` (`size: sm` = `h-9`, `text-[0.8rem]`); used 15 times, e.g. `reports/[type]/page.tsx:57`
  back link, `components/reports/export-buttons.tsx` CSV/XLSX/PDF, `notification-list.tsx`, `invite-item.tsx`, `cancel-event.tsx`.
- Category: Accessibility / Responsive
- Impact: leaders tap these one-handed after a meeting; 36 px targets cause mis-taps, worst for older members.
- Standard: passes WCAG 2.5.8 (24 px), fails the product commitment in PRODUCT.md (44×44 px, matching WCAG 2.5.5 AAA).
- Recommendation: on coarse pointers make `sm` 44 px tall (`pointer-coarse:h-11`) and keep the compact size for mouse users.
- Suggested command: /impeccable adapt

**[P2] The error screen talks about Today on every page**
- Location: `src/app/(app)/error.tsx` (shared by all app routes)
- Category: Implementation Integrity
- Impact: a failed report or member page says "Today's events didn't load", which misleads and hides what failed.
- Recommendation: generic copy ("This page didn't load") with the same retry; keep the Today wording only on Today.
- Suggested command: /impeccable clarify

**[P2] Report content isn't translatable**
- Location: backend `Features/Reports/Reports.cs` titles, columns, notes and scope lines; rendered by `reports/[type]/page.tsx`
- Category: Implementation Integrity (i18n)
- Impact: the app is next-intl everywhere else; a Filipino translation would leave reports in English.
- Recommendation: send keys and values from the API and label them with next-intl, or accept English reports for v1 in an ADR.
- Suggested command: /impeccable harden

**[P2] The JS budget isn't verified per route**
- Location: production build (`.next/static/chunks`)
- Category: Performance
- Impact: §9 sets under 200 KB gzipped JS for main routes. Page-specific chunks are 112 KB (Today), 115 KB (a report) and 114 KB
  (an event); the shared runtime comes on top (largest shared chunk 70 KB), so routes may sit near or over the budget.
- Recommendation: measure transfer size and LCP with Lighthouse on simulated 4G against the production build in Phase 12.
- Suggested command: /impeccable optimize

**[P3] Three display sizes are off the documented type ramp**
- Location: `(auth)/layout.tsx:13`, `components/event-pass.tsx:99`, `components/shell/app-shell.tsx:28`
- Category: Theming
- Recommendation: add the two sizes to the DESIGN.md ramp (they are intentional) so the detector stops flagging them.
- Suggested command: /impeccable document

## Patterns & Systemic Issues
- The compact button size is used as the default for secondary actions across features, so the touch-target gap repeats wherever
  a screen has a secondary action. Fixing the size in the Button component fixes all 15 at once.

## Positive Findings
- axe (WCAG 2.2 AA tags) passes on every signed-in page at 360, 390, 768, 1024 and 1440 px in light and dark, and the audit run
  caught and fixed one real issue on the way (the wide report table wasn't keyboard-scrollable at 768 px).
- Colours come only from tokens; the contrast script gates every palette change. The two literal colours are the browser
  `theme-color` meta values and the white behind the MFA QR code, both necessary.
- Reduced motion: one decorative animation (the stamp), and the state it marks is still shown by the check icon when motion is off.
- Images: the only raw `<img>` uses are short-lived signed URLs and data previews, each with alt text.
- Phone patterns are native and predictable: bottom tabs, cards instead of tables below `md`, filters in the URL.

## Recommended Actions
1. **[P1] `/impeccable adapt`**: make `size="sm"` buttons 44 px tall on touch screens.
2. **[P2] `/impeccable clarify`**: give the shared error screen copy that fits every page.
3. **[P2] `/impeccable harden`**: decide on translatable report content (keys from the API) or record English-only reports.
4. **[P2] `/impeccable optimize`**: measure per-route JS and LCP on 4G in Phase 12.
5. **[P3] `/impeccable document`**: add the two display sizes to the DESIGN.md ramp.
6. `/impeccable polish` after the fixes.
