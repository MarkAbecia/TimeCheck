---
name: TimeCheck
description: Cell group attendance where every gathering is a pass you check people into.
colors:
  ground: "#faf8f3"
  ink: "#141414"
  card-stock: "#ffffff"
  surface-2: "#f1eee6"
  muted-ink: "#5f5a52"
  rule: "#e2ded3"
  pass-red: "#b3121b"
  foil-gold: "#c9a227"
  foil-gold-text: "#7a5c0e"
  destructive: "#9f1239"
  success: "#15803d"
  warning: "#b45309"
  lanyard: "#0b0b0c"
  lanyard-ink: "#f5f2ea"
  lanyard-muted: "#a39e93"
  lanyard-rule: "#2a2a2e"
  lanyard-active: "#e5484d"
  lanyard-gold: "#d4af37"
  lanyard-hover: "#1f1f22"
  band-group: "#b3121b"
  band-church: "#0b0b0c"
  stub-fill: "#dcebdf"
  chart-1: "#b3121b"
  chart-2: "#c9a227"
  chart-3: "#141414"
  chart-4: "#8a857c"
  chart-5: "#c8c3b8"
  dark-ground: "#0b0b0c"
  dark-card-stock: "#161618"
  dark-surface-2: "#1f1f22"
  dark-rule: "#2a2a2e"
  dark-pass-red: "#c8102e"
  dark-red-text: "#e5484d"
  dark-ring: "#d4af37"
  dark-lanyard: "#050506"
  dark-band-church: "#f5f2ea"
  dark-stub-fill: "#1d3324"
  dark-destructive: "#fb7185"
  dark-success: "#4ade80"
  dark-warning: "#fbbf24"
typography:
  display:
    fontFamily: "Barlow Condensed, Public Sans, sans-serif"
    fontSize: "2rem"
    fontWeight: 700
    lineHeight: 1
  headline:
    fontFamily: "Barlow Condensed, Public Sans, sans-serif"
    fontSize: "1.5rem"
    fontWeight: 700
    lineHeight: 1.05
  stub-number:
    fontFamily: "Barlow Condensed, Public Sans, sans-serif"
    fontSize: "1.875rem"
    fontWeight: 700
    lineHeight: 1
    fontFeature: "tnum"
  section-label:
    fontFamily: "Barlow Condensed, Public Sans, sans-serif"
    fontSize: "0.9375rem"
    fontWeight: 700
    letterSpacing: "0.14em"
  band:
    fontFamily: "Barlow Condensed, Public Sans, sans-serif"
    fontSize: "0.75rem"
    fontWeight: 700
    letterSpacing: "0.12em"
  body:
    fontFamily: "Public Sans, ui-sans-serif, system-ui, sans-serif"
    fontSize: "1rem"
    fontWeight: 400
    lineHeight: 1.5
  meta:
    fontFamily: "Public Sans, ui-sans-serif, system-ui, sans-serif"
    fontSize: "0.8125rem"
    fontWeight: 400
  tab-label:
    fontFamily: "Public Sans, ui-sans-serif, system-ui, sans-serif"
    fontSize: "0.6875rem"
    fontWeight: 500
rounded:
  foil: "2px"
  md: "8px"
  lg: "10px"
  sheet: "16px"
  full: "9999px"
spacing:
  pass-x: "14px"
  card-gap: "12px"
  card-gap-lg: "16px"
  section-gap: "28px"
  gutter: "16px"
  gutter-md: "24px"
  gutter-lg: "32px"
components:
  button-primary:
    backgroundColor: "{colors.pass-red}"
    textColor: "{colors.card-stock}"
    rounded: "{rounded.lg}"
    height: "44px"
    padding: "0 16px"
  button-primary-large:
    backgroundColor: "{colors.pass-red}"
    textColor: "{colors.card-stock}"
    rounded: "{rounded.lg}"
    height: "48px"
    padding: "0 20px"
  button-outline:
    backgroundColor: "{colors.ground}"
    textColor: "{colors.ink}"
    rounded: "{rounded.lg}"
    height: "44px"
  input:
    backgroundColor: "{colors.card-stock}"
    textColor: "{colors.ink}"
    rounded: "{rounded.lg}"
    height: "44px"
    padding: "8px 12px"
  event-pass:
    backgroundColor: "{colors.card-stock}"
    rounded: "{rounded.lg}"
    padding: "12px 14px 14px"
  stub-todo:
    backgroundColor: "{colors.pass-red}"
    textColor: "{colors.card-stock}"
    width: "96px"
  stub-attended:
    backgroundColor: "{colors.surface-2}"
    width: "96px"
  role-band:
    backgroundColor: "{colors.lanyard-gold}"
    textColor: "{colors.lanyard}"
    typography: "{typography.band}"
    rounded: "{rounded.foil}"
    padding: "1px 8px"
  shell:
    backgroundColor: "{colors.lanyard}"
    textColor: "{colors.lanyard-ink}"
  bottom-tab:
    textColor: "{colors.lanyard-muted}"
    typography: "{typography.tab-label}"
    height: "56px"
