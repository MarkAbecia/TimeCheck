# Hallmark audit: Phase 11 (frontend/src/app)

- **Date:** 2026-11-25
- **Tool:** Hallmark 2.0.0, `audit` (report only; no project files changed)
- **Linter run:** `sloplint src --scope app --genre editorial` over 76 files. Result: 41 FAIL, 4 REFLEX, 1 WARN; 36 of the FAILs are the `.dark` token block (see below).
- **Render tier:** not run. puppeteer isn't installed, and the linter can't sign in. The judged gates were read from the browser pane at 375×812 and 1440×900 (Today, Reports, Attendance report), plus code reading.
- **Ownership:** Impeccable owns the design system (REQUIREMENTS §14). Findings that conflict with the direction contract are reported, not overruled.
- **Since Phase 3:** the Phase 3 fix (`overflow-x: clip`) holds. Everything new below comes from Phases 4 to 11.

## Read

| | |
|---|---|
| **For** | Leaders marking attendance and checking their group on a phone; admins running reports and member records. |
| **Works** | The event pass is still the one idea per screen: state and action live only in the stub. Reports carry it well: a back link, one caps title, the scope in plain words, then filters, the chart and tables that turn into cards on phones. No gradients, glows, emoji icons, invented figures or fake sends anywhere. |
| **Generated tells** | Two shadcn defaults that came in with components (`transition-all` on Button, tracked caps on the dropdown shortcut), one literal shadow, and the small tracked-caps labels the direction uses for section heads and the role band. |
| **Broken** | Nothing functional. On a phone, the report's filters fill the whole first screen, so the report itself starts below the fold. |

## Findings

### Floor

| Gate | Where | Finding | Read | Lightest fix |
|---|---|---|---|---|
| 10a | `src/components/ui/button.tsx:5` | `transition-all` on every button. Since Phase 11 the `sm` size changes height on touch screens, so `all` can also animate height. | **Real.** | Polish: `transition-[color,background-color,border-color,box-shadow,transform]`. |
| 48 | `src/components/auth/auth-card.tsx:4` | Literal shadow `shadow-[0_1px_2px_rgb(0_0_0/0.06),0_8px_24px_-12px_rgb(0_0_0/0.25)]`. | **Real**, small. | Polish: lift it to a `--shadow-card` token in `globals.css` and use the utility. |
| 67 | `src/components/ui/dropdown-menu.tsx:240` | Shortcut hint at 12px, `tracking-widest`. | **Real but unused**: no menu item shows a shortcut. | Polish: drop `tracking-widest` from the shadcn primitive. |
| 67 | `src/app/(app)/page.tsx:27` (Today section heads, 15px, 0.14em), `src/components/shell/app-shell.tsx:13` (role band, 12px, 0.12em), `src/components/event-pass.tsx:71,100` (stub "Check in" and time, 15px) | Small text in caps with wide tracking. | **Conflict, kept** as in Phase 3. The Barlow Condensed caps voice is the Event Pass direction. Hallmark allows caps where the display is caps, tracked 0.04em or less, so the lightest change that satisfies both is to drop the tracking to 0.04em and keep the caps. | Polish, if Impeccable agrees: tracking to ≤0.04em on these four. |
| 48 ×36 | `src/app/globals.css:71–106` | Colour literals in `.dark`. | **Dismissed, false positive** (as in Phase 3): `.dark` is a declared theme block. | None. |
| 20 | `src/app/globals.css:1` | No Hallmark stamp. | **Dismissed.** Hallmark didn't build this app; Impeccable's `DESIGN.md` is its system record. | None. |
| 4 | `src/components/events/attendance-sheet.tsx:110` | "Card inside a card". | **Dismissed.** It's a segmented control (P/L/A/E) inside a list row; the selected segment uses the card fill as its thumb. One containment layer. | None. |
| 63 | `src/lib/passes.ts:8,9,24`, `app-shell.tsx:11`, `messages/en.json:44` | Middle-dot meta strings. | **Kept** as ticket notation (Phase 3). Seen with real data now; they still read as one line of facts, not noise. | None. |

### Reflex

| Gate | Where | Finding | Read |
|---|---|---|---|
| 7b ×4 | `globals.css:112,114,124,150` | Pure `#fff` card, popover and primary ink. | **Waived** as in Phase 3: §11.1 pins white card stock in light mode. |
| 16 | `member-account.tsx:35`, `groups-manager.tsx:85`, `devotion-form.tsx:67`, `review-form.tsx:25` | Success toasts for changes the screen already shows (status line flips, row leaves the list, the devotion card updates). | **Real, minor.** Toasts that confirm a save which then navigates away (event, member, invite forms) or an unseen effect (offline sync, queued export) are right and stay. |

### Warn

| Gate | Where | Finding | Read |
|---|---|---|---|
| 29 | `src/components/ui/sheet.tsx:28` | `backdrop-blur-xs` over a 10% black overlay behind the More sheet. | **Dismissed.** It's a modal scrim, not a glow behind text, and it's barely visible. |

### Judged, not a gate

- **Report on a phone (375×812):** the filter card (two dates, events, Show report) and the export row fill the first screen; the chart and totals start below it. Leaders mostly open a report with its default 30 days. Suggest folding the filters into a "Filters" disclosure on phones when a report is already showing, opened by default only when the period is invalid. Impeccable `/impeccable adapt` owns this.

## Totals

- **Floor:** 3 real (10a, 48, 67 on the dropdown), 1 conflict kept (67 on the direction's caps labels), 40 dismissed with reasons.
- **Reflex:** 4 waived (7b), 1 real (16).
- **Warn:** 1 dismissed (29).
- **Lightest depth that settles everything real:** polish. Nothing here asks for a restyle or a full redesign.
- **Fixed the same day:** the three real Floor findings (Button transition, `--shadow-card` token, dropdown tracking). The linter now reports only the dismissed and kept items: 38 FAIL, all `.dark` tokens, the stamp, the segmented control and the kept caps labels.

## Not checked

- The render tier (contrast as painted, horizontal scroll at every width, text under other elements); axe in Playwright covers contrast and focus on every signed-in page.
- Admin-only screens live (they need MFA; see progress.md).
- Real Android and iPhone devices (due before each release, §11.3).
