# Runners Diary

A web app for runners to plan and track training and races: monthly calendar diary, run add/edit (planned → completed), race details and experience ratings, GPX import with map + elevation profile, an Insights dashboard (load, ACWR, trends) and an AI-generated period summary grounded in deterministically computed metrics. Multi-user (Entra External ID) with an isolated, 24 h guest/demo mode.

Full requirements: [`docs/PROJECT_BRIEF.md`](docs/PROJECT_BRIEF.md). Architecture and decisions: [`docs/architecture.md`](docs/architecture.md), [`docs/adr/`](docs/adr/).

## Current status

- **Phase 0** (agents + project memory) — done, awaiting approval.
- Next: **M1 – Skeleton**. No application code exists yet; the commands below are the *target* and will be verified in M1.

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

## Commands (target — confirmed in M1)

```bash
# Run everything locally (API, web, SQL, Azurite) via Aspire
dotnet run --project src/RunnersDiary.AppHost

# Backend
dotnet build                       # must be warning-free (TreatWarningsAsErrors)
dotnet test                        # Docker required for Testcontainers
dotnet ef migrations add <Name> --project src/RunnersDiary.Infrastructure --startup-project src/RunnersDiary.Api

# Frontend (from repo root)
npm --prefix web install
npm --prefix web run dev
npm --prefix web run lint
npm --prefix web run typecheck
npm --prefix web test              # Vitest
npm --prefix web run test:e2e      # Playwright
npm --prefix web run generate:api  # regenerate TS client from the API's OpenAPI document

# Infra (validation only — never deploy without explicit user confirmation)
az bicep build --file infra/main.bicep
```

## Conventions

- **Backend**: C# only, .NET 10, Minimal APIs organized as vertical slices (endpoint + request/response + validator + handler in one feature file/folder). Nullable on, `TreatWarningsAsErrors`, analyzers on. RFC 9457 problem details for errors.
- **Domain purity**: load metrics, GPX parsing/metrics, ISO-week logic live in `RunnersDiary.Domain` with no EF/ASP.NET/Azure references, and are unit tested.
- **Units**: store SI (metres, seconds, m/s, metres of elevation); format in the UI only (km, min/km). Metric by default.
- **Time**: store run start as local date + local time plus the user's IANA time zone (decision to confirm in M2 ADR); weeks are ISO, Monday-first.
- **Frontend**: TypeScript `strict`; call the API only via the generated client; no user-facing string literals in components (i18n keys); accessible calendar (ARIA grid, keyboard nav); responsive.
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
