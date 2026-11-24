---
version: 1
slug: "src-app"
primary_target: "src/app"
related_targets: ["src/components"]
---

# App shell, Today and sign-in (TimeCheck)

Scope: the signed-in app shell (all roles), the Today screen, and the auth pages (login, forgot, reset). Mode: Operate.
Audience/job: leaders check members into today's events in seconds on a phone; members see today, their streak, and upload devotions; admins navigate everything on a laptop.
Constraints: palette pinned (REQUIREMENTS §11.1), WCAG 2.2 AA, 360px first, light/dark/system.

## Direction contract

THESIS: Every gathering is a pass you check people into. TimeCheck refuses the generic card-dashboard with a "Take attendance" button per card; the event itself is the object, and its stub is the action.

OWN-WORLD: Black shell (app bar, bottom bar, sidebar) like a lanyard; content on warm off-white card stock in light mode, near-black in dark. Event passes: white card, 6px red top band for group events, black band for church-wide, a dashed perforation with notched corners separating a right-hand stub. Gold appears only as foil: role bands (LEADER · JOSHUA CELL), Disciple badge, streak. Condensed caps (Barlow Condensed) print event names, numbers and bands; Public Sans carries everything else.

STORY: A leader opens the app, sees today's passes in time order, taps the red "Check in" stub, marks all present, toggles exceptions, saves; the stub now reads 18/20 ✓. A member sees their own passes and devotion status. An admin sees the same pass language across groups.

FIRST VIEWPORT: Phone: black app bar (church name, "powered by TimeCheck", greeting, gold role band). "Today · Tue 24 Nov" label, then passes stacked full-width with stub on the right (~96px). Red filled stub = needs check-in, the primary action. Black bottom tab bar, max 5 items. Desktop: black sidebar, passes in a 2-column grid, max width ~1280.

FORM: The Event Pass, position 6 of my ordered list; seed:d34252100f759812. Raises: time-first column rhythm (split-flap), black owns the shell (catalog sleeve), one fixed ramp: stub fill shows share checked in (star atlas), own group in focus with church-wide in the same list (transit diagram). Signature move: the stub; state restyles the stub, never the row.

FINISH: unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict, DESIGN.md, and every shipping raster carrying its provenance
