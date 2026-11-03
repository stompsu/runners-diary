# Runners Diary – Architecture overview

Living document, owned by the `architect` agent. Status: **M1 implemented; M2 implemented — awaiting CI + approval** ([plan + deviations](plans/M2-domain-runs-api.md#11-implementation-notes--deviations-2026-11-03); ADRs 0009–0013 accepted 2026-11-03). Decisions are recorded in [`adr/`](adr/); this page summarizes them.

## 1. Context

A single-page web app where authenticated runners (Entra External ID) or temporary guests plan and record runs, import GPX tracks, and view training insights with an AI-narrated summary. One developer, near-zero budget, Azure-hosted.

```
 Browser (React SPA)
   │  same-origin /api/*  (bearer token: Entra or guest JWT — M2)
   ▼
 web tier (static files + /api proxy)  ── ADR 0004 (proposed: Aspire YARP static site on Container Apps)
   ▼
 RunnersDiary.Api (ASP.NET Core 10 Minimal APIs, Container Apps, scale-to-zero)
   ├── Azure SQL Database (serverless, free offer)     ── ADR 0006
   ├── Azure Blob Storage (GPX raw + GeoJSON, M4)
   ├── Azure Maps (token flow, M4)
   └── Microsoft Foundry model via Agent Framework (M6)
```

## 2. Components

| Component | Responsibility | Introduced |
|---|---|---|
| `web/` | React 19 + TS + Vite SPA; React Router v8 (data mode), TanStack Query via `openapi-react-query`, shadcn/ui + Tailwind v4, i18next. Calls the API only via the generated client on relative `/api`. | M1 |
| `RunnersDiary.Api` | HTTP surface: vertical slices under `Features/<Area>/`, ProblemDetails, OpenAPI 3.1 document (build-time generated, committed), health endpoints. M2: JWT schemes `Entra` + `Guest` behind policy scheme `Smart`, fallback policy, `HttpCurrentUser`, rate limiting (per-client + global guest creation, general per caller), 64 KB body limit, run caps, built-in validation, mandatory forwarded-headers decision outside Development. | M1 → M2 |
| `RunnersDiary.Domain` | Pure C#: entities/value objects (User, Run with Location/Race/Rating, GpxTrack), run-type catalog, run rules, IANA/DST time logic, ISO weeks, calendar grid, weekly totals, demo schedule expansion; later load and GPX metrics. No package references. | M1 (empty) → M2 |
| `RunnersDiary.Infrastructure` | EF Core `RunnersDiaryDbContext` (non-pooled, `Owner` query filter, ownership guard, NTS `geography`) + migrations, `ICurrentUser` abstraction, JIT user provisioning + guest check, Blob store (`gpx` container), demo dataset loader + startup check, guest seeding + cleanup hosted service; later Maps, AI adapters. | M1 (empty DbContext) → M2 |
| `RunnersDiary.ServiceDefaults` | OpenTelemetry, health checks, resilience, service discovery. | M1 |
| `RunnersDiary.AppHost` | Local orchestration: `sql` (+ db `runnersdiary`), `storage` (Azurite, `blobs`), `api`, `web`. | M1 |

Dependency rule: `Api → Infrastructure → Domain`; `Domain` depends on nothing ([ADR 0001](adr/0001-solution-structure-and-aspire.md)).

## 3. Contracts

- **API surface**: everything under `/api`; RFC 9457 errors; anonymous allowlist = `/alive`, `/health`, `/api/health` and (M2) `POST /api/guest-sessions` ([ADR 0007](adr/0007-api-surface-conventions.md), [0009](adr/0009-authentication-entra-and-guest-jwt.md)). Endpoint list: [`api-contract.md`](api-contract.md).
- **OpenAPI → TS**: `src/RunnersDiary.Api/openapi/runnersdiary-api.json` → `web/src/api/generated/schema.d.ts` via `npm --prefix web run generate:api`; CI fails on drift ([ADR 0002](adr/0002-openapi-typescript-client.md)).
- **Data model**: [`data-model.md`](data-model.md). SI units; `geography` points; run date as local `date` + optional `time` + IANA zone, derived `StartUtc` ([ADR 0008](adr/0008-run-date-and-time-storage.md)); race details, rating and location are optional complex types inside `Runs`; clustered `(UserId, Date)` ([ADR 0013](adr/0013-persistence-model-and-migrations.md)).
- **Blobs**: container `gpx`; everything a user owns lives under `users/{userId:N}/` (prefix delete on guest cleanup). Created by the API only when `Storage:CreateContainerOnStartup=true` (local/tests); provisioned by Bicep in Azure (M7).

## 4. Local development

`dotnet run --project src/RunnersDiary.AppHost` starts: SQL Server container (persistent, data volume), Azurite (persistent), the API, and the Vite dev server (Aspire sets `PORT`, injects `API_HTTP`/`API_HTTPS`; Vite proxies `/api`). The Aspire dashboard shows logs, traces and health. Requires Docker running and a trusted dev certificate.

From M2 the AppHost also: sets `Database__MigrateOnStartup=true` (the only place migrations run at startup, ADR 0013) and `Storage__CreateContainerOnStartup=true`; sets `ForwardedHeaders__Enabled=false` (explicit setting is mandatory outside Development); passes the generated, persisted secret parameter `guest-token-signing-key`; forwards Entra settings from its user-secrets section `Entra` only when all three ids are present (otherwise `Auth__Entra__Enabled=false`: only guests can sign in); pins the web port to 5173 for stable redirect URIs.

## 5. Auth flow (M2 API, M3 SPA)

- **Real users**: SPA (MSAL React, M3) signs in against the external tenant's user flow (`https://<subdomain>.ciamlogin.com/`), requests `api://<api-client-id>/access_as_user`, sends the v2 access token. API scheme `Entra` validates it and JIT-provisions `Users` from `(tid, oid)`.
- **Guests**: `POST /api/guest-sessions` creates a guest user + demo copy and returns an HS256 JWT (`sub` = user id, `role=guest`, `exp` = account expiry, 24 h). Scheme `Guest` validates it and checks the user is still an unexpired guest (cached ≤ min(5 min, expiry), evicted by cleanup).
- The default policy scheme `Smart` routes each request to one of the two by the token's issuer; default and **fallback authorization policy require an authenticated user**; every query is scoped by `ICurrentUser` through the EF `Owner` query filter; foreign resources return 404 ([ADR 0009](adr/0009-authentication-entra-and-guest-jwt.md), [0010](adr/0010-current-user-and-data-scoping.md)).
- **Guest lifecycle**: per-client (IPv4 / IPv6 /64) and global creation limits, active-guest cap, run caps, cleanup of rows and blobs after expiry by a hosted service (M7 may add a Container Apps job) ([ADR 0011](adr/0011-guest-accounts-lifecycle.md)).

## 6. Deployment topology (planned, M7)

Azure Container Apps environment (consumption, scale-to-zero) with `api` and the web tier, ACR, Azure SQL (free offer, auto-pause), Storage, Key Vault, Azure Maps, Foundry, Application Insights/Log Analytics; managed identity everywhere; azd + GitHub Actions with OIDC. Web tier choice pending ([ADR 0004](adr/0004-web-hosting.md)).

## 7. CI

GitHub Actions `ci.yml`: backend (build, Testcontainers tests), frontend (lint, typecheck, Vitest, build, Playwright smoke), api-contract drift check ([ADR 0005](adr/0005-ci-pipeline.md)).

## ADR index

| # | Title | Status |
|---|---|---|
| 0001 | [Solution structure and .NET Aspire usage](adr/0001-solution-structure-and-aspire.md) | Accepted |
| 0002 | [OpenAPI document and generated TypeScript client](adr/0002-openapi-typescript-client.md) | Accepted |
| 0003 | [Frontend stack and UI kit](adr/0003-frontend-stack-and-ui-kit.md) | Accepted |
| 0004 | [Hosting the React SPA in Azure](adr/0004-web-hosting.md) | Proposed |
| 0005 | [CI pipeline shape](adr/0005-ci-pipeline.md) | Accepted |
| 0006 | [Azure SQL Database (serverless, free offer)](adr/0006-database-azure-sql.md) | Accepted |
| 0007 | [API surface conventions](adr/0007-api-surface-conventions.md) | Accepted |
| 0008 | [Run date & time storage](adr/0008-run-date-and-time-storage.md) | Accepted |
| 0009 | [Authentication: Entra External ID + guest JWT](adr/0009-authentication-entra-and-guest-jwt.md) | Accepted (M2) |
| 0010 | [Current user and per-user data scoping](adr/0010-current-user-and-data-scoping.md) | Accepted (M2) |
| 0011 | [Guest accounts lifecycle](adr/0011-guest-accounts-lifecycle.md) | Accepted (M2) |
| 0012 | [Request validation](adr/0012-request-validation.md) | Accepted (M2) |
| 0013 | [Persistence model and migrations](adr/0013-persistence-model-and-migrations.md) | Accepted (M2) |
