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
- **M5 – Insights** — done (merged, stompsu/runners-diary#10). Metrics v1 (`metricsVersion` 1) is the read-only contract for M6. Plan and deviations (§12): [`docs/plans/M5-insights.md`](docs/plans/M5-insights.md).
- **M6 – AI summary** — done (merged, stompsu/runners-diary#11). AI is off by default (`Ai:Provider=None`); local options: `dotnet user-secrets set "Ai:UseFake" "true" --project src/RunnersDiary.AppHost` (fake model) or `Ai:Endpoint` + `Ai:Deployment` with `az login` (real Foundry, opt-in, costs money). Plan and deviations (§12): [`docs/plans/M6-ai-summary.md`](docs/plans/M6-ai-summary.md).
- **M7 – Azure deployment** — Part A (offline artifacts) done (merged, stompsu/runners-diary#12). Part B: **G1–G7 done 2026-11-04 to 06 — production is live** at `https://runners-diary.highpace.dev` (custom domain via `CUSTOM_DOMAIN`, managed certificate). It runs in `rg-runnersdiary-prod`, **West Europe**, with SQL in **Poland Central** via `SQL_LOCATION`. **AI summaries are on** (gpt-5.4-mini **Global Standard**, prompt v2, fact sheet v2; switch with `AI_ENABLED` + provision). Guests-only until **G8** (Entra; redirect URIs on the custom domain). Deploys: green CI on `main` → `deploy.yml` waits for the `prod` approval → images, migration job, port + readiness wait, smoke test, automatic rollback. Provisioning: run the hooks by hand around `azd provision` (`azd hooks run preprovision` / `postprovision`); use `--no-state` when only the live state changed. Live AI eval: `RD_AI_LIVE_EVAL=1` (costs money; see ADR 0026/0027). Every gate needs explicit user confirmation: [`docs/plans/M7-azure-deployment.md`](docs/plans/M7-azure-deployment.md) (ADRs 0029–0034; 0029 supersedes 0004).
- **M8 – Race proposals** — done (merged, stompsu/runners-diary#39; deployed 2026-11-14). Upcoming races from a shared, facts-only catalogue (primary source running.life, imported weekly by the Container Apps job `job-race-import` = API image with `--job race-import`) are proposed in the diary calendar per user interest profile; "I signed up" creates a linked planned race run. **Part B: G-R2 (job provisioned) and G-R3 (import on; first run imported 150 races) done 2026-11-14; G-R4 done 2026-11-14 (UI on, `RACES_UI_ENABLED=true`): race proposals are live for guests; G-R1 (permission e-mail to running.life) still to be sent.** Kill switches: `RACE_IMPORT_ENABLED`, `RACE_IMPORT_RUNNING_LIFE`, `RACES_UI_ENABLED` + `azd provision --no-state`. Locally the AppHost enables `Races__Enabled` and seeds the synthetic demo catalogue (`seed/races/demo-races.json`). Every remaining gate needs the user's explicit confirmation: [`docs/plans/M8-race-proposals.md`](docs/plans/M8-race-proposals.md) (ADRs 0035–0038; gate log in §6.2).
- **M9 – Adaptive training plan** — plan approved 2026-11-16 (outline + the four §7 defaults, §10 defaults confirmed); **Part A merged (stompsu/runners-diary#42) and deployed 2026-11-16. Part B: G-P1 (deploy), G-P2 (`PLANS_UI_ENABLED=true`: goals, demo plan and suggestions live for guests), G-P3 (live eval) and G-P4 (`PLANS_AI_ENABLED=true`: AI plan generation live, 2026-11-17) done, after the plan-reliability fixes (prompt v2, deterministic repair step incl. over-full weeks; stompsu/runners-diary#43, #44; live eval 21/21).** Locally the AppHost enables `Goals__Enabled`; plan generation needs a model (`Ai:UseFake` or Foundry). Goals page (`/goals`), realistic-target estimate, AI-suggested trainings up to the goal (≤ 16 weeks, deterministic envelope + validator, existing Foundry client, separate per-user quotas, weighted shared global budget), suggestions in the diary with Accept / Edit & accept / Dismiss, guest demo plan. Kill switches for Part B: `PLANS_UI_ENABLED` (`Goals__Enabled`) and `PLANS_AI_ENABLED` (`Ai__Plan__Enabled`); no new Azure resource; every gate needs the user's explicit confirmation: [`docs/plans/M9-adaptive-training-plan.md`](docs/plans/M9-adaptive-training-plan.md) (ADRs 0039–0041).
- **M10 – Polish localisation** — plan approved 2026-11-17 ("Approve with defaults"); **implemented on `m10-polish-localisation`, reviewed, G-L1 (live eval, en + pl) accepted 2026-11-17 (Polish summaries 51/54, plans 21/21); PR and G-L2 (merge + deploy) pending.** Full Polish UI (all namespaces, user-reviewed batches B1–B4), language switcher (header + sign-in; `EN/PL` on phones), language stored per user (`PUT /api/me/language`, guest `?lang=`), Polish demo data (`seed/demo/i18n/pl.json`; startup fails if incomplete), AI summaries and plans in the UI language (prompt v3, per-language templates/deny lists; Polish-only validator rules for number words, `genderedForm`, `wrongLanguage`). Glossary and style: [`docs/i18n.md`](docs/i18n.md). Every gate needs the user's explicit confirmation: [`docs/plans/M10-polish-localisation.md`](docs/plans/M10-polish-localisation.md) (ADRs 0042–0043).
- Architecture: [`docs/architecture.md`](docs/architecture.md); data model: [`docs/data-model.md`](docs/data-model.md); API contract: [`docs/api-contract.md`](docs/api-contract.md); decisions: [`docs/adr/`](docs/adr/) (0001–0043).

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
│   └── RunnersDiary.Infrastructure/      # EF Core, migrations, Blob, Maps, Ai/, guest cleanup, Races/, RaceSources/ (M8 importer)
├── tests/
│   ├── RunnersDiary.Domain.Tests/        # fast unit tests (metrics, GPX, ISO weeks)
│   ├── RunnersDiary.Api.Tests/           # integration tests (WebApplicationFactory + Testcontainers SQL)
│   ├── RunnersDiary.Ai.Tests/            # agent tests with fake chat client + eval cases
│   └── fixtures/gpx/                     # road, trail, corrupted, XXE, oversized…
├── web/                                  # React + TS + Vite
│   ├── src/api/generated/                # generated OpenAPI client — never hand-edit
│   ├── src/components/map/               # MapView abstraction (Azure Maps / MapLibre)
│   ├── src/features/<area>/              # calendar, runs, insights, auth, races (M8), goals (M9)
│   ├── src/i18n/                         # en + pl (every key in both; parity test)
│   └── e2e/                              # Playwright
├── prompts/training-insights/            # versioned AI prompt templates (v1.md, …)
├── prompts/training-plan/                # M9: versioned plan-generation prompts (v1.md, manifest.json)
├── seed/demo/                            # demo dataset: runs.json + *.gpx (Gliwice / Silesia, synthetic)
├── seed/races/                           # synthetic demo race catalogue (M8; Development/Testing only)
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
az bicep build --file infra/main.bicep && az bicep lint --file infra/main.bicep   # validation only
docker build --file src/RunnersDiary.Api/Dockerfile -t rd-api:local . && docker build -t rd-web:local web   # images (also infra/docker/migrate.Dockerfile)
# Deploying (azd provision, deploy.yml) = Part B gates G0–G13 in docs/plans/M7-azure-deployment.md; never without explicit user confirmation per gate
```

CI (`.github/workflows/ci.yml`) runs four jobs on every PR and push to `main` (the fourth, **deploy-artifacts**, does the Bicep build/lint, actionlint, shellcheck, builds the 3 images, runs the nginx header checks and runs Playwright against the web image under the real CSP): **backend** (build + tests with Testcontainers), **frontend** (lint, typecheck, Prettier, Vitest incl. `test:tz`, build, Playwright), **api-contract** (fails if the committed OpenAPI doc or generated TS schema is stale). A fifth job, **e2e-fullstack** (SQL + Azurite containers plus the API and web **images**, full-stack Playwright journeys: runs, GPX tracks, insights, AI summary demo), runs only on push to `main` and manual dispatch; superseded PR runs are cancelled, `main` runs never are.

## Conventions

- **Backend**: C# only, .NET 10, Minimal APIs organized as vertical slices (endpoint + request/response + validator + handler in one feature file/folder). Nullable on, `TreatWarningsAsErrors`, analyzers on. RFC 9457 problem details for errors.
- **Domain purity**: load metrics, GPX parsing/metrics, ISO-week logic live in `RunnersDiary.Domain` with no EF/ASP.NET/Azure references, and are unit tested.
- **Units**: store SI (metres, seconds, m/s, metres of elevation); format in the UI only (km, min/km). Metric by default.
- **Time** ([ADR 0008](docs/adr/0008-run-date-and-time-storage.md)): a run stores `Date` (`DateOnly`, source of truth for calendar and all metrics), optional `StartTime` (`TimeOnly?`), `TimeZoneId` (IANA, default from the user's profile) and a derived `StartUtc` (ordering/GPX only, never grouping). "Today" is computed in the user's zone. Weeks are ISO, Monday-first.
- **Frontend**: TypeScript `strict` (pinned to 5.9 until `openapi-typescript` supports TS 7); call the API only via `$api`/`fetchClient` in `web/src/api/client.ts` (oxlint blocks `fetch` elsewhere); relative `/api` only — never an API host; no user-facing string literals in components (typed i18n keys in `web/src/i18n/en/*.json`, and **every new key also in `web/src/i18n/pl/*.json`** — the parity test fails otherwise; Polish style and glossary in `docs/i18n.md`); accessible calendar (ARIA grid, keyboard nav); responsive. UI kit: shadcn/ui (Radix base) + Tailwind v4 — add components with `npx shadcn add <name>` from `web/`.
- **Routing to the API**: the browser only talks to the web origin; `/api/*` is proxied (Vite in dev, the web container in Azure). The API never gets public ingress (ADR 0004, superseded by ADR 0029: nginx web container proxies `/api` to the internal-ingress API).
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

Boundary notes: GPX endpoints are wired by `backend-dotnet` but the parsing/processing pipeline belongs to `maps-gpx`. Metrics are computed by `backend-dotnet` in `RunnersDiary.Domain`; `ai-insights` only consumes them. `DemoDataSeeder` is `backend-dotnet`; the demo GPX files are `maps-gpx`. The race catalogue importer (`RaceSources/`, polite crawling per ADR 0035) and the race endpoints are `backend-dotnet`; the race UI is `frontend-react` and the `MapView` radius/pin additions are `maps-gpx`.