---

# Design System: TimeCheck

## Overview

**Creative North Star: "The Event Pass"**

Every gathering is a ticket. A black lanyard (app bar, bottom tabs, rail, sidebar) holds warm card stock; each event is a pass with a colored top band, a dashed perforation, punched notches, and a 96px stub on the right. The stub is the action and the status; the row never restyles.

Density is phone-first and calm: one column of full-width passes at 360px, two columns from `lg`. Condensed caps print names, numbers and bands; a plain humanist sans carries the rest. Red means "act here". Gold is foil, never paint. The palette is pinned by REQUIREMENTS §11.1 and checked for WCAG 2.2 AA by `scripts/contrast.mjs`; light and dark are both first-class.

**Key Characteristics:**
- Black shell in both modes; content on warm off-white (light) or near-black (dark).
- Pass anatomy: 6px band, dashed stub divider, notches in the page color.
- State lives in the stub: red fill, fill ramp, day/time, check.
- Barlow Condensed caps for print; Public Sans for reading.
- One authored motion: the stamp.

## Colors

Warm paper and black lanyard, with one red for action and gold reserved for foil.

### Primary
- **Pass Red** (pass-red): the todo stub, primary buttons, group bands, the light-mode focus ring. Dark mode shifts to dark-pass-red for fills and dark-red-text for red text.

### Secondary
- **Foil Gold** (foil-gold / lanyard-gold): role bands, Disciple badge, streak, the shell focus ring and the dark-mode focus ring. Gold text on paper uses foil-gold-text.

### Neutral
- **Warm Ground** (ground): page background, and the fill of perforation notches.
- **Card Stock** (card-stock): passes, auth card, inputs.
- **Surface Two** (surface-2): unfilled stub, attended stub, muted fills.
- **Ink / Muted Ink** (ink, muted-ink): text; muted for times, meta, section labels.
- **Rule** (rule): every border and the dashed perforation.
- **Lanyard** (lanyard, lanyard-ink, lanyard-muted, lanyard-rule, lanyard-active): the shell. Active nav icons are lanyard-active red.
- **Stub Fill** (stub-fill): the green fill ramp on a marked stub.
- **Status** (success, warning, destructive): the check mark, warnings, field errors.

### Named Rules
**The Lanyard Rule.** The shell is black in both modes; it never inverts with the theme.
**The Foil Rule.** Gold is foil on small labels and the focus ring on black. Never a fill for buttons or surfaces.
**The Reversed Band Rule.** In dark mode the church-wide band reverses to dark-band-church (light), never a grey.
**The One Ramp Rule.** A marked stub fills from the bottom with stub-fill to the share checked in, over surface-2. No other quantity uses this ramp.
**The Contrast Gate Rule.** Any palette change is made in `globals.css` and passes `npm run contrast` (4.5:1 text, 3:1 UI, bands and rings).

## Typography

**Display Font:** Barlow Condensed 600/700 (falls back to Public Sans)
**Body Font:** Public Sans (with ui-sans-serif, system-ui)

**Character:** Ticket print over plain reading text. Condensed caps state what and how many; Public Sans says everything else.

### Hierarchy
- **Display** (700, 2rem, 1): auth card titles.
- **Headline** (700, 1.5rem, 1.05, uppercase, balanced): event names on passes, empty-state titles.
- **Stub number** (700, 1.875rem, tabular): "18/20" on a done stub; the upcoming day prints at 1.625rem, its time at 0.9375rem 600.
- **Section label** (700, 0.9375rem, 0.14em, uppercase, muted): "Today · Tue 24 Nov", "Coming up". These are real headings.
- **Band** (700, 0.75rem, 0.12em, uppercase): role band and the todo stub's "Check in" (0.9375rem, 0.06em).
- **Body** (400, 1rem, 1.5): never below 16px on inputs.
- **Meta** (0.8125rem): pass time (600, tabular) and pass meta.
- **Tab label** (500, 0.6875rem): bottom tabs and rail.

