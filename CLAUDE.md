# TimeCheck

Read `misc/docs/REQUIREMENTS.md` first. It is the source of truth.
Progress per phase is in `misc/docs/progress.md`; decisions are in `misc/docs/adr/`.

- Schema lives in `database/supabase/migrations` (plain SQL). EF Core maps onto it; never add EF migrations.
- Backend layer rules are enforced by `backend/tests/TimeCheck.ArchitectureTests`.
- Every endpoint requires auth except health checks.
