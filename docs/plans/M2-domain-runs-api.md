# M2 – Domain & Runs API: plan

**Status:** Implemented — awaiting CI + approval (plan approved by the user 2026-11-03; deviations in [§11](#11-implementation-notes--deviations-2026-11-03)) · **Date:** 2026-11-03 · **Author:** architect
**Builds on:** M1 (as implemented, incl. [M1 §10 deviations](M1-skeleton.md#10-implementation-notes--deviations-2026-11-03)).
**Related ADRs:** [0007](../adr/0007-api-surface-conventions.md) API conventions · [0008](../adr/0008-run-date-and-time-storage.md) date/time · **new (accepted 2026-11-03):** [0009](../adr/0009-authentication-entra-and-guest-jwt.md) auth schemes · [0010](../adr/0010-current-user-and-data-scoping.md) ICurrentUser & scoping · [0011](../adr/0011-guest-accounts-lifecycle.md) guest lifecycle · [0012](../adr/0012-request-validation.md) validation · [0013](../adr/0013-persistence-model-and-migrations.md) persistence & migrations
**Contracts:** [`data-model.md`](../data-model.md) · [`api-contract.md`](../api-contract.md) (full detail; this plan only summarizes)

## 1. Goal & acceptance criteria

**Goal (brief §4 M2):** data model (User, Run, RunType, RaceDetails, Rating, GpxTrack, Location), migrations, seeded run types, CRUD endpoints with per-user authorization, date-range query. Auth wired: Entra External ID for real users **plus** guest access (guest token endpoint, `DemoDataSeeder`, demo dataset in `seed/demo/`, 24 h expiry cleanup, rate limits).

**Out of scope:** any UI beyond regenerating the TS schema (M3: MSAL, sign-in page, calendar); GPX upload/parsing/GeoJSON/maps (M4); insights metrics beyond weekly totals (M5); AI (M6); Azure resources, Key Vault, deployment (M7); guest → user conversion; account deletion.

| # | Criterion | How verified |
|---|---|---|
| AC1 | `dotnet build` warning-free; `RunnersDiary.Domain` still has **no** package/project references. | CI; architecture test in Domain.Tests |
| AC2 | Initial migration creates the schema in `data-model.md` (clustered `(UserId, Date)`, `geography` location, rowversion) and seeds the 20 run types; AppHost applies it on start (Development only). | Integration test (schema + seed) · manual AppHost run |
| AC3 | **No endpoint answers without a valid token** except `/alive`, `/health`, `/api/health`, `POST /api/guest-sessions`; unauthenticated → 401 problem+json. Fallback policy requires an authenticated user. | Inventory test + generic 401 sweep over every mapped endpoint |
| AC4 | **Isolation**: two guests, two Entra users and guest-vs-user can never list/get/update/delete each other's runs (range list, calendar, get → 404, put → 404, delete → 404 and the row survives). | Integration isolation matrix |
| AC5 | **A fresh guest sees a populated calendar for the current month**: `POST /api/guest-sessions` → `GET /api/calendar/months/{y}/{m}` for "today" in Europe/Warsaw returns ≥ 4 runs; history contains ≥ 3 completed races with a GPX track, result and rating, and 1–2 planned future races with a course GPX. | Integration test + Domain test over 366 simulated "today" values |
| AC6 | Expired guest token → 401; cleanup removes expired guests' **rows and blobs**, leaves other guests untouched. | Integration tests (FakeTimeProvider + Azurite) |
| AC7 | Guest creation over the per-IP limit → 429 with `Retry-After`; capacity cap → 503; general limiter → 429. | Integration tests with low configured limits |
| AC8 | Runs CRUD per `api-contract.md`: 201/200/204; 400 validation problems (shape + domain rules); 409 on stale `version`; DST rules for `startUtc`. | Integration + Domain tests |
| AC9 | Entra scheme: disabled cleanly when unconfigured (guests still work, warning logged); with config it validates tokens from the configured issuer, enforces `access_as_user`, JIT-provisions users. | Integration tests with a test issuer |
| AC10 | OpenAPI document and `web/src/api/generated/schema.d.ts` regenerated and committed; CI `api-contract`, `frontend` jobs green. | CI |
| AC11 | No secrets or tenant ids committed (signing key + Entra ids only in user-secrets); reviewer sign-off. | reviewer |

## 2. Files / projects by owning agent

### backend-dotnet
```
.config/dotnet-tools.json                       # local tool: dotnet-ef (10.0.x)
Directory.Packages.props                        # + packages in §7
src/RunnersDiary.Domain/
  Users/User.cs, UserKind.cs, UnitSystem.cs
  RunTypes/RunType.cs, RunTypeCategory.cs, RunTypeCatalog.cs        # 20 seeded types (data-model.md)
  Runs/Run.cs, RunStatus.cs, Surface.cs, RunValues.cs, RunRules.cs, GeoPoint.cs,
       RunLocation.cs, RaceDetails.cs, RaceDistanceCategory.cs, RunRating.cs
  Tracks/GpxTrack.cs, GpxTrackKind.cs, GpxTrackStatus.cs
  Time/IanaTimeZone.cs (validate IANA, reject Windows ids), LocalStartResolver.cs (StartUtc, DST rules), UserToday.cs (TimeProvider + zone)
  Calendar/IsoWeeks.cs (wraps System.Globalization.ISOWeek), CalendarGrid.cs, WeeklyTotals.cs
  Demo/DemoTemplate.cs, DemoScheduleExpander.cs                     # week-relative → dated runs (pure)
  Common/DomainError.cs, DomainRuleException.cs
  (delete AssemblyMarker.cs once real types exist)
src/RunnersDiary.Infrastructure/
  Identity/ICurrentUser.cs
  Persistence/RunnersDiaryDbContext.cs          # DbSets, ICurrentUser ctor, named filter "Owner"
  Persistence/Configurations/*.cs               # User, Run (+complex Race/Rating/Location), RunType (HasData), GpxTrack
  Persistence/GeoPointConverter.cs              # GeoPoint ↔ NTS Point (SRID 4326) — or interceptor fallback (ADR 0013 §3)
  Persistence/OwnershipGuardInterceptor.cs      # ADR 0010 §5
  Persistence/DesignTimeDbContextFactory.cs
  Persistence/Migrations/<ts>_InitialSchema.cs (+ snapshot)
  Persistence/DatabaseMigrator.cs               # MigrateAsync when Database:MigrateOnStartup=true
  Storage/IBlobStore.cs, AzureBlobStore.cs, BlobPaths.cs             # upload, delete-by-prefix; container "gpx"
  Users/UserProvisioner.cs                      # JIT (tid,oid) → Users, memory-cached
  Guests/GuestOptions.cs, GuestAccountService.cs (create + capacity), DemoDataSeeder.cs, DemoDatasetLoader.cs,
         GuestCleanup.cs (RunOnceAsync), GuestCleanupService.cs (BackgroundService)
  DependencyInjection.cs                        # AddDbContext + UseSqlServer(UseNetTopologySuite) + EnrichSqlServerDbContext; blob store; guests
src/RunnersDiary.Api/
  Program.cs                                    # pipeline below; AddValidation; JSON options; map feature groups
  RunnersDiary.Api.csproj                       # + JwtBearer, EF Design (PrivateAssets); Content: ../../seed/demo/** → seed/demo/
  Auth/AuthenticationSetup.cs, EntraAuthOptions.cs, GuestAuthOptions.cs, GuestTokenIssuer.cs, HttpCurrentUser.cs, AppClaims.cs
  RateLimiting/RateLimitingSetup.cs, RateLimitingOptions.cs
  Http/ForwardedHeadersSetup.cs, ProblemTypes.cs, HourMinuteTimeOnlyConverter.cs
  OpenApi/BearerSecuritySchemeTransformer.cs, TimeOnlySchemaTransformer.cs
  Features/GuestSessions/CreateGuestSession.cs
  Features/Me/GetMe.cs, UpdateMe.cs           # as built: Features/Account/MeEndpoints.cs (§11 #7)
  Features/RunTypes/ListRunTypes.cs
  Features/Runs/RunDto.cs, RunWriteRequest.cs, RunMapping.cs, ListRuns.cs, GetRun.cs, CreateRun.cs, UpdateRun.cs, DeleteRun.cs
  Features/Calendar/GetCalendarMonth.cs
  appsettings.json / appsettings.Development.json  # non-secret defaults only (limits, issuer names, Entra:Enabled)
  openapi/runnersdiary-api.json                 # regenerated
src/RunnersDiary.AppHost/AppHost.cs             # signing-key parameter, Entra passthrough, Database__MigrateOnStartup, web port pin
seed/demo/runs.json, seed/demo/README.md        # dataset (week-relative); provenance note
tests/RunnersDiary.Api.Tests/Infrastructure/*   # fixtures updated with the test token issuer (pairs with qa-tester)
```
Pipeline order in `Program.cs`: `UseForwardedHeaders` (config-gated) → `UseExceptionHandler` → `UseStatusCodePages` → `UseAuthentication` → `UseRateLimiter` → `UseAuthorization` → endpoints. `MapGroup("/api").RequireAuthorization()`; guest-sessions `.AllowAnonymous().RequireRateLimiting("guest-create")`. Keep the `GetDocument.Insider` guard: skip options `ValidateOnStart`, migrations and hosted services during OpenAPI generation, but register auth so the security scheme appears in the document.

### maps-gpx
```
seed/demo/gpx/*.gpx       # 3–4 recorded race tracks (10K road Gliwice, half marathon, trail race in Silesia, optional XC)
                          # + 1–2 planned race courses (no <time>); synthetic, no personal data, no DTD, ≤ 300 KB each
seed/demo/README.md       # (joint) provenance: generated, coordinates around Gliwice/Silesia, licence of any base data
```
M2 needs only valid, small files; parsing/metrics/GeoJSON are M4. maps-gpx also decides in M4 whether the seeder rewrites trackpoint timestamps to the shifted dates.

### frontend-react
```
web/src/api/generated/schema.d.ts   # regenerated only (`npm --prefix web run generate:api`); no UI work in M2
```

### qa-tester
```
tests/RunnersDiary.Domain.Tests/                 # new xUnit v3 project (MTP), added to RunnersDiary.slnx
  Time/*, Calendar/*, Runs/RunRulesTests.cs, RunTypes/CatalogTests.cs, Demo/*, ArchitectureTests.cs
tests/RunnersDiary.Api.Tests/
  Infrastructure/TestTokens.cs (RSA test key, Entra-like + guest token minting), TestClientIpStartupFilter.cs,
                 ContainersApiFactory.cs (+ test Entra config, migrations, FakeTimeProvider), OfflineApiFactory.cs (+ Entra disabled, key)
  Security/EndpointAuthorizationTests.cs (extended), UnauthenticatedSweepTests.cs, TokenValidationTests.cs
  Runs/*, Calendar/*, Me/*, RunTypes/*, Isolation/IsolationMatrixTests.cs
  Guests/GuestSessionTests.cs, GuestCleanupTests.cs, RateLimitTests.cs
  Persistence/SchemaTests.cs
```

### azure-infra
Nothing in M2 (CI unchanged: Domain.Tests is picked up by `dotnet test` on the solution). Notes for M7 are in §5.

## 3. Contracts (summary)

**Schema** — see [`data-model.md`](../data-model.md): `Users`, `RunTypes` (seeded, ids are contracts), `Runs` (one wide row: ADR 0008 time columns, SI metrics, optional complex `Location` (`geography`), `Race`, `Rating`; `rowversion`; clustered `(UserId, Date)`), `GpxTracks` (metadata + blob name; metrics deferred to M4). Cascades: `Users → Runs → GpxTracks`. Blobs: container `gpx`, everything under `users/{userId:N}/`.

**Endpoints** — see [`api-contract.md`](../api-contract.md): `POST /api/guest-sessions` (anon, rate-limited), `GET/PUT /api/me`, `GET /api/run-types`, `GET /api/runs?from&to` (≤ 92 days), `GET/PUT/DELETE /api/runs/{id}`, `POST /api/runs`, `GET /api/calendar/months/{year}/{month}` (Monday-first grid + ISO week totals). One `RunDto` for list and detail; `version` for optimistic concurrency (409).

**Auth** ([ADR 0009](../adr/0009-authentication-entra-and-guest-jwt.md)) — schemes `Entra`, `Guest`, default policy scheme `Smart` (routes by unvalidated `iss`); default + **fallback policy = authenticated user**. Config keys (non-secret unless noted):

| Key | Example / default | Source |
|---|---|---|
| `Auth:Entra:Enabled` | `true` when TenantId set; tests/pre-tenant: `false` | AppHost / test factory |
| `Auth:Entra:TenantId` | `<tenant GUID>` | AppHost user-secrets → env |
| `Auth:Entra:TenantSubdomain` | `runnersdiary` | AppHost user-secrets → env |
| `Auth:Entra:Audience` | `<API application (client) id>` | AppHost user-secrets → env |
| `Auth:Entra:ValidIssuers` | `["https://<tenantId>.ciamlogin.com/<tenantId>/v2.0"]` (derived default) | derived; override only if the metadata says otherwise |
| `Auth:Entra:RequiredScope` | `access_as_user` | appsettings |
| `Auth:Guest:SigningKey` | **secret**, string ≥ 32 UTF-8 bytes (§11 #8) | Aspire secret parameter `guest-token-signing-key` (persisted to AppHost user-secrets) → `Auth__Guest__SigningKey`; Key Vault in M7 |
| `Auth:Guest:Issuer` / `Audience` | `runnersdiary-guest` / `runnersdiary-api` | appsettings |

**ICurrentUser** ([ADR 0010](../adr/0010-current-user-and-data-scoping.md)) — `IsAuthenticated`, `UserId`, `UserIdOrNull`, `IsGuest`; from claim `rd_uid` set by the scheme's `OnTokenValidated` (Entra: JIT `(tid, oid)` → `Users.Id`; Guest: `sub` after a `Kind = Guest` check). DbContext (non-pooled) applies named filter `"Owner"` on `Run` and `GpxTrack`; inserts take `UserId` from `ICurrentUser`; an interceptor asserts ownership. Foreign → 404.

**Guests** ([ADR 0011](../adr/0011-guest-accounts-lifecycle.md)) — token `exp` = account `ExpiresAt` (24 h); synchronous seeding from `seed/demo/`; cleanup = blobs by prefix, then `ExecuteDelete` user (cascade), as a hosted service (M2) with an M7 option for a Container Apps job. Limits (config): 5 guest creations/IP/hour, 200 active guests, seeding concurrency 2, general token bucket 120 (+60/30 s) per caller.

**Demo dataset** (`seed/demo/runs.json`, sketch):
```json
{ "version": 1, "timeZoneId": "Europe/Warsaw",
  "runs": [
    { "week": -3, "day": "Sun", "startTime": "08:30", "type": "long", "status": "Completed",
      "distanceMeters": 21000, "durationSeconds": 7020, "elevationGainMeters": 85, "rpe": 6, "avgHeartRate": 148,
      "surface": "Road", "location": { "latitude": 50.2945, "longitude": 18.6714, "name": "Park Chrobrego, Gliwice" } },
    { "week": -9, "day": "Sun", "type": "roadRace", "status": "Completed", "durationSeconds": 2790, "distanceMeters": 10000,
      "race": { "name": "…", "distanceCategory": "TenK", "resultTimeSeconds": 2790, "overallPlace": 87 },
      "rating": { "stars": 4, "comment": "…" }, "gpx": { "file": "gpx/gliwice-10k.gpx", "kind": "RecordedTrack" } }
  ] }
```
`week` 0 = current ISO week in the dataset zone; entries after "today" become Planned. Validated by `RunRules` in a unit test.

**Validation** ([ADR 0012](../adr/0012-request-validation.md)) — `AddValidation()` + DataAnnotations on request records; cross-field rules in `RunnersDiary.Domain/Runs/RunRules` via `IValidatableObject`; unknown JSON members rejected.

**OpenAPI / TS** — new operations get explicit `WithName(operationId)`, `Produces*`/`ProducesProblem`/`ProducesValidationProblem`; bearer security scheme added by a document transformer; `TimeOnly` documented as `HH:mm`. After endpoint changes: `dotnet build src/RunnersDiary.Api && npm --prefix web run generate:api`, commit both. Frontend code is unchanged in M2 (`client.ts` auth middleware is M3); `npm run typecheck` must still pass with the new schema.

**AppHost** — `api`: `.WithEnvironment("Database__MigrateOnStartup", "true")`, `.WithEnvironment("Auth__Guest__SigningKey", guestKey)` where `guestKey = AddParameter("guest-token-signing-key", new GenerateParameterDefault { MinLength = 44 }, secret: true, persist: true)` (verify overload in 13.6.4; fallback: plain secret parameter + documented `dotnet user-secrets set` command); Entra values read from AppHost configuration section `Entra` (user-secrets) and passed as `Auth__Entra__*` **only when present**, otherwise `Auth__Entra__Enabled=false` (no dashboard prompt while the tenant doesn't exist). `web`: pin the browser-facing port to **5173** (`WithEndpoint("http", e => e.Port = 5173)`, verify) so Entra redirect URIs are stable (M3).

## 4. Implementation steps (each = one reviewable commit)

1. **backend-dotnet – Domain.** Catalog, entities/value objects, `RunRules`, time (IANA validation, DST resolver, user today), ISO weeks, calendar grid, weekly totals, demo expander. **qa-tester** writes Domain.Tests alongside (project must contain tests in the same commit — MTP fails on zero tests).
2. **backend-dotnet – Persistence spike + schema.** NTS + converter spike for `geography` in an optional complex type (fallback per ADR 0013 §3; fallback 2: owned 1:1 tables); configurations, `HasData`, `Owner` filter, interceptor, design-time factory, `dotnet-ef` tool manifest, `InitialSchema` migration, `DatabaseMigrator`; AppHost `Database__MigrateOnStartup`. SchemaTests.
3. **backend-dotnet – Auth & cross-cutting.** Schemes + `Smart` selector, fallback policy, `HttpCurrentUser`, JIT provisioning, guest token issuer, JSON options, forwarded headers, rate limiter, problem types; AppHost parameters; update both test factories (test issuer, Entra disabled offline). Extend inventory test; 401 sweep.
4. **backend-dotnet – Features.** run-types, me, runs CRUD, list, calendar; validation; OpenAPI transformers; regenerate OpenAPI + TS schema.
5. **maps-gpx (parallel from step 1)** – demo GPX files + provenance.
6. **backend-dotnet – Guests.** `runs.json`, loader, seeder (rows + blob copies), guest-sessions endpoint, capacity, cleanup (+ hosted service), blob store with container creation in Development/tests.
7. **qa-tester** – isolation matrix, token tests, guest/cleanup/rate-limit tests, full run of all suites (Docker on; Rosetta for SQL on Apple Silicon); AC checklist.
8. **reviewer** – authZ/IDOR, token handling, rate limits, secrets, ADR consistency. **architect** – update docs for deviations; main agent updates CLAUDE.md (status, `dotnet tool restore`, user-secrets commands) and commits "M2: domain & runs API"; stop for approval.

## 5. Risks

| Risk | Impact | Mitigation |
|---|---|---|
| EF value converter to NTS `Point`, and spatial inside an EF 10 optional complex type, are **unverified** | Schema design churn | Step-2 spike first; documented fallbacks (shadow property + interceptors; owned 1:1 tables). |
| CIAM issuer format differs from the documented default (community reports of ciamlogin vs login.microsoftonline issuers) | Real Entra tokens rejected in M3 | `ValidIssuers` is configuration; user copies `issuer` from the tenant's metadata document (§10 step 8); swap to `Microsoft.Identity.Web` is localized. |
| Guest HS256 key leak | Forged guest tokens | Kind=Guest DB check (cannot impersonate real users), 24 h exp, key rotation list, Key Vault in M7. |
| Per-IP limits behind the web proxy in Azure see the proxy IP | All guests share one bucket (M7) | `UseForwardedHeaders` with `KnownIPNetworks`; **M7 must verify XFF through web tier + ACA ingress before go-live**. |
| In-memory rate-limit counters per replica | Limits × replicas | Cap `api` max replicas in M7. |
| Hosted cleanup only runs while a replica is alive (scale-to-zero) | Expired guests linger while idle | Harmless (tokens already expired); opportunistic run on guest creation; M7 may add a daily job (each run wakes serverless SQL — cost). |
| Synchronous seeding on a resuming serverless SQL | Slow first "Try as guest" (tens of seconds) | M3 shows a "waking up" state; seeding concurrency cap; single `SaveChanges`. |
| Container image without `tzdata` (e.g. chiseled) | IANA ids fail in Azure | M7 picks an image with tzdata (or `-extra`); Domain test asserts `Europe/Warsaw` resolves. |
| `TimeOnly` JSON default is `HH:mm:ss` | Contract mismatch with ADR 0008 | Custom converter + schema transformer; contract test. |
| Demo dataset authoring effort (~110 entries + GPX) | Schedule | Week-relative compact JSON; a unit test guards the guarantees; maps-gpx works in parallel. |
| `GetDocument.Insider` build-time run now registers auth/options | Build breaks | Keep guard; skip `ValidateOnStart`/hosted services there; CI api-contract job catches it. |
| Aspire `GenerateParameterDefault` overload / Vite endpoint port pin not verified on 13.6.4 | Local setup friction | Fallbacks documented (user-secrets command; register an extra redirect URI). |

## 6. Test plan outline (qa-tester)

**Domain.Tests (fast, no I/O)**
- `LocalStartResolver`: normal; DST gap `2026-03-29 02:30 Europe/Warsaw` → `03:30` local (`01:30Z`); ambiguous `2026-10-25 02:30` → earlier occurrence (`00:30Z`); `StartTime = null` → `null`; `America/New_York` sample.
- `IanaTimeZone`: accepts `Europe/Warsaw`, `Etc/UTC`; rejects `Central European Standard Time`, empty, garbage.
- ISO weeks: 2026-01-01 → 2026-W01; 2027-01-01 → 2026-W53; 2024-12-30 → 2025-W01; Sunday belongs to the preceding Monday's week.
- `CalendarGrid`: Feb 2021 = 4 weeks; a 6-week month; grid always Monday→Sunday.
- `WeeklyTotals`: planned vs completed split; cross-training and rest excluded from distance/time.
- `RunRules`: one test per rule in `api-contract.md`; boundary values.
- `RunTypeCatalog`: 20 entries, unique ids/codes, categories/flags as in `data-model.md`.
- Demo: parse the real `seed/demo/runs.json`; every entry passes `RunRules`; for each "today" in a 366-day window (incl. 1st/last of months, DST days): current month ≥ 4 runs, no Completed after today, ≥ 3 completed races with GPX + rating, ≥ 1 planned race with course GPX, one spike week (≥ 1.3 × mean of the preceding 4 weeks) and one taper week; referenced GPX files exist.
- Architecture: `RunnersDiary.Domain` references only framework assemblies.

**Api.Tests (WebApplicationFactory + Testcontainers SQL + Azurite; skip locally without Docker, fail when `CI=true`)**
- Inventory: allowlist = `/alive`, `/health`, `/api/health`, `/api/guest-sessions` (each `IAllowAnonymous`); every other endpoint has `IAuthorizeData`; fallback policy non-null and requires an authenticated user; guest-sessions carries rate-limit policy `guest-create`.
- **401 sweep**: enumerate `EndpointDataSource`, substitute route values (GUID, 2026, 11), call every non-allowlisted endpoint without a token → 401 `application/problem+json` + `WWW-Authenticate: Bearer` (offline factory; auth fails before any DB access).
- Token validation → 401: expired guest token; guest token signed with another key; wrong issuer/audience; `alg: none`; Entra-like token without `access_as_user`; guest-key token whose `sub` is a real user's id; guest token for a deleted guest.
- **Isolation matrix** (guest A, guest B via the real endpoint; Entra users U1, U2 via the test issuer): for every pair × {list range, calendar month, get, put, delete}: foreign get/put/delete → 404 and data unchanged; lists/calendars contain only own runs; JIT creates distinct `Users` rows for U1/U2; repeated U1 token reuses the row.
- Runs CRUD: create 201 + `Location`; get; update with correct `version` 200 / stale 409; delete 204 then 404; validation 400 samples (rating on Planned, race on non-race type, HR order, Windows zone id, unknown property `userId`, `to < from`, span > 92 days); `startUtc` derived; `HH:mm` round trip.
- Calendar: grid bounds and week totals for a known dataset.
- Me: guest `kind = Guest`, `guestExpiresAt`, `today` in Europe/Warsaw (FakeTimeProvider); `PUT /api/me` updates zone; existing runs keep their zone.
- Guests: fresh guest → current month populated (AC5); blobs exist under `users/{id:N}/`; two guests have disjoint data; capacity 503 with small `MaxActive`.
- Cleanup: advance FakeTimeProvider past `ExpiresAt + grace`, run `GuestCleanup.RunOnceAsync()` → user, runs, tracks and blobs gone; non-expired guest and Entra users intact; blob-delete failure keeps the row (fault-injected `IBlobStore`).
- Rate limits: factory with `GuestCreate.PermitLimit = 2` and the test client-IP header → 3rd call 429 + `Retry-After`; a different IP still 201; general limiter 429 with a tiny bucket.
- Schema: run types seeded (20); `Runs.Location_Point` is `geography`; clustered index on `(UserId, Date)`.
- OpenAPI: document contains every operationId in `api-contract.md`, bearer security scheme, guest-sessions without security requirement.

**Frontend**: `typecheck`, `lint`, `test`, `build`, `test:e2e` unchanged and green with the regenerated schema.
**Manual**: AppHost up → `POST /api/guest-sessions` via the Aspire dashboard/HTTP file → `GET /api/calendar/months/{current}` with the token returns data; restart AppHost → schema persists, signing key stable (persisted parameter).

## 7. Dependencies

### NuGet (central versions in `Directory.Packages.props`; EF packages on the **same EF Core 10 patch** that `Aspire.Microsoft.EntityFrameworkCore.SqlServer` 13.6.4 resolves — check with `dotnet list package --include-transitive`)
| Package | Project | Why |
|---|---|---|
| `Microsoft.EntityFrameworkCore.SqlServer.NetTopologySuite` (10.0.x) | Infrastructure | `geography` mapping via NTS (`UseNetTopologySuite()`), brief mandates `geography`. |
| `Microsoft.EntityFrameworkCore.Design` (10.0.x, `PrivateAssets=all`) | Api (startup project for `dotnet ef`) | Migrations tooling at design time only. |
| `dotnet-ef` (10.0.x, local tool in `.config/dotnet-tools.json`) | tool | Reproducible `migrations add` across machines/CI. |
| `Microsoft.AspNetCore.Authentication.JwtBearer` (10.0.14) | Api | Validates Entra and guest JWTs; brings `Microsoft.IdentityModel.JsonWebTokens` used to mint guest tokens. |
| `Microsoft.Extensions.TimeProvider.Testing` (10.x) | Domain.Tests, Api.Tests | `FakeTimeProvider` for "today", expiry and cleanup tests. |

Built-in, no package: rate limiting, forwarded headers, `AddValidation()` (shared framework), `System.Globalization.ISOWeek`, `TimeZoneInfo`.
Rejected: `Microsoft.Identity.Web` (ADR 0009), `FluentValidation` (ADR 0012), NetArchTest (one reflection test suffices).
npm: **none**.

## 8. Assumptions

1. The Entra external tenant may not exist during M2; all automated tests use the test issuer; real-token verification happens in M3.
2. Guest token lifetime = account lifetime (24 h), no refresh (confirmed by the user, §9).
3. Guests use `Europe/Warsaw`; new Entra users start at `Etc/UTC` with `timeZoneIsDefault = true` until the SPA sets the browser zone (M3).
4. Weekly totals count running-distance types only; cross-training/load appear in M5.
5. Status can move Planned ↔ Completed via `PUT`; a rating requires Completed.
6. Weather and gear are free text in MVP.
7. `/api/calendar/months/{y}/{m}` is the M3 calendar's single data call; day/month lists use `/api/runs?from&to`.
8. Local Docker SQL (Rosetta) keeps working as in M1; spatial types work in the Linux SQL Server 2022 container.
9. No Azure resources are created in M2.

## 9. Open questions — answered by the user (2026-11-03)

1. **Guest token**: one 24 h token, no refresh endpoint (as planned).
2. **Abuse defaults**: kept — 5 guest creations / IP / hour, max 200 active guests, general 120-request burst per caller.
3. **Entra sign-in method**: **email one-time passcode** (passwordless); collect *Display Name* only; social sign-in may come later.
4. **Default zone for new Entra users**: `Etc/UTC` until the SPA sets the browser zone (M3).
5. **Weekly totals**: running types only; cross-training/load in M5.
6. **`UnitSystem`**: kept in the profile now (UI stays metric).
7. **Road-race sub-types**: `race.distanceCategory` on one road-race run type (as planned).
8. **Naming**: `runners-diary-api`, `runners-diary-web`, tenant subdomain `runnersdiary` (if available).

## 10. Entra External ID checklist (user does this; ~30 min)

Verified against Microsoft Learn on 2026-11-03 (links in ADR 0009 and below). Items marked ⚠ could not be fully verified.

1. **Create the external tenant** — [entra.microsoft.com](https://entra.microsoft.com) → *Entra ID → Overview → Manage tenants → Create → External → Continue*. Choose *Use Azure subscription* (or the 30-day trial, which needs no subscription). Basics: **Tenant name** (e.g. `Runners Diary`), **Domain name** (e.g. `runnersdiary` → this becomes the **subdomain** `runnersdiary.ciamlogin.com`), **Country/Region** (cannot change later). Subscription + resource group → *Review + Create* (can take up to 30 min). Requires the *Tenant Creator* role. ⚠ Pricing (free MAU allowance) not re-verified.
2. **Switch** to the new tenant (Settings → *Directories + subscriptions* → Switch); note **Tenant ID** and **Primary domain** from *Tenant overview*.
3. **Register the API** — *Entra ID → App registrations → New registration*: name `runners-diary-api`, *Single tenant only*, no redirect URI → Register. Note **Application (client) ID** (= API audience).
   - *Expose an API* → *Add* Application ID URI → accept `api://<api-client-id>` → Save.
   - *Add a scope*: name **`access_as_user`**, Who can consent: *Admins only* (customer users can't consent in external tenants anyway), admin display name/description "Access Runners Diary as the signed-in user", State Enabled.
   - *Manifest* → set `"api": { …, "requestedAccessTokenVersion": 2 }` → Save (v2 access tokens; `aud` = API client id).
   - Assign yourself as **Owner** (docs: otherwise the API may not be listed under *My APIs*).
4. **Register the SPA** — New registration: `runners-diary-web`, *Single tenant only*; *Authentication → Add a platform → Single-page application*, redirect URI **`http://localhost:5173/`** (Vite standalone and, after the M2 port pin, the Aspire-launched web). Add the Azure URL `https://<web-fqdn>/` in M7. No client secret, no implicit grant. Note the SPA **Application (client) ID**.
5. **API permissions on the SPA** — *Add a permission → My APIs → runners-diary-api → Delegated → access_as_user*; then **Grant admin consent for <tenant>** (also covers the default Graph `User.Read`; in external tenants admin consent is required). Optional: on the API's *Expose an API → Authorized client applications*, add the SPA client id for `access_as_user`.
6. **User flow** — *Entra ID → External Identities → User flows → New user flow*: name `SignUpSignIn`, identity provider *Email Accounts* → **Email one-time passcode** (decided 2026-11-03), user attributes: *Display Name* → Create. If the option is greyed out, first enable *Email one-time passcode* under *External Identities → All identity providers → Built-in → Email Accounts* (⚠ portal path not re-verified).
7. **Link the SPA to the flow** — open the user flow → *Use → Applications → Add application* → `runners-diary-web` → Select. (Only the SPA; the API doesn't sign users in. One app can have only one user flow.)
8. **Read the issuer** — open `https://<subdomain>.ciamlogin.com/<tenant-id>/v2.0/.well-known/openid-configuration` in a browser and copy the `issuer` value. ⚠ Expected `https://<tenant-id>.ciamlogin.com/<tenant-id>/v2.0`; community threads report variants — send me the exact value.
9. **Store the non-secret IDs locally (never committed)**:
   ```bash
   dotnet user-secrets set "Entra:TenantId"        "<tenant GUID>"         --project src/RunnersDiary.AppHost
   dotnet user-secrets set "Entra:TenantSubdomain" "<subdomain>"           --project src/RunnersDiary.AppHost
   dotnet user-secrets set "Entra:ApiClientId"     "<API client id>"       --project src/RunnersDiary.AppHost
   dotnet user-secrets set "Entra:SpaClientId"     "<SPA client id>"       --project src/RunnersDiary.AppHost   # used from M3
   dotnet user-secrets set "Entra:Issuer"          "<issuer from step 8>"  --project src/RunnersDiary.AppHost   # only if it differs from the default
   ```
   The AppHost maps them to the API (`Auth__Entra__TenantId`, `__TenantSubdomain`, `__Audience`, `__ValidIssuers__0`) and, in M3, to the web app (SPA client id, authority `https://<subdomain>.ciamlogin.com/`, scope `api://<api-client-id>/access_as_user`). In Azure (M7) they become azd environment values / Bicep parameters.

| Value | Where it comes from | Used by |
|---|---|---|
| Tenant ID | Tenant overview | API (authority, issuer) |
| Tenant subdomain | Domain name chosen in step 1 | API authority host; SPA authority |
| API client id (= audience) | API registration overview | API audience; SPA scope prefix |
| Scope `api://<api-client-id>/access_as_user` | Expose an API | SPA token requests (M3) |
| SPA client id | SPA registration overview | MSAL (M3) |
| Authority (SPA) `https://<subdomain>.ciamlogin.com/` | derived | MSAL (M3) |
| Metadata (API) `https://<subdomain>.ciamlogin.com/<tenant-id>/v2.0` | derived | API JwtBearer |
| Issuer | step 8 | API JwtBearer `ValidIssuers` |

No client secrets are needed (SPA = public client; API only validates tokens).

**Docs checked (2026-11-03):** [create external tenant](https://learn.microsoft.com/entra/external-id/customers/how-to-create-external-tenant-portal) · [register app + admin consent in external tenants](https://learn.microsoft.com/entra/identity-platform/quickstart-register-app) · [expose an API](https://learn.microsoft.com/entra/identity-platform/quickstart-configure-app-expose-web-apis) · [manifest `requestedAccessTokenVersion`](https://learn.microsoft.com/entra/identity-platform/reference-microsoft-graph-app-manifest) · [user flow](https://learn.microsoft.com/entra/external-id/customers/how-to-user-flow-sign-up-sign-in-customers) · [add app to user flow](https://learn.microsoft.com/entra/external-id/customers/how-to-user-flow-add-application) · [React SPA, external tenant MSAL authority](https://learn.microsoft.com/entra/identity-platform/tutorial-single-page-app-react-prepare-app) · [ASP.NET Core API, ciamlogin `Instance`](https://learn.microsoft.com/entra/identity-platform/tutorial-web-api-dotnet-core-build-app) · [multiple auth schemes (ASP.NET Core 10)](https://learn.microsoft.com/aspnet/core/security/authorization/limitingidentitybyscheme?view=aspnetcore-10.0) · [rate limiting (10.0)](https://learn.microsoft.com/aspnet/core/performance/rate-limit?view=aspnetcore-10.0) · [`KnownIPNetworks`](https://learn.microsoft.com/aspnet/core/breaking-changes/10/ipnetwork-knownnetworks-obsolete) · [validation (10.0)](https://learn.microsoft.com/aspnet/core/fundamentals/validation?view=aspnetcore-10.0) · [EF Core 10 what's new](https://learn.microsoft.com/ef/core/what-is-new/ef-core-10.0/whatsnew) · [Aspire EF Core SQL Server connect](https://aspire.dev/integrations/databases/efcore/sql-server/sql-server-connect/) · [Aspire parameters](https://aspire.dev/fundamentals/external-parameters/).
**Not verified online:** EF value converter to NTS `Point` / spatial in complex types; exact CIAM `issuer` string; Aspire 13.6.4 C# `GenerateParameterDefault` overload and Vite endpoint port pinning; External ID pricing; `oid`+`tid` guidance page (from memory: [access token claims](https://learn.microsoft.com/entra/identity-platform/access-token-claims-reference)). *(Converter, complex-type spatial, `GenerateParameterDefault` and the port pin were verified by implementation — see §11.)*

## 11. Implementation notes & deviations (2026-11-03)

| # | Deviation from this plan | Reason |
|---|---|---|
| 1 | **EF spike succeeded**: `GeoPoint` → NTS `Point` value converter inside the optional complex type `Location` works on a `geography` column; no interceptor/owned-table fallback (ADR 0013 §3). | Verified by schema + round-trip integration tests. |
| 2 | Non-pooled `AddDbContext` + `EnrichSqlServerDbContext` instead of Aspire's `AddSqlServerDbContext`. | `AddSqlServerDbContext` pools contexts; pooling is incompatible with the `ICurrentUser` constructor dependency (ADR 0010 §4). |
| 3 | Entity ids are `Guid.CreateVersion7()` set in the domain factories (`ValueGeneratedNever`), not EF's sequential GUID generator. | Ids exist before `SaveChanges` (blob paths, `Location` header); v7 GUIDs are time-ordered. |
| 4 | `.editorconfig` marks `Persistence/Migrations/*.cs` as `generated_code = true`. | Scaffolded migrations would otherwise break `TreatWarningsAsErrors`/analyzers. |
| 5 | EF packages (`…SqlServer.NetTopologySuite`, `…Design`, `dotnet-ef`) pinned to **10.0.12**. | The EF Core patch Aspire 13.6.4 resolves; mixing patches fails at runtime. Bump together with Aspire. |
| 6 | `IanaTimeZone` additionally requires the **Area/Location** form (`Europe/Warsaw`, `Etc/UTC`, `America/Argentina/Buenos_Aires`) or bare `UTC`/`GMT` before resolving. | .NET on macOS/Linux (ICU) also resolves Windows ids such as `Central European Standard Time`; a format check is the only portable rejection. |
| 7 | `Features/Me` → **`Features/Account`** (`MeEndpoints.cs`; routes unchanged: `/api/me`). | CA1716: `Me` clashes with a VB reserved word in namespaces. |
| 8 | Guest signing key = **UTF-8 bytes of the configured string, ≥ 32 bytes** (not base64-decoded). AppHost parameter: `GenerateParameterDefault { MinLength = 44, Special = false }`, `secret: true`, `persist: true` (verified on Aspire 13.6.4; persisted to AppHost user-secrets). | Aspire generates printable strings, not base64; letters/digits only avoid escaping issues in env files. 44 chars ≈ 262 bits of entropy. |
| 9 | Web endpoint pinned to **5173** via `WithEndpoint("http", e => e.Port = 5173)` — verified. | Stable Entra redirect URI (§10 step 4). |
| 10 | Unmatched `/api/*` routes → **401** without a token (fallback policy runs first), **404** with a valid token. | Consequence of the fallback policy; avoids route discovery by anonymous callers. Documented in `api-contract.md`. |
| 11 | Malformed/missing/unknown-member JSON → 400 validation problem with a **fixed generic message** keyed by the JSON path (`body` when none); binding failures keyed by the parameter name with a fixed message (`ApiExceptionHandler`, `ThrowOnBadRequest = true`). DataAnnotations error keys rewritten to camelCase JSON paths (`ValidationErrorKeys`). | Framework/`System.Text.Json` messages leak internal type names; one key style for every 400. |
| 12 | Anonymous endpoints are split into two groups: health (`AllowAnonymous().DisableRateLimiting()`) and guest-sessions (own anonymous group with `RequireRateLimiting("guest-create")`). | `DisableRateLimiting()` on a group wins over an endpoint's policy, so guest-sessions cannot share the health group. |
| 13 | Demo dataset parsing is pure Domain (`Demo/DemoTemplateParser`); the file loader is Infrastructure (`DemoDatasetLoader`); the dataset is validated at host startup (`DemoDatasetStartupCheck`: parse, `RunRules`, GPX files exist, within guest run caps). `seed/demo/runs.json` = **122 entries**, weeks **−26 … +5**. | Broken seed data fails fast at startup rather than on every "Try as guest". |
| 14 | Blob container `gpx` is created at startup **only when `Storage:CreateContainerOnStartup=true`** (AppHost and tests set it). | The app identity in Azure should not need container-create rights; **M7 Bicep must provision `gpx`**. |
| 15 | `DELETE /api/runs/{id}` does not delete track blobs (`TODO(M4)` in `DeleteRun.cs`). | No user uploads exist in M2; demo blobs go with guest cleanup (prefix delete). M4 adds blob deletion with the upload pipeline. |
| 16 | `AppHost` also sets `ForwardedHeaders__Enabled=false` and `Storage__CreateContainerOnStartup=true`. | Required by #14 and the review fix "ForwardedHeaders mandatory outside Development". |

**Verified (2026-11-03):** Release build 0 warnings; **689/689 .NET tests** with `CI=true` and Docker (Domain, Api with Testcontainers SQL + Azurite); web lint, typecheck, Prettier, Vitest 24/24, build, Playwright 3/3; no OpenAPI/TS contract drift.

**Review follow-ups applied before commit:**
run caps — 409 `urn:runnersdiary:problem:run-limit` on create and on a date-changing update (guests 500, users 20 000, 20 per date; `RunLimits` section); 64 KB body limit on every `/api` route incl. guest-sessions → 413 (`RequestLimits:MaxBodyBytes`); per-client partitioning by IPv4 address or **IPv6 /64**, plus a **global** guest-creation sliding window (60/h; consulted after the per-client policy); `ForwardedHeaders:Enabled` must be set explicitly outside Development (`ForwardLimit` = trusted hop count, `KnownIPNetworks`); the guest check requires `Kind = Guest` **and** an unexpired `ExpiresAt`, the positive cache entry lives ≤ min(5 min, `ExpiresAt`) and is evicted by cleanup; guest capacity is re-checked after a seeding slot is acquired; the ownership guard rejects owned-entity writes from contexts without a caller (seeder passes an explicit `FixedCurrentUser`, cleanup uses an explicit anonymous context); opportunistic cleanup runs with `ExecutionContext.SuppressFlow()`; composite FK `GpxTracks(RunId, UserId)` → `Runs(Id, UserId)` (`AK_Runs_Id_UserId`); any partial Entra config fails startup (Development too, unless `Auth:Entra:Enabled=false`); `PUT` without `timeZoneId` keeps the run's zone; `TotalsDto.durationSeconds` is `int64`; fixed messages for binding errors; `/openapi/v1.json` is mapped (anonymous) in Development only; the `Smart` selector falls through to the default scheme (→ 401) on any token-parse error.

**Still open for later milestones:** M7 — verify the X-Forwarded-For chain (web tier + ACA ingress) and set `ForwardedHeaders` (`Enabled=true`, `KnownIPNetworks`, `ForwardLimit`); cap `api` max replicas; container image with `tzdata`; provision the `gpx` container in Bicep. Per-replica semantics of rate limiters, the global guest window and the guest cache (accepted while replicas are capped). M3 — validate a real Entra token end to end once the tenant exists (issuer string). M4 — GPX processing of the demo tracks (still `Pending`) and blob deletion on run delete.
