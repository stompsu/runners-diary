# Runners Diary – Architecture overview

Living document, owned by the `architect` agent. Status: **initial (M1)**. Decisions are recorded in [`adr/`](adr/); this page summarizes them.

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
| `RunnersDiary.Api` | HTTP surface: vertical slices under `Features/<Area>/`, ProblemDetails, OpenAPI 3.1 document (build-time generated, committed), health endpoints, auth policies (M2). | M1 |
| `RunnersDiary.Domain` | Pure C#: entities, run types, ISO-week logic, load metrics, GPX metrics. No package references. | M1 (empty) → M2 |
| `RunnersDiary.Infrastructure` | EF Core `RunnersDiaryDbContext` + migrations, Blob access, Maps, AI adapters, guest cleanup. | M1 (empty DbContext) → M2 |
| `RunnersDiary.ServiceDefaults` | OpenTelemetry, health checks, resilience, service discovery. | M1 |
| `RunnersDiary.AppHost` | Local orchestration: `sql` (+ db `runnersdiary`), `storage` (Azurite, `blobs`), `api`, `web`. | M1 |

Dependency rule: `Api → Infrastructure → Domain`; `Domain` depends on nothing ([ADR 0001](adr/0001-solution-structure-and-aspire.md)).

## 3. Contracts

- **API surface**: everything under `/api`; RFC 9457 errors; `/alive`, `/health`, `/api/health` are the only anonymous endpoints until guest-token issuance in M2 ([ADR 0007](adr/0007-api-surface-conventions.md)). Full endpoint list will live in `api-contract.md` (from M2).
- **OpenAPI → TS**: `src/RunnersDiary.Api/openapi/runnersdiary-api.json` → `web/src/api/generated/schema.d.ts` via `npm --prefix web run generate:api`; CI fails on drift ([ADR 0002](adr/0002-openapi-typescript-client.md)).
- **Data model**: `data-model.md` (from M2). SI units; `geography` points; run start stored as local date/time + IANA zone (to be confirmed by an M2 ADR).

## 4. Local development

`dotnet run --project src/RunnersDiary.AppHost` starts: SQL Server container (persistent, data volume), Azurite (persistent), the API, and the Vite dev server (Aspire sets `PORT`, injects `API_HTTP`/`API_HTTPS`; Vite proxies `/api`). The Aspire dashboard shows logs, traces and health. Requires Docker running and a trusted dev certificate.

## 5. Auth flow (planned, M2/M3)

SPA obtains a token from Entra External ID (MSAL React) or from `POST /api/guest-sessions` (app-issued short-lived JWT, role `guest`). API validates either scheme; a fallback policy requires an authenticated user; every query is scoped by the caller's user id from a single `ICurrentUser` abstraction; foreign resources return 404.

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
