# Runners Diary

A web app for runners to plan and track training and races: monthly calendar diary, run add/edit (planned → completed), race details and experience ratings, GPX import with map + elevation profile, an Insights dashboard (load, ACWR, trends) and an AI-generated period summary grounded in deterministically computed metrics. Multi-user (Entra External ID) with an isolated, 24 h guest/demo mode.

Full requirements: [`docs/PROJECT_BRIEF.md`](docs/PROJECT_BRIEF.md). Architecture and decisions: [`docs/architecture.md`](docs/architecture.md), [`docs/adr/`](docs/adr/).

## Current status

- **Phase 0** (agents + project memory) — done.
- **M1 – Skeleton** — done (merged, stompsu/runners-diary#1). Plan: [`docs/plans/M1-skeleton.md`](docs/plans/M1-skeleton.md).
- **M2 – Domain & Runs API** — done (merged, stompsu/runners-diary#4). Plan, deviations (§11) and the Entra tenant checklist (§10): [`docs/plans/M2-domain-runs-api.md`](docs/plans/M2-domain-runs-api.md).
  - The user creates the Entra external tenant themself (checklist §10). Until its IDs are in AppHost user-secrets, the API runs with Entra disabled; guests work and tests mint their own tokens.
- **M3 – Calendar UI** — done (merged, stompsu/runners-diary#5). Real Entra sign-in/sign-out and zone sync (AC3/AC4) still to check once the tenant exists. Plan and deviations (§12): [`docs/plans/M3-calendar-ui.md`](docs/plans/M3-calendar-ui.md).
- **M4 – Maps & GPX** — done (merged, stompsu/runners-diary#9). Azure Maps (adapters, token endpoint, geocoder) deferred to M7. Plan and deviations (§12): [`docs/plans/M4-maps-gpx.md`](docs/plans/M4-maps-gpx.md).
- **M5 – Insights** — plan approved 2026-11-04 (all defaults), implemented and verified locally on branch `m5-insights`; awaiting CI + approval: [`docs/plans/M5-insights.md`](docs/plans/M5-insights.md) (ADRs 0024–0025).
- Architecture: [`docs/architecture.md`](docs/architecture.md); data model: [`docs/data-model.md`](docs/data-model.md); API contract: [`docs/api-contract.md`](docs/api-contract.md); decisions: [`docs/adr/`](docs/adr/) (0001–0025).

## Prerequisites (local dev)

- .NET SDK 10.0.400+ (pinned in `global.json`), Node 26 (`web/.nvmrc`).
- **Docker Desktop running** — Aspire runs SQL Server + Azurite, and API tests use Testcontainers. On Apple Silicon, enable *Use Rosetta for x86_64/amd64 emulation* (SQL Server has no arm64 image).
- First `dotnet run` of the AppHost asks macOS to trust the ASP.NET dev certificate (Keychain password prompt). Declining works only with `--launch-profile http`.

## Repo layout (target)

```
runners-diary/
├── CLAUDE.md
├── RunnersDiary.slnx                     # solution (new XML .slnx format)
├── Directory.Build.props                 # Nullable, TreatWarningsAsErrors, analyzers, LangVersion
├── Directory.Packages.props              # central NuGet package versions
├── global.json                           # pinned .NET 10 SDK
├── azure.yaml                            # azd project definition
├── src/
│   ├── RunnersDiary.AppHost/             # .NET Aspire orchestration (API, web, SQL, Azurite)
│   ├── RunnersDiary.ServiceDefaults/     # OpenTelemetry, health checks, resilience
│   ├── RunnersDiary.Api/                 # Minimal APIs, vertical slices: Features/<Area>/<UseCase>.cs
│   ├── RunnersDiary.Domain/              # pure C#: entities, run types, load metrics, Gpx/ — NO infra deps
│   └── RunnersDiary.Infrastructure/      # EF Core, migrations, Blob, Maps, Ai/, guest cleanup
├── tests/
│   ├── RunnersDiary.Domain.Tests/        # fast unit tests (metrics, GPX, ISO weeks)
│   ├── RunnersDiary.Api.Tests/           # integration tests (WebApplicationFactory + Testcontainers SQL)
│   ├── RunnersDiary.Ai.Tests/            # agent tests with fake chat client + eval cases
│   └── fixtures/gpx/                     # road, trail, corrupted, XXE, oversized…
├── web/                                  # React + TS + Vite
│   ├── src/api/generated/                # generated OpenAPI client — never hand-edit
│   ├── src/components/map/               # MapView abstraction (Azure Maps / MapLibre)
│   ├── src/features/<area>/              # calendar, runs, insights, auth
│   ├── src/i18n/                         # en (first), pl (next)
│   └── e2e/                              # Playwright
├── prompts/training-insights/            # versioned AI prompt templates (v1.md, …)
├── seed/demo/                            # demo dataset: runs.json + *.gpx (Gliwice / Silesia, synthetic)
├── infra/                                # Bicep: main.bicep + modules/
├── .github/workflows/                    # ci.yml, deploy.yml (OIDC)
└── docs/                                 # brief, architecture, ADRs, data model, API contract, milestone plans
```

## Commands (verified in M1)

```bash
# Run everything locally (SQL, Azurite, API, web) via Aspire; prints the dashboard URL
dotnet run --project src/RunnersDiary.AppHost

# Backend
dotnet build                       # must be warning-free (TreatWarningsAsErrors); also regenerates the OpenAPI doc
dotnet test                        # Microsoft.Testing.Platform (global.json); Docker needed for container tests
                                   # without Docker they are skipped locally, but fail when CI=true
dotnet test --report-xunit-trx     # TRX output (as CI does)

# Frontend (from repo root)
npm --prefix web ci
npm --prefix web run dev           # standalone: proxies /api to https://localhost:7080 (API https profile)
npm --prefix web run lint          # oxlint, warnings are errors
npm --prefix web run typecheck
npm --prefix web test -- --run     # Vitest (watch mode without --run)
npm --prefix web run test:tz       # Vitest for dates/features under America/Los_Angeles and Pacific/Kiritimati
npm --prefix web run build
npm --prefix web run test:e2e      # Playwright (fake API) vs `vite preview`: desktop + phone-la projects; first time: npx --prefix web playwright install chromium
# Full-stack journey against a running stack (AppHost or the CI job's setup); CI runs it on push to main + manual dispatch
FULLSTACK_BASE_URL=http://localhost:5173 npm --prefix web run test:e2e:fullstack
npm --prefix web run format        # Prettier

# API contract: after changing endpoints, build the API first, then regenerate the TS schema and commit both
dotnet build src/RunnersDiary.Api && npm --prefix web run generate:api

# EF Core migrations (dotnet-ef is a local tool in .config/dotnet-tools.json); the AppHost applies them on start
dotnet tool restore
dotnet ef migrations add <Name> --project src/RunnersDiary.Infrastructure --startup-project src/RunnersDiary.Api

# Entra External ID (once the tenant exists; non-secret, never committed) — see M2 plan §10
dotnet user-secrets set "Entra:TenantId" "<tenant GUID>" --project src/RunnersDiary.AppHost   # also TenantSubdomain, ApiClientId, SpaClientId, optional Issuer

# Later milestones
az bicep build --file infra/main.bicep   # M7 — validation only; never deploy without explicit user confirmation
```

CI (`.github/workflows/ci.yml`) runs three jobs on every PR and push to `main`: **backend** (build + tests with Testcontainers), **frontend** (lint, typecheck, Prettier, Vitest incl. `test:tz`, build, Playwright), **api-contract** (fails if the committed OpenAPI doc or generated TS schema is stale). A fourth job, **e2e-fullstack** (SQL + Azurite containers, API, `vite preview`, full-stack Playwright journeys: runs, GPX tracks, insights), runs only on push to `main` and manual dispatch; superseded PR runs are cancelled, `main` runs never are.

## Conventions

- **Backend**: C# only, .NET 10, Minimal APIs organized as vertical slices (endpoint + request/response + validator + handler in one feature file/folder). Nullable on, `TreatWarningsAsErrors`, analyzers on. RFC 9457 problem details for errors.
- **Domain purity**: load metrics, GPX parsing/metrics, ISO-week logic live in `RunnersDiary.Domain` with no EF/ASP.NET/Azure references, and are unit tested.
- **Units**: store SI (metres, seconds, m/s, metres of elevation); format in the UI only (km, min/km). Metric by default.
- **Time** ([ADR 0008](docs/adr/0008-run-date-and-time-storage.md)): a run stores `Date` (`DateOnly`, source of truth for calendar and all metrics), optional `StartTime` (`TimeOnly?`), `TimeZoneId` (IANA, default from the user's profile) and a derived `StartUtc` (ordering/GPX only, never grouping). "Today" is computed in the user's zone. Weeks are ISO, Monday-first.
- **Frontend**: TypeScript `strict` (pinned to 5.9 until `openapi-typescript` supports TS 7); call the API only via `$api`/`fetchClient` in `web/src/api/client.ts` (oxlint blocks `fetch` elsewhere); relative `/api` only — never an API host; no user-facing string literals in components (typed i18n keys in `web/src/i18n/en/*.json`); accessible calendar (ARIA grid, keyboard nav); responsive. UI kit: shadcn/ui (Radix base) + Tailwind v4 — add components with `npx shadcn add <name>` from `web/`.
- **Routing to the API**: the browser only talks to the web origin; `/api/*` is proxied (Vite in dev, the web container in Azure). The API never gets public ingress (ADR 0004).
- **Package pins**: versions live in `Directory.Packages.props` (central management). Vulnerable transitive packages are pinned in its "Security pins" group with the CVE and a removal condition. Container image tags that override Aspire defaults (Azurite) are pinned in both `AppHost.cs` and the test fixtures — keep them in sync.
- **Security**: every endpoint requires auth (users or guests) except health and guest-token issuance; every query is scoped to the caller's user id from claims; 404 for foreign resources. Every upload validated (size, type, safe XML: DTD prohibited, no resolver).
- **Secrets**: none in the repo — Aspire parameters / user-secrets locally, Key Vault + managed identity in Azure. Azure Maps keys never reach the browser.
- **Dependencies**: every new NuGet/npm package gets a one-line justification in the PR/commit or report; prefer well-maintained ones.
- **Fast-moving APIs**: check current docs (WebSearch/WebFetch) for Azure Maps, Microsoft Foundry, Microsoft Agent Framework, Aspire, Entra External ID before implementing.

## How we work

- Work in phases/milestones (M1–M7, see brief §4). **Do not skip ahead.** At the end of each phase: stop, summarize, list open questions, wait for approval.
- Before each milestone: **architect** writes `docs/plans/Mx-<name>.md` (files, contracts, risks).
- After each milestone: **qa-tester** runs all tests, **reviewer** reviews the diff, then stop for approval. Commit per milestone with a clear message.
- Ask when requirements are ambiguous; state assumptions explicitly when proceeding.
- Small, reviewable changes; keep the build green.
- Never deploy or change cloud resources (`azd up/provision/deploy/down`, `az ... create/delete`) without explicit user confirmation for that action.
- Keep this file and `docs/` up to date when decisions change.

## Which agent to use

| Agent | Use for |
|---|---|
| `architect` | Milestone plans, ADRs, architecture/data model/API contract docs; any cross-cutting decision. Writes only to `docs/`. |
| `backend-dotnet` | Minimal API endpoints, EF Core model/migrations, auth (Entra + guest JWT), guest lifecycle & seeding, Aspire wiring, domain load metrics, with tests. |
| `frontend-react` | React pages, calendar grid, forms, lists, dashboard charts, routing, MSAL, i18n, a11y, responsive layout. |
| `maps-gpx` | GPX parsing/validation/metrics/simplification, GeoJSON, Blob track storage, MapView (Azure Maps / MapLibre), geocoding, map token flow, elevation profile, demo GPX files. |
| `ai-insights` | Training Insights agent (Agent Framework + Foundry), read-only tools, prompts, grounding, caching, token/guest limits, evals. |
| `azure-infra` | Bicep, azd, Container Apps, ACR, SQL, Storage, Key Vault, Maps, Foundry, App Insights, managed identities, Dockerfiles, GitHub Actions, cost estimates. Never deploys without confirmation. |
| `qa-tester` | Runs all suites, writes missing tests and fixtures (incl. malicious GPX), reports failures with repro. Test code only. |
| `reviewer` | Read-only code & security review of each milestone diff (authZ/IDOR, XML/upload, secrets, guest abuse, cost, ADR consistency). |

Boundary notes: GPX endpoints are wired by `backend-dotnet` but the parsing/processing pipeline belongs to `maps-gpx`. Metrics are computed by `backend-dotnet` in `RunnersDiary.Domain`; `ai-insights` only consumes them. `DemoDataSeeder` is `backend-dotnet`; the demo GPX files are `maps-gpx`.
