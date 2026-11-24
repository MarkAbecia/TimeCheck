# 0004 — Library choices

Status: Accepted (2026-11-24)

- **No MediatR.** It is commercially licensed from v13. A ~60-line `Dispatcher` (validate → handle → audit → save) covers what we need.
- **AwesomeAssertions** instead of FluentAssertions (v8 moved to a paid license). Same API, Apache-2.0.
- **Minimal APIs**, not MVC controllers. Deny-by-default is a fallback policy; an integration test walks the live
  endpoint table and fails if anything except health/docs allows anonymous access.
- **Scalar** for the API reference UI (non-production only) on top of the built-in `Microsoft.AspNetCore.OpenApi`.
- **Microsoft.* packages pinned to 10.x** (the LTS line). NuGet now offers 11.x by default; don't take it until we move off the LTS.
- **Newtonsoft.Json pinned** explicitly: Hangfire pulls a vulnerable 11.x transitively.
