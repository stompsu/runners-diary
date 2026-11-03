# 0011 – Guest accounts: creation, demo seeding, expiry, cleanup and abuse limits

**Status:** Accepted (M2 plan approved by the user 2026-11-03; implemented in M2)
**Date:** 2026-11-03

## Context

Brief §1.3: "Try as guest" creates an isolated temporary account with its own copy of a ~6-month demo dataset (Gliwice/Silesia, synthetic, dates relative to today), expiring after 24 h; rows **and blobs** removed by a scheduled cleanup; per-IP rate limit on creation; limits on later uploads/AI. Hosting is scale-to-zero Container Apps + serverless SQL with auto-pause (ADR 0006) — anything that wakes SQL on a timer costs free-offer vCore-seconds.

## Decision

1. **Endpoint** `POST /api/guest-sessions` (anonymous, the only anonymous write): per-client limit → global creation window → capacity check → wait for a seeding slot → **capacity re-check** (the first count may be stale after queueing) → creates `Users(Kind = Guest, TimeZoneId = Europe/Warsaw, ExpiresAt = now + 24 h)` and runs `DemoDataSeeder` for that user → `201 { accessToken, tokenType: "Bearer", expiresAt, userId }` with `Cache-Control: no-store`. Seeding is synchronous so the first calendar request already sees data. The seeder writes through its own context whose caller is **explicitly the new guest** (`FixedCurrentUser`, ADR 0010), never the request's. Failure → transaction rolled back, already-uploaded blobs deleted, 503 problem. Check-then-insert is not atomic: concurrent creations can overshoot the cap by at most `SeedingConcurrency − 1` per replica (acceptable for an abuse cap).
2. **Demo dataset as data**: `seed/demo/runs.json` (backend-dotnet) + `seed/demo/gpx/*.gpx` (maps-gpx), copied to the API output as content. Entries are **week-relative**: `{ "week": -3, "day": "Sun", … }` where week 0 is the current ISO week in `Europe/Warsaw`; ~26 past weeks + 4–6 future weeks of planned training and 1–2 planned races. Expansion (pure, in `RunnersDiary.Domain/Demo/`): entries dated after "today" become `Planned` (completion-only fields dropped); entries ≤ today keep their declared status. Guarantees (unit-tested for every "today" across a year): the current month is never empty, ≥ 3 completed races with GPX/result/rating, ≥ 1 planned race with a course GPX, one load-spike week and one taper week. As built: 122 entries, weeks −26 … +5; parsing is pure Domain (`DemoTemplateParser`), file loading is Infrastructure (`DemoDatasetLoader`). **The dataset is validated at host startup** (`DemoDatasetStartupCheck`: parses, passes `RunRules`, referenced GPX files exist, fits the guest run cap) — a broken dataset fails startup instead of every guest creation.
3. **Blobs**: each guest gets its own copy of the demo GPX files at `gpx/users/{userId:N}/tracks/{trackId:N}/original.gpx` (private container `gpx`). **All blobs of a user live under `users/{userId:N}/`**, so deleting a user's blobs is a prefix delete. `GpxTrack.Status = Pending` in M2; processing/GeoJSON arrives with the M4 pipeline (maps-gpx), which will also process the demo tracks. The API creates the container only when `Storage:CreateContainerOnStartup=true` (AppHost, tests); in Azure Bicep provisions it (M7), so the app identity needs no container-create right.
4. **Expiry**: guest JWT `exp` = `ExpiresAt` (ADR 0009) — after 24 h the token is rejected regardless of cleanup timing; the guest check additionally requires `ExpiresAt` in the future and caches a positive result for at most min(5 min, remaining lifetime). Cleanup deletes guests with `ExpiresAt < now − 1 h` (grace > clock skew so no request can race the delete).
5. **Cleanup** (`GuestCleanup.RunOnceAsync`, Infrastructure): per expired guest, batch of ≤ 50 per run: **delete blobs by prefix first, then** `ExecuteDelete` the `Users` row (FK cascades remove runs and tracks), then **evict the guest's cached "active guest" verdict** (`UserProvisioner.EvictGuest`). Blob failure ⇒ row kept ⇒ retried next run (no orphaned blobs without a record). Idempotent; safe if two replicas run it. It always runs **anonymously**: its own context with `AnonymousCurrentUser`, touching only `Users` (ADR 0010).
   - **M2: `GuestCleanupService : BackgroundService`** with `PeriodicTimer` (default 30 min, first run 1 min after start) and an opportunistic run (fire-and-forget, throttled to once per 10 min) on guest creation and when the capacity cap is hit. The opportunistic run is started with `ExecutionContext.SuppressFlow()`, so it does not inherit the triggering request's `HttpContext` or user. It only runs while an API replica is alive — i.e. when somebody is using the app and SQL is awake anyway — so it never wakes serverless SQL by itself.
   - **M7 decides** whether a Container Apps **scheduled job** (same image, `--job guest-cleanup` entry, at most daily) is also needed for low-traffic periods; each run wakes SQL, so frequency is a cost decision.
