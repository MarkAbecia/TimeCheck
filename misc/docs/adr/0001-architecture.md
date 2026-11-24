# 0001 — Architecture

Status: Accepted (2026-11-24)

## Decision
- Monorepo with four project folders: `frontend/`, `backend/`, `database/`, `misc/`.
- Next.js (16.x, App Router) as UI + thin BFF (cookie handling, proxy). No business logic in the BFF.
- .NET 10 (LTS) ASP.NET Core API using Clean Architecture: Domain ← Application ← Infrastructure ← Api/Worker. Enforced by NetArchTest.
- Supabase Postgres is the only database. **Schema = plain SQL migrations in `database/supabase/migrations`**; EF Core maps onto it with Fluent config and never generates migrations.
- Hangfire (Postgres storage) in a separate Worker process for jobs; Redis for cache, rate limits, locks.
- Render hosts web, api, worker and Key Value, defined in `render.yaml`.

## Why
Keeps RLS, enums and indexes reviewable in one place; keeps heavy jobs off the request path; every service is stateless so it scales horizontally.

## Consequences
- Every schema change is a SQL migration plus an EF configuration change. Integration tests catch mapping drift.
- Use cases are plain handler classes (no MediatR) to avoid a licensed dependency; cross-cutting concerns (audit, validation) run in a small handler pipeline.
