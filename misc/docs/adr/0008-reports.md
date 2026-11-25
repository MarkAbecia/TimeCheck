# 0008 — Reports: one attendance view, a per-church cache, hand-written exports

Status: Accepted (2026-11-25).

**Decision: every attendance number in every report comes from one plain SQL view, `attendance_facts`. Built reports are cached
in Redis per church and dropped on any audited write. Exports are written by our own small CSV/XLSX/PDF writers: small ones in the
request, large ones by the Worker.**

The view (`20261125000100_reports.sql`):
- One row per person expected at a started, uncancelled event, with their group on that day and their status
  (`UNMARKED` when nobody marked them). People marked at an event they weren't expected at get rows too.
- "Expected" follows §6.6: group events take members on that day (`joined_on <= occurs_on` and not yet left); church-wide events
  without an audience take every eligible user; audience events take the audience groups' members and the named users.
- It is a plain view (`security_invoker`), not a materialized one, so a report is never stale against a mark just saved.
- The branches never overlap, so it is `union all` with no dedupe sort, and the CTE is `not materialized` so the date and church
  filters reach the base tables.
- Load test (1,000 members, a year of daily and weekly events, rolled back): a year by event type in about 270 ms, a year per
  member about 315 ms, one group's year about 620 ms, 30 days about 100 ms. `count(distinct)` in SQL cost about 1.2 s, so distinct
  counts are done in memory. All well under the 3 s target, so nothing is queued just for being slow.
- Integration tests compare each report's totals with an independent raw SQL count over the same events (AC: numbers equal raw rows).

Cache (`RedisReportCache`):
- Key `reports:{church}:{version}:{report+filters+scope}`, 10-minute TTL. Every audited request bumps `reports:ver:{church}` after
  commit, so the next view rebuilds. Changes the Worker makes without an audited request (an invite purge, say) show within the TTL.
- If Redis is down, reports are built directly. Concurrent builds of one key are merged in one API instance only.

Exports:
- `POST /reports/{type}/exports` with `csv`, `xlsx` or `pdf`. Up to 2,000 rows the file is written and uploaded in the request.
  Bigger reports queue a `report_jobs` row; the Worker's `report-exports` job builds it as the requester (re-checking their role
  and groups) and sends a `report.ready` notification.
- Files live in the private `exports` bucket. Downloads are signed links (at most 10 minutes) issued only to the requester.
  Files expire after 24 hours; the Worker deletes them and marks the job `EXPIRED`. A job stuck running for 30 minutes fails.
- Writers are hand-written (about 300 lines): CSV with a BOM and formula-injection guard, XLSX as a zip of inline strings, PDF as
  A4 landscape Helvetica with page numbers. No new dependency for three simple, tested formats.

Not done:
- Report text (titles, column names, scope lines) comes from the API in English, not next-intl.
- The PDF has tables only, no chart.
- Age brackets for the member report come from church settings (`reports.ageBrackets`) but have no settings screen yet.