6. **Abuse limits** (all configurable, defaults in `appsettings.json`):
   - Client partition key: IPv4 address (IPv4-mapped IPv6 normalized to IPv4); **IPv6 by its /64 prefix** (one subscriber usually owns a whole /64, so per-address limits would be trivial to bypass).
   - Per-client sliding window on guest creation: **5 per hour** (policy `guest-create`; Development raises it to 100 because all local traffic is `::1`).
   - **Global backstop**: a process-wide sliding window on guest creations, **60 per hour** (Development 1000), consulted *after* the per-client policy so one client exhausting its own budget cannot burn the global one → 429 `urn:runnersdiary:problem:rate-limited` with `Retry-After`.
   - Global cap of **active guests: 200** → `503` problem `urn:runnersdiary:problem:guest-capacity` with `Retry-After`.
   - Seeding concurrency: a process-wide `SemaphoreSlim(2)`, 10 s wait → 503.
   - General per-caller limiter for all authenticated `/api` traffic (keyed by `rd_uid`, else client partition): token bucket 120 tokens, +60 per 30 s → 429 problem with `Retry-After`.
   - Request bodies on every `/api` route (guest-sessions included) ≤ 64 KB → 413 (`RequestLimits:MaxBodyBytes`).
   - **Run caps** (`RunLimits`): ≤ 500 runs per guest, ≤ 20 000 per user, ≤ 20 per date → 409 `urn:runnersdiary:problem:run-limit` (create; update that changes the date). Bounds storage per account; the demo dataset must fit the guest cap (startup check).
   - Later milestones: GPX count/size for guests (M4), 3 AI generations per guest + cached demo summaries (M6).
7. **Client IP behind proxies**: `UseForwardedHeaders` driven by config — `KnownIPNetworks` (not the obsolete `KnownNetworks`) and **`ForwardLimit` = the number of trusted proxy hops** in front of the API (default 1). **`ForwardedHeaders:Enabled` is mandatory outside Development**: if it is not set explicitly (`true` behind trusted proxies, `false` otherwise) the API refuses to start, because every per-IP limit depends on it. The AppHost sets `false` locally. **M7 must verify the X-Forwarded-For chain through the web tier and ACA ingress before go-live**, otherwise all guests share one IP bucket (or, worse, spoofed headers are trusted).

## Consequences

- A fresh guest gets a populated, internally consistent diary in one round trip (122 runs + a few small GPX copies; seconds even on a resumed SQL).
- Rate-limit counters, the global guest window, the seeding semaphore and the guest-check cache are per replica (in-memory) — acceptable; effective limits scale with the replica count, so M7 should cap `api` max replicas (e.g. 1–2), which it wants for cost anyway.
- Storage cost per guest is a few MB at most; 200 active guests ≈ < 1 GB blob, ≈ 22k rows.
- Converting a guest to a real account (brief "optional, later") stays possible: re-point `UserId` on rows and move the blob prefix.
- Shared (non-copied) demo blobs were rejected to keep "everything a user owns is under their prefix" true without exceptions.

## Alternatives considered

- **Shared read-only demo blobs** — less storage, but ownership and deletion become conditional; a guest deleting a demo track must not delete the shared blob.
- **Asynchronous seeding (202 + polling)** — needed only if seeding were slow; adds UI states.
- **Container Apps job only (no hosted service)** — runs even when idle, but wakes SQL every run (free-offer vCore budget); kept as an M7 option.
- **CAPTCHA / proof-of-work on guest creation** — stronger anti-bot, extra dependency and UX; revisit if abuse is observed.

## References (checked 2026-11-03)

- ASP.NET Core rate limiting (partitions, per-instance counters, IP spoofing caveat, `OnRejected`): https://learn.microsoft.com/aspnet/core/performance/rate-limit?view=aspnetcore-10.0 (page dated 2026-10-27)
- `KnownNetworks` obsolete → `KnownIPNetworks` (.NET 10, warning ASPDEPR005): https://learn.microsoft.com/aspnet/core/breaking-changes/10/ipnetwork-knownnetworks-obsolete
- Azure SQL free offer / auto-pause: https://learn.microsoft.com/azure/azure-sql/database/free-offer
