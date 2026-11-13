# Runners Diary – Architecture overview

Living document, owned by the `architect` agent. Status: **M1–M6 implemented and merged. M7 (Azure deployment) Part A (offline deployment artifacts: images, nginx web tier, Bicep + azd, CI/CD workflows, Azure Maps adapters, Azure readiness) merged (stompsu/runners-diary#12). Part B: gates G1–G7 done (2026-11-04 to 06)**. **Production is live** at `https://runners-diary.highpace.dev` (custom domain, managed certificate): 25 resources in `rg-runnersdiary-prod` (`westeurope`, SQL in `polandcentral`). Deploys run after green CI on `main` behind the `prod` approval, with readiness wait, smoke test and automatic rollback. **AI summaries are on** (gpt-5.4-mini Global Standard, prompt v2). Guests-only until Entra (G8). Log: [plan §12.7](plans/M7-azure-deployment.md).

## 1. Context

A single-page web app where authenticated runners (Entra External ID) or temporary guests plan and record runs, import GPX tracks, and view training insights with an AI-narrated summary. One developer, near-zero budget, Azure-hosted.

```
 Browser (React SPA)
   │  same-origin /api/*  (bearer token: Entra or guest JWT — M2)
   ▼
 web tier: nginx container (static SPA, security headers/CSP, /api proxy)  ── ADR 0029 (supersedes 0004)
   ▼  HTTPS to the internal ingress only
 RunnersDiary.Api (ASP.NET Core 10 Minimal APIs, Container Apps, internal ingress, scale-to-zero)
   ├── Azure SQL Database (serverless, free offer, Entra-only)   ── ADR 0006, 0031
   ├── Azure Blob Storage (GPX raw + GeoJSON, M4; shared key off)
   ├── Azure Maps (server-side geocoding, M7; SAS token for display built but off)   ── ADR 0033
   └── Microsoft Foundry model via Agent Framework (code M6, keyless; deployment M7 Part B)

 Browser ── map tiles/style ──▶ OpenFreeMap (MapLibre, production default; no key)
```

## 2. Components

| Component | Responsibility | Introduced |
|---|---|---|
| `web/` | React 19 + TS + Vite SPA; React Router v8 (data mode), TanStack Query via `openapi-react-query`, shadcn/ui + Tailwind v4, i18next. Calls the API only via the generated client on relative `/api`. M3: auth session store (`auth/session.ts`) + `client.ts` bearer middleware for guest and Entra tokens; `@azure/msal-browser` v5 (no msal-react) behind an `EntraController`, lazy-loaded only with Entra config, with a `redirect.html` bridge page; hand-built `<table role="grid">` month grid with lazy, URL-addressable panels/dialogs (bottom sheets on phones); react-hook-form + zod run form; plain-date helpers (no date library); in-memory fake API shared by Vitest and Playwright, plus one full-stack journey (ADRs 0014–0019). Build: Vite 8/Rolldown with `index.html` + `redirect.html` entries and named vendor chunks. M4: provider-neutral `components/map/MapView` with a lazy MapLibre 6 provider (WebGL2 check, "map unavailable" fallback), Recharts elevation profile with keyboard range input, track data table; `features/tracks` (upload/replace/delete/download, track viewer route, prefill review dialog → edit form); `LocationField` + map picker; lazily registered `maps`/`tracks` i18n; initial-load budget enforced by `scripts/bundle-budget.mjs` (ADRs 0022, 0023). M5: lazy `/insights` route (`features/insights`) with URL-state period picker and comparison select, KPI grid with worded deltas, ACWR/monotony cards, five Recharts charts as accessible `<figure>`s with data tables, race history; lazily registered `insights` i18n. M6: `features/insights/summary` "AI summary" card in the lazy Insights chunk (placeholder text rendered with the existing unit formatters, source badge, Generate/Regenerate with remaining count, cancelable generation, polite live region, a state per problem type), no new npm dependency (ADRs 0026, 0028). M7: lazy `AzureMapsMap` provider (`azure-maps-control` 3.7.4, own vendor chunk; SAS token in memory only, session fallback to MapLibre, privacy note), zod `jitless` (CSP without `unsafe-eval`), `ApiStatus` once per page load; served in Azure by the nginx image `web/Dockerfile` (ADRs 0029, 0033). | M1 → M7 |
| `RunnersDiary.Api` | HTTP surface: vertical slices under `Features/<Area>/`, ProblemDetails, OpenAPI 3.1 document (build-time generated, committed), health endpoints. M2: JWT schemes `Entra` + `Guest` behind policy scheme `Smart`, fallback policy, `HttpCurrentUser`, rate limiting (per-client + global guest creation, general per caller), 64 KB body limit, run caps, built-in validation, mandatory forwarded-headers decision outside Development. M4: `Features/Tracks` (multipart upload read by the handler, GeoJSON stream with ETag/304, GPX download, delete), `Features/Geocoding` (provider-neutral search), `client-config.maps`/`gpx`, per-endpoint body limit (`EndpointBodyLimit`), `gpx-upload`/`geocode` rate policies, run delete removes blobs first. M5: `Features/Insights` (`GET /api/insights/report`, `private, no-store`). M6: `GET`/`POST /api/insights/summary`, problem types `ai-quota`, `ai-busy`, `summary-in-progress`, `summary-failed`, rate policy `ai-summary`, `client-config.ai`. M7: `GET /api/maps/token` (`maps-token` policy), geocode daily budget (429 `code: geocode-budget`), `client-config.maps.azureMapsDomain`, `/api/health` split (anonymous = live checks only outside Development; `health` policy), `--job guest-cleanup` entry mode (`Jobs/JobMode.cs`), production config validator, top-level startup failure handling; chiseled non-root image (`src/RunnersDiary.Api/Dockerfile`). | M1 → M7 |
| `RunnersDiary.Domain` | Pure C#: entities/value objects (User, Run with Location/Race/Rating, GpxTrack), run-type catalog, run rules, IANA/DST time logic, ISO weeks, calendar grid, weekly totals, demo schedule expansion. M4: `Gpx/` — hardened forward-only `GpxReader`, haversine distance, 3 m hysteresis, moving time, splits, elevation profile, budgeted Douglas–Peucker, `TrackProcessor` (ADRs 0020, 0021). M5: `Insights/` — session load (sRPE), uncoupled 7/28 ACWR, monotony/strain, totals, week/month buckets, comparison windows, type distribution, easy-pace trend, race history, `InsightsReportBuilder` (today is an input; ADR 0024). M6: `Insights/Narrative/` — `FactSheetBuilder` (projection, canonical JSON + SHA-256 fingerprint), `SummaryValidator` (8 rule codes), templates, demo fixture predicates; `Ai/` entities `AiSummary`, `AiGeneration` (ADR 0026). No package references. | M1 (empty) → M6 |
| `RunnersDiary.Infrastructure` | EF Core `RunnersDiaryDbContext` (non-pooled, `Owner` query filter, ownership guard, NTS `geography`) + migrations, `ICurrentUser` abstraction, JIT user provisioning + guest check, Blob store (`gpx` container), demo dataset loader + startup check, guest seeding + cleanup hosted service. M4: `Gpx/` (`TrackIngestionService`: bounded copy, SHA-256, processing slots, both blobs, rollback; `TrackDeletionService`; per-user `sp_getapplock`; `TrackGeoJsonWriter`), `Maps/` (`MapsOptions`, `IGeocoder` + `NoGeocoder`), demo tracks pre-processed once per process. M5: `Insights/InsightsReportService` (projection queries, ADR 0025). M6: `Ai/` — `AiOptions` (+ startup validation), `PromptCatalog` (manifest hash check), `ChatClientFactory` (`None` \| `Foundry` keyless \| `Fake` Development/Testing only), `TrainingInsightsAgent` (`ChatClientAgent`, strict JSON schema), `SummaryGenerator` (attempts, timeout, slots, in-flight guard), `AiUsageLedger` + EF stores, `InsightsSummaryService`; migration `AiSummaries`; ledger purge in guest cleanup (ADRs 0027, 0028). M7: `Maps/` `AzureMapsGeocoder` (Entra MI, domain allow-list, no redirects/retries), `AzureMapsSasTokenIssuer` (ARM `listSas`, one shared cached token, single-flight), `GeocodeBudget` + `GeocodeUsage` table (migration `GeocodeUsage`), `SqlActivityInterceptor` + cleanup idle-skip, `GeocodeUsage` purge in guest cleanup (ADRs 0032, 0033). | M1 (empty DbContext) → M7 |
| `RunnersDiary.ServiceDefaults` | OpenTelemetry, health checks, resilience, service discovery. M6: registers the meter and activity source `RunnersDiary.Ai` (no prompt or answer content is recorded). M7: Azure Monitor exporter (`Azure.Monitor.OpenTelemetry.AspNetCore` 1.6.0) only when `APPLICATIONINSIGHTS_CONNECTION_STRING` is set, 5 traces/s (ADR 0034). | M1 → M7 |
| `infra/`, `azure.yaml`, `.github/workflows/deploy.yml` | Bicep (subscription scope, 15 modules), azd hooks, grant SQL, migrate image, smoke script; OIDC deploy workflow (ADRs 0030–0034). Provisioned at G2 (2026-11-04); not yet deployed. | M7 |
| `RunnersDiary.AppHost` | Local orchestration: `sql` (+ db `runnersdiary`), `storage` (Azurite, `blobs`), `api`, `web`. M6: forwards `Ai__Provider` (`None` by default; `Fake` with `Ai:UseFake=true`; `Foundry` only when user-secrets `Ai:Endpoint` and `Ai:Deployment` are both set). No `Aspire.Hosting.Foundry` (ADR 0027 §4). | M1 → M6 |
| `job-race-import` (**M8, planned, awaiting approval**) | Weekly Container Apps job (Monday 03:25 UTC, sharing the guest cleanup's SQL wake) running the API image with `--job race-import`. It crawls an allow-listed public race calendar (running.life first) politely, and upserts the shared `Races` catalogue. Own identity `id-rd-race-import` with table-level SQL grants only. Kill switches `RaceImport:Enabled` + per source, off by default; disabled = no SQL connection ([ADR 0035](adr/0035-race-catalogue-sources-and-crawling-policy.md), [0038](adr/0038-race-import-job.md)). | M8 (planned) |
| `tests/RunnersDiary.Ai.Tests` | Fact sheet, validator, pipeline (scripted `IChatClient`), prompt manifest, demo fixture for every "today" across a year, eval cases (CI mode; live mode only with `RD_AI_LIVE_EVAL=1`), Foundry wire test. | M6 |

Dependency rule: `Api → Infrastructure → Domain`; `Domain` depends on nothing ([ADR 0001](adr/0001-solution-structure-and-aspire.md)).

## 3. Contracts

- **API surface**: everything under `/api`; RFC 9457 errors; anonymous allowlist = `/alive`, `/health`, `/api/health`, (M2) `POST /api/guest-sessions` and (M3) `GET /api/client-config` ([ADR 0007](adr/0007-api-surface-conventions.md), [0009](adr/0009-authentication-entra-and-guest-jwt.md)). Endpoint list: [`api-contract.md`](api-contract.md).
- **OpenAPI → TS**: `src/RunnersDiary.Api/openapi/runnersdiary-api.json` → `web/src/api/generated/schema.d.ts` via `npm --prefix web run generate:api`; CI fails on drift ([ADR 0002](adr/0002-openapi-typescript-client.md)).
- **Data model**: [`data-model.md`](data-model.md). SI units; `geography` points; run date as local `date` + optional `time` + IANA zone, derived `StartUtc` ([ADR 0008](adr/0008-run-date-and-time-storage.md)); race details, rating and location are optional complex types inside `Runs`; clustered `(UserId, Date)` ([ADR 0013](adr/0013-persistence-model-and-migrations.md)).
- **Blobs**: container `gpx`; everything a user owns lives under `users/{userId:N}/` (prefix delete on guest cleanup); each track under `users/{u:N}/tracks/{t:N}/` (`original.gpx` + `track.json`, M4). Never served by URL; the API streams them. Created by the API only when `Storage:CreateContainerOnStartup=true` (local/tests); provisioned by Bicep in Azure (M7).
- **Maps (M4, M7)**: `client-config.maps` selects the provider at runtime: MapLibre + OpenFreeMap (default, also in production), or Azure Maps with a shared SAS token from `GET /api/maps/token` (built in M7, off until gate G9). Geocoding only through the API (`IGeocoder`): `None` locally, Azure Maps in Azure, capped by a durable global daily budget of 75 calls ([ADR 0022](adr/0022-map-provider-geocoding-and-azure-maps-tokens.md), [0033](adr/0033-production-maps-and-azure-maps.md)).
- **Insights (M5)**: one endpoint `GET /api/insights/report?from&to&compare` (≤ 366 days) returns a deterministic, versioned report (`metricsVersion` 1). Flow: handler validates → `InsightsReportService` reads 1–5 `AsNoTracking` projections through the `Owner` filter (seeks on `(UserId, Date)`, no stored aggregates, no schema change, no server cache) → pure `InsightsReportBuilder` (Domain) computes everything with `today` from the profile zone → DTO mapping. Presets live in the SPA (from `me.today`); run create/update/delete invalidate the client cache. Metric definitions and bucket rules: [ADR 0024](adr/0024-training-metrics-definitions-v1.md); computation, API and the **M6 contract** (agent tools call `IInsightsReportService` in-process and never compute new numbers): [ADR 0025](adr/0025-insights-report-computation-and-api.md).
- **AI summary (M6)**: `GET`/`POST /api/insights/summary`. The flow:
  1. The report is computed as above.
  2. The pure `FactSheetBuilder` projects it into a flat fact sheet: no user free text, no heart rate, no ids; canonical JSON with a SHA-256 fingerprint.
  3. Empty, planned-only and few-session periods get a deterministic template, without a model call.
  4. Otherwise, on an explicit click only, one `ChatClientAgent` call (Agent Framework over `IChatClient`, strict JSON schema, prompt v1) returns prose in which every number is a `{key}` placeholder.
  5. `SummaryValidator` (Domain) checks it; one retry on failure.
  6. The result is upserted into `AiSummaries`, keyed by user + period + compare + language. It is fresh while the fingerprint, versions and deployment match, and stale otherwise.
  7. The client formats the placeholders with the dashboard's unit helpers.

  Durable quotas come from the `AiGenerations` ledger. Guests on the default view get a predicate-guarded demo example summary without tokens. **No cloud resource before M7:** the provider is `None` by default (generation → 503), `Fake` for local development and tests, and `Foundry` (keyless, managed identity) when configured. ADRs [0026](adr/0026-training-insights-agent-and-grounding.md)–[0028](adr/0028-summary-cache-quotas-and-demo-summaries.md).

- **Race proposals (M8, planned; proposed 2026-11-13, awaiting the user's approval).**
  - **Catalogue:** a shared, read-only race catalogue (`Races` + `RaceDistances`, no `Owner` filter, facts only, attributed). It is filled by `job-race-import`; locally and in CI by a synthetic demo catalogue.
  - **Proposals:** per-user `RaceInterestProfiles` and `RaceProposalStates` (Owner-filtered, cascade with the user) drive `GET /api/races/proposals?from&to`. The handler reads three projections, and the pure `RaceProposalBuilder` (Domain) filters and ranks them. Nothing is stored per proposal.
  - **Calendar:** the diary calls it next to `getCalendarMonth`, with the month's grid bounds, and draws dotted "proposal" bubbles with a legend and a toggle.
  - **Signup:** "I signed up" creates an ordinary **planned race run** linked by `Runs.CatalogRaceId` (ON DELETE SET NULL), so the calendar, insights and the AI summary need no change. Date changes are flagged, never applied to runs automatically.
  - Plan: [M8](plans/M8-race-proposals.md); ADRs [0035](adr/0035-race-catalogue-sources-and-crawling-policy.md)–[0038](adr/0038-race-import-job.md).

## 4. Local development

`dotnet run --project src/RunnersDiary.AppHost` starts: SQL Server container (persistent, data volume), Azurite (persistent), the API, and the Vite dev server (Aspire sets `PORT`, injects `API_HTTP`/`API_HTTPS`; Vite proxies `/api`). The Aspire dashboard shows logs, traces and health. Requires Docker running and a trusted dev certificate.

From M2 the AppHost also: sets `Database__MigrateOnStartup=true` (the only place migrations run at startup, ADR 0013) and `Storage__CreateContainerOnStartup=true`; sets `ForwardedHeaders__Enabled=false` (explicit setting is mandatory outside Development); passes the generated, persisted secret parameter `guest-token-signing-key`; forwards Entra settings from its user-secrets section `Entra` only when all three ids are present (otherwise `Auth__Entra__Enabled=false`: only guests can sign in), plus `Entra:SpaClientId` → `Auth__Entra__SpaClientId` when set (M3, feeds `GET /api/client-config`); pins the web port to 5173 for stable redirect URIs.

## 5. Auth flow (M2 API, M3 SPA)

- **Real users**: SPA (MSAL Browser v5, M3) signs in against the external tenant's user flow (`https://<subdomain>.ciamlogin.com/`), requests `api://<api-client-id>/access_as_user`, sends the v2 access token. API scheme `Entra` validates it and JIT-provisions `Users` from `(tid, oid)`.
- **SPA side (M3)**: the SPA reads the non-secret Entra settings at runtime from anonymous `GET /api/client-config` (`entra: null` hides "Sign in"; [ADR 0015](adr/0015-spa-runtime-configuration.md)). Only then is the MSAL chunk loaded. MSAL v5 uses redirect sign-in with `redirectUri = /redirect.html` (bridge page, no COOP) and a sessionStorage cache; no MSAL `state` — the bridge returns to `/signin?returnTo=…`, which is re-sanitised (same-origin path only). Silent-token failures other than network errors end the session (`sessionEnded`); the app never redirects to the IdP from an API call. The guest token lives in `localStorage` (`rd.guestSession`) with a skew-corrected expiry and is synced across tabs. One `client.ts` middleware attaches either token; a 401 for the current session's token signs out and returns to `/signin`; every sign-out clears the query and mutation caches ([ADR 0014](adr/0014-spa-authentication-and-token-handling.md)). Real-tenant sign-in/sign-out is still to be verified (AC3/AC4 deferred).
- **Guests**: `POST /api/guest-sessions` creates a guest user + demo copy and returns an HS256 JWT (`sub` = user id, `role=guest`, `exp` = account expiry, 24 h). Scheme `Guest` validates it and checks the user is still an unexpired guest (cached ≤ min(5 min, expiry), evicted by cleanup).
- The default policy scheme `Smart` routes each request to one of the two by the token's issuer; default and **fallback authorization policy require an authenticated user**; every query is scoped by `ICurrentUser` through the EF `Owner` query filter; foreign resources return 404 ([ADR 0009](adr/0009-authentication-entra-and-guest-jwt.md), [0010](adr/0010-current-user-and-data-scoping.md)).
- **Guest lifecycle**: per-client (IPv4 / IPv6 /64) and global creation limits, active-guest cap, run caps, cleanup of rows and blobs after expiry by a hosted service (idle-skip since M7: no tick without SQL activity) plus, in Azure, the weekly Container Apps job `job-guest-cleanup` running `--job guest-cleanup` ([ADR 0011](adr/0011-guest-accounts-lifecycle.md), [0032](adr/0032-migrations-and-jobs-in-azure.md)).

## 6. Deployment topology (M7 — **provisioned at G2 2026-11-04, not yet deployed**; [plan](plans/M7-azure-deployment.md))

The topology is one `prod` environment (azd env `prod`, RG `rg-runnersdiary-prod`) in **`westeurope`**, including the Azure Maps account. The **SQL server and database are in `polandcentral`** (Bicep `sqlLocation` / azd `SQL_LOCATION`; server `sql-rd-<token>-polandcentral`), because new SQL servers were restricted for the subscription in West and North Europe; app ↔ DB calls therefore cross regions, and all data at rest stays in the EU ([ADR 0030](adr/0030-azure-topology-iac-and-deployment.md) amendment "Region"). The resources exist (G2); the apps run a placeholder image until the first deploy (G4). **Nothing is provisioned, changed or deployed without the user's explicit confirmation of each gate** (plan §6.2: G3 GitHub settings, G4 first deploy, …). Provisioning procedure (hooks run by hand: `azd hooks run preprovision` → `azd provision` → `azd hooks run postprovision`): plan §10 step 5.

**Container Apps environment** (Consumption plan, no VNet):
- `web` (nginx unprivileged, external ingress, 0–2, probes `/nginx-alive`): serves the SPA with strict headers (CSP without `unsafe-inline`/`unsafe-eval`, hashed library styles, COOP and `frame-ancestors 'none'` except on `/redirect.html`), and proxies `/api/*` to the API's **internal FQDN over verified HTTPS**, with the client IP reduced to the rightmost `X-Forwarded-For` value, 120 s timeouts and a 21 MB body limit ([ADR 0029](adr/0029-web-tier-nginx-container.md) + amendment, supersedes 0004);
- `api` (chiseled non-root image, internal ingress, 0–2 replicas, probes `/alive` only; anonymous `/api/health` never touches SQL);
- `job-migrate` (manual: self-contained EF bundle + go-sqlcmd grant script, identity = SQL Entra admin);
- `job-guest-cleanup` (weekly, Mon 03:15 UTC, API image with `--job guest-cleanup`).
- *(M8, planned, not provisioned)* `job-race-import` (weekly, Mon 03:25 UTC, API image with `--job race-import`, identity `id-rd-race-import`, flags off by default; gates G-R2…G-R4 in the [M8 plan](plans/M8-race-proposals.md#62-part-b-user-confirmed-gates-each-needs-the-users-explicit-yes-same-procedure-as-m7)).
- First provision runs a public placeholder image (port 80, no probes) until the first deploy.

**Shared services:**
- ACR Basic;
- Azure SQL free offer (`polandcentral`): Entra-only, firewall rule `AllowAzureServices` (0.0.0.0), `AutoPause` at the free limit;
- Storage: shared key off;
- Key Vault: only the guest signing key;
- Foundry: gpt-5.4-mini, Global Standard, 30k TPM, local auth off (data at rest in `westeurope`; prompts, which carry only the metrics-only fact sheet, may be processed in any Azure region: [ADR 0027](adr/0027-ai-model-provider-foundry-and-local-dev.md) amendment "deployment type", [ADR 0026](adr/0026-training-insights-agent-and-grounding.md) amendment);
- Azure Maps Gen2: server-side geocoding (Entra MI) capped by a **durable global** budget of 75 calls/day (≤ 2,325/month, under the free 5,000); SAS display built but off by default ([ADR 0033](adr/0033-production-maps-and-azure-maps.md) + amendment);
- Log Analytics + App Insights: OpenTelemetry via the Azure Monitor exporter (only with a connection string; 5 traces/s).

**Identities and secrets:** one user-assigned identity per role (`api`, `web`, migrator = SQL admin, deployer with a GitHub federated credential), with no keys or passwords anywhere. Key Vault holds one secret, the guest signing key, generated by `preprovision.sh` only when absent (never printed, never rotated by a re-provision) ([ADR 0031](adr/0031-identities-secrets-and-data-access.md) + amendment).

**IaC and deployment** ([ADR 0030](adr/0030-azure-topology-iac-and-deployment.md), [0032](adr/0032-migrations-and-jobs-in-azure.md), both amended):
- Hand-written Bicep (`infra/main.bicep` + 15 modules, all linter rules as errors), provisioned only by the user with `azd provision`; hooks `preprovision.sh` / `postprovision.sh` / `predown.sh`.
- Three digest-pinned images: `api` (≈ 101 MB), `web` (≈ 27 MB), `migrate` (≈ 116 MB), tagged `sha-<sha>`. ACR Basic has no retention policy; old tags are pruned by hand.
- CI deploys (`deploy.yml`, OIDC, off until `DEPLOY_ENABLED=true`) in a fixed order: images, then the migration job, then the apps, then `smoke.sh`, with automatic rollback when the update started and the smoke test did not pass (including partial failure and cancellation), all behind the GitHub environment `prod` approval. Dispatched refs must be ancestors of `origin/main`.

**Cost** ([ADR 0034](adr/0034-cost-guardrails-and-observability.md) + amendment): about $5.5/month idle, about $211–300 in the guarded worst case (Global Standard AI prices since 2026-11-04; estimated with `swedencentral` retail prices, ⚠ to re-check for `westeurope`/`polandcentral`, plan §4). Guardrails: an RG budget of $25 (Actual 40/80/100 %, Forecasted 100 %), alerts on Foundry `TokenTransaction`, SQL `free_amount_remaining`, App Insights failed requests and Maps `Usage` (> 200/day), a Log Analytics cap of 0.15 GB/day, and no SQL wake from probes, anonymous health calls or idle cleanup ticks.

## 7. CI

GitHub Actions `ci.yml`: backend (build, Testcontainers tests), frontend (lint, typecheck, Prettier, Vitest, `test:tz` under two time zones, build, Playwright with the fake API incl. phone/LA project and axe), api-contract drift check ([ADR 0005](adr/0005-ci-pipeline.md)); since M3 also `e2e-fullstack` on push to `main` and manual dispatch (real API + SQL + Azurite containers, [ADR 0019](adr/0019-frontend-test-strategy.md)); since M4 the frontend job runs `npm run bundle-budget` after the build, Playwright renders the real MapLibre map against a stubbed style (no network), and the full-stack suite includes a GPX upload (`tracks.spec.ts`); since M5 the bundle budget also requires the Insights route chunk to stay lazy, and the full-stack suite includes an insights journey (`insights.spec.ts`). Since M7: a fourth PR job **`deploy-artifacts`** (Bicep build + lint, actionlint, shellcheck, the three image builds, a header-verify script and the whole Playwright suite against the nginx web image with a fixture that fails on any CSP violation, proven by `csp-guard.spec.ts`), and `e2e-fullstack` runs the built `api` and `web` images. `deploy.yml` (OIDC, environment approval) is separate and inert until `DEPLOY_ENABLED=true` ([ADR 0030](adr/0030-azure-topology-iac-and-deployment.md)). Concurrency cancels only superseded PR runs.

## ADR index

| # | Title | Status |
|---|---|---|
| 0001 | [Solution structure and .NET Aspire usage](adr/0001-solution-structure-and-aspire.md) | Accepted |
| 0002 | [OpenAPI document and generated TypeScript client](adr/0002-openapi-typescript-client.md) | Accepted |
| 0003 | [Frontend stack and UI kit](adr/0003-frontend-stack-and-ui-kit.md) | Accepted |
| 0004 | [Hosting the React SPA in Azure](adr/0004-web-hosting.md) | Superseded by 0029 |
| 0005 | [CI pipeline shape](adr/0005-ci-pipeline.md) | Accepted |
| 0006 | [Azure SQL Database (serverless, free offer)](adr/0006-database-azure-sql.md) | Accepted |
| 0007 | [API surface conventions](adr/0007-api-surface-conventions.md) | Accepted |
| 0008 | [Run date & time storage](adr/0008-run-date-and-time-storage.md) | Accepted |
| 0009 | [Authentication: Entra External ID + guest JWT](adr/0009-authentication-entra-and-guest-jwt.md) | Accepted (M2) |
| 0010 | [Current user and per-user data scoping](adr/0010-current-user-and-data-scoping.md) | Accepted (M2) |
| 0011 | [Guest accounts lifecycle](adr/0011-guest-accounts-lifecycle.md) | Accepted (M2) |
| 0012 | [Request validation](adr/0012-request-validation.md) | Accepted (M2) |
| 0013 | [Persistence model and migrations](adr/0013-persistence-model-and-migrations.md) | Accepted (M2) |
| 0014 | [SPA authentication and token handling](adr/0014-spa-authentication-and-token-handling.md) | Accepted (M3; amended 2026-11-03) |
| 0015 | [SPA runtime configuration via `/api/client-config`](adr/0015-spa-runtime-configuration.md) | Accepted (M3; amended 2026-11-03) |
| 0016 | [Calendar grid and URL state](adr/0016-calendar-grid-and-url-state.md) | Accepted (M3; amended 2026-11-03) |
| 0017 | [Client-side dates without a date library](adr/0017-client-dates-without-a-date-library.md) | Accepted (M3) |
| 0018 | [Run forms and server errors](adr/0018-run-forms-and-server-errors.md) | Accepted (M3; amended 2026-11-03) |
| 0019 | [Frontend test strategy](adr/0019-frontend-test-strategy.md) | Accepted (M3; amended 2026-11-03) |
| 0020 | [GPX upload and safe parsing](adr/0020-gpx-upload-and-safe-parsing.md) | Accepted (M4; amended 2026-11-03) |
| 0021 | [Track metrics, simplification and GeoJSON](adr/0021-track-metrics-simplification-and-geojson.md) | Accepted (M4; amended 2026-11-03) |
| 0022 | [Map provider, geocoding and Azure Maps tokens](adr/0022-map-provider-geocoding-and-azure-maps-tokens.md) | Accepted (M4; amended 2026-11-03) |
| 0023 | [Run ↔ track, GPX prefill and blob deletion](adr/0023-run-tracks-prefill-and-deletion.md) | Accepted (M4; amended 2026-11-03) |
| 0024 | [Training metrics v1: definitions, edge cases and versioning](adr/0024-training-metrics-definitions-v1.md) | Accepted (M5; amended 2026-11-04) |
| 0025 | [Insights report: on-demand computation, one period endpoint, M6 contract](adr/0025-insights-report-computation-and-api.md) | Accepted (M5; amended 2026-11-04) |
| 0026 | [Training Insights agent: fact-sheet input, placeholder-only numbers, validation](adr/0026-training-insights-agent-and-grounding.md) | Accepted (M6; amended 2026-11-04) |
| 0027 | [AI model provider: Foundry over the OpenAI v1 endpoint, keyless, no cloud before M7](adr/0027-ai-model-provider-foundry-and-local-dev.md) | Accepted (M6; amended 2026-11-04) |
| 0028 | [AI summaries: fingerprint cache, explicit generation, durable quotas, demo example summaries](adr/0028-summary-cache-quotas-and-demo-summaries.md) | Accepted (M6; amended 2026-11-04) |
| 0029 | [Web tier: nginx container on Container Apps](adr/0029-web-tier-nginx-container.md) | Accepted (M7; amended 2026-11-04) |
| 0030 | [Azure topology, IaC (Bicep + azd) and the deploy pipeline](adr/0030-azure-topology-iac-and-deployment.md) | Accepted (M7; amended 2026-11-04) |
| 0031 | [Identities, role assignments, secrets and data-plane access](adr/0031-identities-secrets-and-data-access.md) | Accepted (M7; amended 2026-11-04) |
| 0032 | [Schema migrations and background jobs in Azure](adr/0032-migrations-and-jobs-in-azure.md) | Accepted (M7; amended 2026-11-04) |
| 0033 | [Production map provider, Azure Maps geocoding and the SAS display option](adr/0033-production-maps-and-azure-maps.md) | Accepted (M7; amended 2026-11-04) |
| 0034 | [Cost guardrails and observability](adr/0034-cost-guardrails-and-observability.md) | Accepted (M7; amended 2026-11-04) |
| 0035 | [Race catalogue sources, import strategy and the legal/crawling policy](adr/0035-race-catalogue-sources-and-crawling-policy.md) | Proposed (M8) |
| 0036 | [Race catalogue model, upsert/dedup/removal, and proposal → planned race run](adr/0036-race-catalogue-model-and-race-runs.md) | Proposed (M8) |
| 0037 | [Race proposals: interests, filtering and ranking, per-user state, calendar integration](adr/0037-race-proposals-filtering-and-state.md) | Proposed (M8) |
| 0038 | [The race import job on Container Apps](adr/0038-race-import-job.md) | Proposed (M8) |
