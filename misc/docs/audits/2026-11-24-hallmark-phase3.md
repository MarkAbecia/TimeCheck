# Hallmark audit: Phase 3 (frontend/src/app)

- **Date:** 2026-11-24
- **Tool:** Hallmark 2.0.0, `audit` (report only)
- **Linter run:** `sloplint --scope app --genre editorial` over `src/app` and five live routes: `/`, `/login`, `/forgot-password`, `/reset-password`, `/members`.
- **Render tier:** not run, because puppeteer-core isn't installed. The judged gates were read from the browser-pane screenshots at 390×844, 768×1024 and 1440×900, in both modes.
- **Ownership:** Impeccable owns the design system (REQUIREMENTS §14). Each finding below was either fixed in the project's own system or dismissed with a reason.

## Read

| | |
|---|---|
| **For** | Cell leaders checking their group in on a phone. Members see their own passes. |
| **Works** | Each event is one pass, and its stub is the only place state and action live. The black shell, the red check-in stub and the 44px targets are clear. |
| **Generated tells** | None of the structural tells: no card-in-card, no gradients, no emoji icons, no invented metrics. The sample data is now labelled on screen. |
| **Broken** | Nothing functional. The only Floor failure was a missing `overflow-x: clip`. |

## Findings

| Gate | Tier | Finding | Outcome |
|---|---|---|---|
| 34 | Floor | `overflow-x: clip` missing on `html` and `body` (globals.css). | **Fixed.** Added in the base layer. |
| 48 ×32 | Floor | Colour literals in `.dark`. | **Dismissed, false positive.** `.dark` is a declared theme block, which the Floor allows. No literal colour exists outside the token blocks. |
| 7b ×4 | Reflex | Pure `#fff` for `--card` and `--popover`. | **Waived.** §11.1 pins light card `#FFFFFF`; white card stock on an off-white ground is the world's material. |
| 26b | Reflex | No `:active` styling in CSS. | **Dismissed.** Active states are Tailwind variants in components (button `active:translate-y-px`, check-in stub `active:` darken), which the CSS-only scan doesn't see. |
| 9 / 54 / 67 | Floor (judged) | Tracked capitals on the section heads and the role band. | **Conflict, kept.** The Barlow Condensed caps bands are the core of the direction contract (Impeccable owns this). They are display type on bands, not body labels, and nav and form labels are sentence case. |
| 10 / 63 | Floor (judged) | Middle-dot meta strings ("Joshua Cell · 20 members", "LEADER · JOSHUA CELL"). | **Kept for now.** This is ticket notation in the world. Revisit in the Phase 11 `/impeccable polish` if it reads as noise with real data. |
| 12, 13, 44 | Floor | Hero and word-count gates. | **Not applicable.** App screens have no hero (`--scope app`). |

**Totals:** 1 Floor fix, 1 Reflex waived, 3 dismissed or kept with reasons.

**Not checked:**
- The render tier (no puppeteer).
- Real-device checks on Android and iPhone; these are due before each release, per §11.3.