### Named Rules
**The Print Rule.** Barlow Condensed is for names, numbers, bands and headings, always uppercase. Sentences and labels stay in Public Sans.
**The Tabular Rule.** Times and counts use tabular figures.

## Layout

Phone first at 360px. Main column: max 1280px, gutters 16 / 24 (md) / 32 (lg). Passes stack in one column with 12px gaps, two columns at `lg` with 16px gaps. Sections are 28px apart.

Shell by breakpoint: phone gets a black app bar (wordmark, theme toggle, avatar, greeting, role band) and a fixed bottom tab bar of at most 4 tabs plus More (a bottom sheet). `md` gets a 96px rail with a monogram; `lg` gets a 256px sidebar with wordmark and role band. Safe-area insets pad the app bar and tab bar. No horizontal page scroll (`overflow-x: clip`).

## Elevation & Depth

Flat by default. Depth comes from the black shell against the ground, the card border, and the notches. One shadow exists: the auth card, which floats alone on the ground. The selected segment in the theme control uses a minimal shadow.

### Shadow Vocabulary
- **Auth lift** (`0 1px 2px rgb(0 0 0 / 0.06), 0 8px 24px -12px rgb(0 0 0 / 0.25)`): the auth card only.

### Named Rules
**The Flat Pass Rule.** Passes carry no shadow; their border and notches are the depth.

## Shapes

Gently rounded cards and controls (10px). Role band is almost square (2px). Notches are 20px circles centered on the stub edge, top and bottom, filled with the page ground and outlined in the rule color. The stub divider is a 2px dashed line. Bands are a 6px top border clipped by the card radius. Bottom sheets round their top at 16px.

## Components

### Buttons
- **Shape:** gently rounded (10px), 44px tall (48px large, 44px icon).
- **Primary:** pass-red fill, white text; hover darkens 12% toward black.
- **Outline / Secondary / Ghost:** ground or surface-2 fills, muted hover.
- **Active:** presses down 1px. Disabled at 50% opacity.

### Inputs / Fields
- **Style:** card-stock fill, rule border, 10px radius, 44px tall, 16px text, red caret.
- **Focus:** ring-colored border plus the global outline.
- **Error:** destructive border, an alert icon and message below; form-level errors in a tinted destructive box.

### Cards / Containers
- **Auth card:** a pass without a stub: max 400px, red band, display title, auth lift.
- **Empty state:** dashed rule border on card stock, muted icon, headline title.

### Navigation
- **Bottom tabs:** 56px, lucide icon at 22px over a tab label; active is lanyard-ink with a lanyard-active icon.
- **Rail / sidebar:** 44px minimum rows; hover adds 5% white; active adds 7% white and a red icon.
- **Theme toggle:** 44px icon button on the shell opening Light / Dark / System.

### Event Pass (signature)
Grid of content plus a 96px stub; min height 96px; 6px band (group red, church black or reversed in dark).
- **todo:** red stub, "Check in" in band type, "0 of N" below. The primary action.
- **done:** fill ramp, big tabular count, "checked in", green check plus "Marked". Still a link.
- **upcoming:** "opens", day and time printed in Barlow.
- **attended:** surface-2 stub, large green check.
- **pending:** "not marked" in muted text.
- **Stamp:** the check stamps in (420ms, scale 1.6 and -12deg to rest, ease-out-expo) only when a pass was just marked. Reduced motion disables it.

## Do's and Don'ts

### Do:
- **Do** put every state change in the stub; leave the pass body untouched.
- **Do** keep touch targets at 44px or more (tabs 56px).
- **Do** use the 2px outline with 2px offset for focus: pass-red on paper (light), gold in dark and on the shell.
- **Do** list passes in time order and give future church-wide events their own "Coming up" section.

### Don't:
- **Don't** add per-card "Take attendance" buttons; the stub is the action.
- **Don't** use gold as a fill for buttons or surfaces.
- **Don't** grey out the church-wide band in dark mode.
- **Don't** add a second animation or play the stamp on page load.
- **Don't** shadow a pass.
