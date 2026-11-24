# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

Mobile-first web app, installable as a PWA. No native apps in v1.

## Users

- **Leaders** run one cell group (schema allows more). Their main job: take attendance for today's events in under 30 seconds on a phone, often right after a meeting, one-handed, sometimes on flaky mobile data. They also register members, schedule group events, and review devotions.
- **Members** (teens ~13+ through older adults) upload a Day and a Night devotion (text or photo of handwriting) in under 3 taps, log people they invited, and check their own attendance and streaks.
- **Admins** run church-wide events, move members between groups, promote leaders, and read reports, mostly on laptops.
- **Super Admins** (1–3 people) manage settings, Admins and the audit log.

Children under 13 do not sign in themselves. Minors 13–17 do, with guardian consent recorded.

## Product Purpose

TimeCheck tracks attendance for church cell groups (daily devotions, Morning Prayer, weekly meetings, Sunday Cell Celebration, trainings) and produces reports for leaders and admins. Success: leaders actually mark attendance every time because it is faster than not doing it; members keep their devotion streak because uploading is effortless; admins trust the numbers.

## Positioning

Built around how a cell church actually runs: every attendable thing is an event of a known type (devotions, Morning Prayer Mon–Sat, Week of Fire, Cell Celebration, Cell Meeting, PID/ILD, Mentoring, PEPSOL, RFN Mentoring, conferences), tracked per member and rolled up per cell. Daily devotions count as attendance. AI-writing detection on devotions is advisory only and never penalizes anyone.

## Operating Context

- Primary device: mid-range Android phones on 4G; also iPhones. Admin work on laptops.
- Peak load: Sunday (Cell Celebration).
- Church timezone governs "today" (default Asia/Manila).
- Attendance flow: "Mark all present", then toggle the exceptions. Offline marks queue and sync later.
- Devotion photo uploads open the camera or gallery directly.

## Capabilities and Constraints

- Roles: Super Admin, Admin, Leader, Member (Leader is a role; member statuses are New Member, Disciple, Inconsistent, Inactive).
- Attendance statuses: Present, Late, Absent, Excused, plus Unmarked shown separately.
- No public sign-up; accounts are created by leaders/admins. MFA required for admins.
- Personal data of minors is held: privacy-first defaults, RA 10173 (Philippine Data Privacy Act).
- English UI copy, written for later Filipino/Tagalog translation (next-intl).
- Stack fixed by `misc/docs/REQUIREMENTS.md`: Next.js App Router, Tailwind, shadcn/ui.

## Brand Commitments

- **Identity:** the church's own name leads; "powered by TimeCheck" follows in smaller type. The church name comes from settings (`churches.name`), so nothing is hard-coded. No church logo has been supplied yet; leave a slot for one.
- **Palette (binding, from REQUIREMENTS §11.1):** black, red and gold. Black is the base, red is the brand and primary action, gold is a sparing accent for meaning and celebration (streaks, Disciple badge, leader/admin badges, logo mark). Exact light/dark tokens and contrast rules live in §11.1 and must not regress.
- **Voice:** warm, encouraging, trustworthy. Not corporate, not gamified. Plain app copy with an occasional light, natural faith touch (e.g. "Your devotion is in — have a blessed day."). No scripture-heavy chrome.

## Evidence on Hand

- No logo, photography, testimonials or church name supplied yet. Do not invent them; use the configured church name and leave the logo slot empty.
- Demo seed data (fictional people, "Demo Church") exists for development only.

## Product Principles

1. **Faster than paper.** The leader attendance screen and member devotion screen get the most care; every extra tap is a cost.
2. **Encourage, never shame.** Streaks celebrate; missed days and AI scores are shown factually and privately, never as public failure.
3. **Trustworthy numbers.** Reports must match raw attendance exactly; protected fields are editable only by leaders/admins.
4. **Private by default.** Show personal data only to people who need it; minors' data even less.
5. **Works on a cheap phone in a weak signal.** Performance and offline tolerance are features.

## Accessibility & Inclusion

WCAG 2.2 AA. 44×44 px touch targets, visible focus, labels on all inputs, `prefers-reduced-motion` respected, body text ≥ 16 px on mobile. Color is never the only signal (attendance status and AI bands carry labels/icons; errors use a rose destructive token, not brand red). Light, Dark and System themes. Users span teens to seniors, so type must stay comfortably readable.
