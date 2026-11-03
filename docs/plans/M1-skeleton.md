# M1 – Skeleton: plan

**Status:** Draft for approval · **Date:** 2026-11-03 · **Author:** architect
**Related ADRs:** [0001](../adr/0001-solution-structure-and-aspire.md) structure & Aspire · [0002](../adr/0002-openapi-typescript-client.md) OpenAPI → TS · [0003](../adr/0003-frontend-stack-and-ui-kit.md) frontend · [0004](../adr/0004-web-hosting.md) web hosting (proposed) · [0005](../adr/0005-ci-pipeline.md) CI · [0006](../adr/0006-database-azure-sql.md) Azure SQL · [0007](../adr/0007-api-surface-conventions.md) API conventions

## 1. Goal & acceptance criteria

**Goal (brief §4):** solution + Aspire AppHost, API with health endpoint, React app shell with routing, generated API client, local SQL + Azurite, CI building and testing both apps.

**Out of scope:** domain entities, migrations, auth (Entra/guest), i18n for Polish, any Azure resources/Bicep/deploy, forms, charts, calendar UI.

Refined, testable criteria:

| # | Criterion | How verified |
|---|---|---|
| AC1 | Clean clone: `dotnet build` (solution `RunnersDiary.slnx`) succeeds with **0 warnings, 0 errors**. | CI backend job; local |
| AC2 | With Docker running, `dotnet run --project src/RunnersDiary.AppHost` brings up `sql`, `runnersdiary`, `storage`, `blobs`, `api`, `web`; all reach *Running/Healthy* in the Aspire dashboard (after first image pull). | Manual checklist (qa-tester), screenshot/notes in report |
| AC3 | `GET /alive` → 200 `Healthy`; `GET /health` → 200 `Healthy` with SQL + Blob checks; stopping the SQL container makes `/health` return 503. | Integration test + manual |
| AC4 | `GET /api/health` → 200 `{"status":"Healthy","version":"…"}`, documented in the OpenAPI doc; errors are `application/problem+json` (e.g. `GET /api/does-not-exist` → 404 problem details). | Integration tests |
| AC5 | Web: `/` redirects to `/diary/{currentYear}/{currentMonth}`; `/diary/2026/11` and `/insights` render placeholder pages inside the app shell; `/diary/2026/13` and unknown paths render the NotFound page; shell shows an API status indicator fed by `/api/health` through the generated client and the `/api` proxy. | Vitest + Playwright smoke + manual |
| AC6 | `npm --prefix web run generate:api` on a clean checkout produces **no git diff**; generated schema contains `/api/health`. | CI api-contract job |
| AC7 | `dotnet test`, `npm --prefix web run lint`, `typecheck`, `test`, `build`, `test:e2e` all pass locally and in CI. | CI + qa-tester |
| AC8 | CI (`.github/workflows/ci.yml`) green on the M1 PR: jobs `backend`, `frontend`, `api-contract`. | GitHub checks |
| AC9 | No secrets committed (SQL password is the Aspire-generated parameter in user-secrets); `RunnersDiary.Domain.csproj` has no Package/ProjectReferences. | reviewer |

## 2. Files / projects by owning agent

### backend-dotnet
```
global.json                          # sdk 10.0.400, rollForward latestFeature; test.runner = Microsoft.Testing.Platform
RunnersDiary.slnx
Directory.Build.props                # net10.0, Nullable, ImplicitUsings, TreatWarningsAsErrors, AnalysisLevel=latest, AnalysisMode=Recommended, EnforceCodeStyleInBuild
Directory.Packages.props             # ManagePackageVersionsCentrally=true; all versions below
.editorconfig                        # C# style + analyzer severities (each suppression commented)
.gitignore                           # bin/ obj/ .vs/ .idea/ *.user node_modules/ dist/ playwright-report/ test-results/ .env*
src/RunnersDiary.AppHost/            # AppHost.cs (Program), RunnersDiary.AppHost.csproj (Sdk="Aspire.AppHost.Sdk/13.6.4"), Properties/launchSettings.json
src/RunnersDiary.ServiceDefaults/    # Extensions.cs (from template; health endpoints mapped in ALL environments, ADR 0007)
src/RunnersDiary.Api/
  Program.cs
  Features/Health/GetApiHealth.cs    # GET /api/health
  openapi/runnersdiary-api.json      # build-time generated, committed
  Properties/launchSettings.json     # http 5080, https 7080 (standalone runs only)
  appsettings.json, appsettings.Development.json   # no connection strings
src/RunnersDiary.Infrastructure/
  Persistence/RunnersDiaryDbContext.cs   # empty DbContext (no DbSets, no migrations) – seam for M2
  DependencyInjection.cs             # AddInfrastructure(this IHostApplicationBuilder): SQL DbContext + Blob client
src/RunnersDiary.Domain/
  RunnersDiary.Domain.csproj         # no references; one AssemblyMarker.cs so the project builds
tests/RunnersDiary.Api.Tests/        # see qa-tester; backend-dotnet writes the fixture together with the endpoint
```

### frontend-react
```
web/.nvmrc                           # 26
web/package.json, package-lock.json
web/index.html, vite.config.ts, tsconfig.json, tsconfig.app.json, tsconfig.node.json
web/eslint.config.js, .prettierrc, components.json (shadcn)
web/src/main.tsx
web/src/index.css                    # @import "tailwindcss"; shadcn theme tokens
web/src/app/providers.tsx            # QueryClientProvider, I18nextProvider
web/src/app/router.tsx               # createBrowserRouter route objects
web/src/components/layout/AppShell.tsx, ApiStatus.tsx
web/src/components/ui/               # shadcn: button (+ whatever the shell needs, e.g. badge)
web/src/features/diary/DiaryPage.tsx         # placeholder; validates :year/:month
web/src/features/insights/InsightsPage.tsx   # placeholder
web/src/features/errors/NotFoundPage.tsx, RouteErrorPage.tsx
web/src/api/client.ts                # openapi-fetch + openapi-react-query ($api)
web/src/api/generated/schema.d.ts    # generated, never hand-edited
web/src/i18n/index.ts, en/common.json
web/src/lib/utils.ts                 # shadcn cn()
web/vitest.config.ts (or in vite.config), src/test/setup.ts
web/playwright.config.ts
```

### azure-infra (CI only in M1)
```
.github/workflows/ci.yml
.github/dependabot.yml               # github-actions, nuget, npm – weekly
```

### qa-tester
```
tests/RunnersDiary.Api.Tests/
  RunnersDiary.Api.Tests.csproj
  Infrastructure/ApiFactory.cs       # WebApplicationFactory<Program> + Testcontainers MsSql + Azurite (IAsyncLifetime, shared collection fixture)
  Health/HealthEndpointsTests.cs
  Errors/ProblemDetailsTests.cs
  OpenApi/OpenApiDocumentTests.cs    # committed doc exists & contains /api/health
web/src/**/*.test.tsx                # router + ApiStatus tests
web/e2e/smoke.spec.ts
docs/plans/M1-skeleton.md            # (architect) — qa appends acceptance checklist results in its report, not here
```

> No `RunnersDiary.Domain.Tests` / `Ai.Tests` in M1: with Microsoft.Testing.Platform a test project with **zero tests fails** `dotnet test` (exit code 8). They are created in M2/M6 with real tests.

## 3. Contracts

### 3.1 Aspire resources (names are contracts)
| Resource | Definition | Consumer receives |
|---|---|---|
| `sql` | `AddSqlServer("sql").WithDataVolume().WithLifetime(ContainerLifetime.Persistent)` (default image `mcr.microsoft.com/mssql/server:2022-latest`) | — |
| `runnersdiary` | `sql.AddDatabase("runnersdiary")` | api: `ConnectionStrings__runnersdiary` |
| `storage` | `AddAzureStorage("storage").RunAsEmulator(a => a.WithDataVolume().WithLifetime(ContainerLifetime.Persistent))` | — |
| `blobs` | `storage.AddBlobs("blobs")` | api: `ConnectionStrings__blobs` |
| `api` | `AddProject<Projects.RunnersDiary_Api>("api").WithReference(db).WithReference(blobs).WaitFor(db).WaitFor(blobs).WithHttpHealthCheck("/health")` | web: `API_HTTP`, `API_HTTPS` |
| `web` | `AddViteApp("web", "../../web").WithReference(api).WaitFor(api)` (auto `npm install`; Aspire sets `PORT`) | — |

### 3.2 Ports & env vars
- Aspire assigns dynamic ports; nothing hard-codes them. `web/vite.config.ts`: `server.port = Number(process.env.PORT) || 5173`, `strictPort: true` when `PORT` is set; `server.proxy['/api'] = { target: process.env.API_HTTPS ?? process.env.API_HTTP ?? 'https://localhost:7080', changeOrigin: true, secure: false }`.
- API `launchSettings.json` fixed ports (http 5080 / https 7080) exist only for running the API standalone (`npm run dev` without Aspire then uses the fallback above).
- Frontend never knows an API host: all calls are relative `/api/...` (ADR 0004).

### 3.3 HTTP endpoints (all anonymous in M1; ADR 0007)
| Method/Path | Response | In OpenAPI |
|---|---|---|
| `GET /alive` | 200 `text/plain` `Healthy` (self check only) | no |
| `GET /health` | 200/503 `text/plain` `Healthy\|Degraded\|Unhealthy` (all checks: self, SQL DbContext, Blob) | no |
| `GET /api/health` | 200 (Healthy/Degraded) or 503 (Unhealthy) `application/json` `ApiHealthResponse` | yes, `operationId: getApiHealth` |
| any error | `application/problem+json` (RFC 9457) | via `ProducesProblem` where relevant |

```csharp
// Features/Health/GetApiHealth.cs
public sealed record ApiHealthResponse(ApiHealthStatus Status, string Version);
public enum ApiHealthStatus { Healthy, Degraded, Unhealthy }   // serialized as strings (JsonStringEnumConverter)
// Version = AssemblyInformationalVersion (e.g. "1.0.0+<git sha>")
// Implementation: HealthCheckService.CheckHealthAsync(); no per-check details returned.
```
Generated TS shape (expected): `components["schemas"]["ApiHealthResponse"] = { status: "Healthy" | "Degraded" | "Unhealthy"; version: string }`.

### 3.4 OpenAPI document & generation
- Document name `v1`; runtime `/openapi/v1.json` in Development only.
- Build-time output: **`src/RunnersDiary.Api/openapi/runnersdiary-api.json`** (OpenAPI 3.1), via `Microsoft.Extensions.ApiDescription.Server` with
  `<OpenApiDocumentsDirectory>$(MSBuildProjectDirectory)/openapi</OpenApiDocumentsDirectory>` and `<OpenApiGenerateDocumentsOptions>--file-name runnersdiary-api</OpenApiGenerateDocumentsOptions>`.
- `Program.cs` skips `AddServiceDefaults()`/`AddInfrastructure()` when the entry assembly is `GetDocument.Insider`, but keeps endpoint mapping identical.
- `web/package.json`: `"generate:api": "openapi-typescript ../src/RunnersDiary.Api/openapi/runnersdiary-api.json -o src/api/generated/schema.d.ts"`.
- `web/src/api/client.ts` exports `fetchClient` (openapi-fetch, `baseUrl: "/"`) and `$api` (openapi-react-query). M2 adds an auth middleware here; nothing else changes.

### 3.5 npm scripts (`web/package.json`) — must match CLAUDE.md
`dev` (vite) · `build` (`tsc -b && vite build`) · `preview` · `lint` (eslint .) · `typecheck` (`tsc -b --noEmit` or `tsc -p tsconfig.app.json --noEmit`) · `test` (vitest) · `test:e2e` (playwright test) · `generate:api` · `format` (prettier --write).

## 4. Implementation steps (each = one reviewable commit)

1. **backend-dotnet – repo scaffolding.** `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, `.gitignore`, `RunnersDiary.slnx`, empty `Domain` and `Infrastructure` (+ empty `RunnersDiaryDbContext`). `dotnet build` green.
2. **backend-dotnet – ServiceDefaults + Api.** ServiceDefaults from the Aspire template (`dotnet new install Aspire.ProjectTemplates::13.6.4` → `dotnet new aspire-servicedefaults`, or hand-authored per aspire.dev) with health endpoints in all environments. Api: `AddProblemDetails`, `UseExceptionHandler`, `UseStatusCodePages`, `AddOpenApi`, `/api` group, `GET /api/health`, `AddInfrastructure()` (Aspire SQL EF + Blob client), `GetDocument.Insider` guard, build-time OpenAPI → commit JSON.
3. **backend-dotnet – AppHost (sql, storage, api).** Verify locally: dashboard green, `/health` Healthy. Run `dotnet dev-certs https --trust` if needed.
4. **qa-tester (+backend-dotnet fixture) – Api.Tests.** Testcontainers SQL + Azurite fixture injecting `ConnectionStrings:runnersdiary` / `ConnectionStrings:blobs`; tests listed in §6.
5. **frontend-react – web scaffold.** Vite React-TS, strict TS, Tailwind v4 + `@tailwindcss/vite`, `@/` alias, `shadcn init` + `button`, ESLint/Prettier, i18n (en), router (data mode) with AppShell, Diary/Insights/NotFound/RouteError pages, `.nvmrc`.
6. **frontend-react – API client.** `openapi-typescript`, `openapi-fetch`, `openapi-react-query`; `generate:api`; `client.ts`; `ApiStatus` component using `$api.useQuery("get", "/api/health")`; Vite `PORT` + `/api` proxy; lint rule forbidding direct `fetch` outside `src/api/`.
7. **backend-dotnet – add `web` to AppHost** (`Aspire.Hosting.JavaScript`, `AddViteApp`). Verify AC2/AC5 manually.
8. **qa-tester – frontend tests.** Vitest/RTL tests + Playwright smoke (§6).
9. **azure-infra – CI.** `ci.yml` (3 jobs per ADR 0005) + `dependabot.yml`. Open PR; iterate to green.
10. **qa-tester → reviewer → user.** Full test run + AC checklist; reviewer reviews diff vs ADRs; main agent updates CLAUDE.md "Current status"/commands; commit "M1: skeleton"; stop for approval.

## 5. Risks

| Risk | Impact | Mitigation |
|---|---|---|
| **SQL Server container on Apple Silicon.** No arm64 image; runs only under Rosetta x64 emulation. Reports that macOS 26 broke Rosetta for SQL Server 2025 RC; Apple says Rosetta stays only through macOS 27 (this machine reports Darwin 27 = macOS 27). | AppHost can't start SQL locally | Enable Docker Desktop → "Use Rosetta for x86_64/amd64 emulation"; keep Aspire's default `2022-latest` tag. If it fails: (a) try OrbStack/Colima with Rosetta; (b) fallback AppHost switch to `AddConnectionString("runnersdiary")` pointing at a dev Azure SQL free-offer DB (requires user approval to create a cloud resource). CI (x64 Ubuntu) unaffected. **Confirm CPU (`uname -m`).** |
| Docker daemon not running | AC2/tests fail | Documented prerequisite; qa-tester starts by checking `docker info`. |
| Build-time OpenAPI runs `Program.cs` (mock server) | build fails on missing connection strings | `GetDocument.Insider` guard (ADR 0002). |
| `TreatWarningsAsErrors` + `AnalysisMode=Recommended` noise (incl. generated/template code) | slow start | Tune in `.editorconfig` with a comment per rule; no blanket `NoWarn`. |
| Vite does not read `PORT` by itself | web endpoint unreachable in Aspire | `vite.config.ts` reads `process.env.PORT` explicitly (§3.2). |
| Dev HTTPS cert untrusted on macOS | proxy/browser errors | `dotnet dev-certs https --trust`; proxy `secure: false`. |
| Fast-moving majors (Aspire 13.6, RR v8, Tailwind v4, Vite, xUnit v3 4.x/MTP v2) | template drift, peer-dep conflicts | Pin exact versions centrally; agents check current docs before each step. |
| Aspire auto `npm install` vs committed lockfile | lockfile churn | Commit `package-lock.json`; CI uses `npm ci`. Consider `.WithNpm(installCommand: "ci")` if churn appears. |
| Node 26 newer than some tooling expects | install warnings | Pin `.nvmrc` = 26 (local version); fall back to 24 LTS if a dependency refuses. |

## 6. Test plan outline (qa-tester)

**Backend – `RunnersDiary.Api.Tests` (xUnit v3, MTP, Testcontainers)**
- `GET /alive` → 200 `Healthy`.
- `GET /health` → 200 `Healthy` with SQL + Azurite containers up.
- `GET /health` → 503 when SQL connection string points to an unreachable server (separate factory, short timeout).
- `GET /api/health` → 200, JSON `status == "Healthy"`, non-empty `version`; no check names in body.
- `GET /api/does-not-exist` → 404 `application/problem+json` with `status: 404`.
- Unhandled exception path (test-only endpoint registered by the factory) → 500 problem details without stack trace in Production environment.
- OpenAPI: committed `openapi/runnersdiary-api.json` exists, `openapi` starts with `3.1`, contains `/api/health` with `operationId: getApiHealth`; `/health` and `/alive` absent.

**Frontend – Vitest + RTL**
- Router: `/` redirects to current year/month (fake timers); `/diary/2026/11` renders Diary heading; `/diary/2026/13`, `/diary/abc/1`, `/nope` render NotFound; `/insights` renders.
- `ApiStatus`: healthy / unhealthy (503) / network error states, using a mocked `fetchClient` (or MSW if qa prefers — would need a justification).
- i18n: shell renders translated text from `en/common.json` (no raw keys).

**E2E – Playwright (chromium only in M1)**
- Smoke against `vite preview`: open `/`, assert redirect to `/diary/YYYY/MM`, shell visible, `/api/health` mocked via `page.route` → status shows "healthy"; keyboard: nav links reachable via Tab.

**Manual (AC2/AC3/AC5)**: start Docker → `dotnet run --project src/RunnersDiary.AppHost` → dashboard resources all healthy → open `web` URL → status indicator healthy → stop `sql` container in dashboard → indicator unhealthy, `/health` 503 → restart.

## 7. Dependencies

### NuGet (versions pinned in `Directory.Packages.props`)
| Package | Project | Why |
|---|---|---|
| `Aspire.AppHost.Sdk` 13.6.4 (MSBuild SDK) | AppHost | Aspire AppHost build/run support. |
| `Aspire.Hosting.AppHost` | AppHost | Core AppHost APIs (`DistributedApplication`). |
| `Aspire.Hosting.SqlServer` | AppHost | SQL Server container + database resource. |
| `Aspire.Hosting.Azure.Storage` | AppHost | Azure Storage resource with Azurite emulator. |
| `Aspire.Hosting.JavaScript` | AppHost | `AddViteApp` for the React dev server (replaces `Aspire.Hosting.NodeJs`). |
| `Microsoft.Extensions.Http.Resilience` | ServiceDefaults | Standard resilience handlers (template). |
| `Microsoft.Extensions.ServiceDiscovery` | ServiceDefaults | Resolve Aspire service references (template). |
| `OpenTelemetry.Extensions.Hosting` | ServiceDefaults | OTel host integration (template). |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` | ServiceDefaults | OTLP export to dashboard / App Insights (template). |
| `OpenTelemetry.Instrumentation.AspNetCore` / `.Http` / `.Runtime` | ServiceDefaults | Request, HttpClient and runtime telemetry (template). |
| `Microsoft.AspNetCore.OpenApi` | Api | Built-in OpenAPI 3.1 document generation. |
| `Microsoft.Extensions.ApiDescription.Server` (PrivateAssets=all) | Api | Build-time OpenAPI document emission for the TS client. |
| `Aspire.Microsoft.EntityFrameworkCore.SqlServer` | Infrastructure | Registers `RunnersDiaryDbContext` with retries, health check, telemetry from the Aspire connection string. |
| `Aspire.Azure.Storage.Blobs` | Infrastructure | `BlobServiceClient` with health check + telemetry (used from M4). |
| `xunit.v3` (4.x, MTP v2) | Api.Tests | Test framework; MTP runner via `global.json`. |
| `Microsoft.AspNetCore.Mvc.Testing` | Api.Tests | `WebApplicationFactory` in-memory API host. |
| `Testcontainers.MsSql` | Api.Tests | Real SQL Server for integration tests. |
| `Testcontainers.Azurite` | Api.Tests | Real Blob emulator so `/health` is genuinely Healthy. |

Deferred: `Microsoft.EntityFrameworkCore.Design` + `dotnet-ef` tool manifest (M2, first migration), NetTopologySuite (M2), FluentValidation decision (M2).

### npm (`web/`)
| Package | Dep type | Why |
|---|---|---|
| `react`, `react-dom` (19.2.x ≥ 19.2.7) | prod | UI runtime; RR v8 minimum. |
| `react-router` (8.x) | prod | Routing, data mode (ADR 0003). |
| `@tanstack/react-query` (5.x) | prod | Server-state cache. |
| `openapi-fetch` | prod | Typed fetch client from generated `paths`. |
| `openapi-react-query` | prod | TanStack Query bindings over openapi-fetch. |
| `i18next`, `react-i18next` | prod | i18n from day one (no string literals in components). |
| `radix-ui`, `class-variance-authority`, `clsx`, `tailwind-merge`, `lucide-react`, `tw-animate-css` | prod | Added by `shadcn init/add` — primitives, variants, class merging, icons, animations. |
| `vite`, `@vitejs/plugin-react` | dev | Build/dev server. |
| `typescript`, `@types/react`, `@types/react-dom`, `@types/node` | dev | Strict typing; node types for `vite.config.ts`. |
| `tailwindcss` (4.x), `@tailwindcss/vite` | dev | Styling per shadcn Vite guide. |
| `openapi-typescript` (7.x) | dev | Generate `schema.d.ts` from the OpenAPI 3.1 doc. |
| `eslint`, `@eslint/js`, `typescript-eslint`, `globals` | dev | Linting (flat config). |
| `eslint-plugin-react-hooks`, `eslint-plugin-react-refresh` | dev | Hooks rules; HMR-safe exports. |
| `eslint-plugin-jsx-a11y` | dev | Accessibility lint (calendar ARIA requirement). |
| `prettier` | dev | Formatting. |
| `vitest`, `jsdom` | dev | Unit/component test runner + DOM. |
| `@testing-library/react`, `@testing-library/jest-dom`, `@testing-library/user-event` | dev | Component testing & a11y-oriented queries. |
| `@playwright/test` | dev | E2E smoke. |
| `@tanstack/react-query-devtools` | dev (optional) | Query debugging in dev only; skip if not wanted. |

Not in M1: react-hook-form, zod (M3), Recharts (M5), MSAL (M3), map libs (M4).

## 8. Assumptions

1. Solution file is `RunnersDiary.slnx` at repo root; `dotnet build`/`dotnet test` at root pick it up.
2. Aspire 13.6.4 is used without installing the Aspire CLI; templates or hand-authored projects are both fine.
3. npm (not pnpm) is the frontend package manager, matching CLAUDE.md commands.
4. Node pinned to 26 (`web/.nvmrc`), the version installed locally.
5. Everything under `/api`; no API URL versioning yet (ADR 0007).
6. M1 ships an empty `RunnersDiaryDbContext` (no migrations) so SQL readiness is real; the first migration is M2.
7. ADR 0004 recommendation (Aspire YARP static site on ACA) is *proposed*; M1 only depends on the relative `/api` contract, which works for every option.
8. Branch protection rules on `main` are set by the user in GitHub.
9. Run time-zone storage remains an M2 decision (pending question), not affected by M1.
10. **Local SQL on Apple Silicon (decided 2026-11-03):** the dev machine is an Apple M1 (`arm64`). First try the SQL Server container under Docker Desktop's Rosetta emulation. If it won't run, the user has accepted a **dev Azure SQL database on the free offer** as the fallback: the AppHost then uses `AddConnectionString("runnersdiary")` with the connection string in user-secrets (Entra auth, no password in the repo). azure-infra provisions it only after the user confirms that specific action. Testcontainers SQL tests still run in CI (x64); locally they need Rosetta, otherwise they are skipped with a clear message.

## 9. Open questions for the user

1. ~~**CPU / macOS**~~ — resolved: Apple M1 (`arm64`); Azure SQL free-offer dev DB accepted as fallback (assumption 10). Please enable Rosetta in Docker Desktop.
2. **Web hosting (ADR 0004)**: accept "Aspire YARP static site on Container Apps (nginx as fallback)", or prefer nginx explicitly, or SWA Free?
3. **Models per agent** (pending from Phase 0): no impact on M1 content.
4. **Run time-zone storage** (pending, M2): no impact on M1.
5. Do you want an automated AppHost end-to-end test (`Aspire.Hosting.Testing`) in CI now, or keep AC2 as a manual check until later? (Plan assumes manual.)
6. OK to include `@tanstack/react-query-devtools` (dev-only)?

## 10. Implementation notes & deviations (2026-11-03)

| # | Deviation from this plan | Reason |
|---|---|---|
| 1 | Azurite image pinned to `3.37.0` (AppHost `WithImageTag` + test fixture) | Aspire 13.6.4 defaults to 3.35.0, which rejects the Storage SDK's API version `2026-06-06` (health check 400). Remove the pin once Aspire's default catches up. |
| 2 | `System.Security.Cryptography.Pkcs` pinned to 10.0.14 (central transitive pinning) | CVE-2026-84008 / GHSA-846f-f764-76mx in 10.0.12, pulled in by `Aspire.Microsoft.EntityFrameworkCore.SqlServer` 13.6.4; restore fails under `TreatWarningsAsErrors`. |
| 3 | TypeScript 5.9 instead of 7.0 (Vite 8 template default) | `openapi-typescript` 7.x has a `typescript ^5` peer dependency (uses the TS compiler API). |
| 4 | **oxlint** instead of ESLint + plugins | Vite 8 template default; jsx-a11y, react hooks and import rules built in; replaces ~6 dev dependencies. `no-restricted-globals` blocks `fetch` outside `src/api/` (tests exempt). |
| 5 | `cn` instead of `clsx` + `tailwind-merge` | Installed by the shadcn CLI 4.x (package from `shadcn-ui/cn`, no install scripts, no dependencies). |
| 6 | `--report-xunit-trx` instead of `--report-trx` | xunit.v3's built-in TRX reporter; `--report-trx` needs an extra MTP extension package. |
| 7 | `fetchClient` `baseUrl` = `window.location.origin` instead of `/` | openapi-fetch builds a `Request`, which needs an absolute URL outside browsers (Vitest). Still same-origin; no API host in the bundle. |
| 8 | AppHost `http` profile sets `ASPIRE_ALLOW_UNSECURED_TRANSPORT=true` | Missing from the 13.6.4 template; needed to run without trusting the dev certificate. |
| 9 | Container-backed API tests skip (not fail) without Docker locally; fail when `CI=true` | Lets offline tests run anywhere without hiding a broken CI runner. |
| 10 | Local SQL: container works on Apple M1 (Docker Desktop, Rosetta) | Cloud dev DB fallback (assumption 10) not needed. |

Verified locally: AppHost brings up `sql`, `storage`, `api`, `web`; `/alive`, `/health`, `/api/health` 200; stopping SQL → 503 and "API unavailable" badge; restart → 200. Release build 0 warnings; 9/9 .NET tests, 24/24 Vitest, 3/3 Playwright; no OpenAPI/TS contract drift.

**Review follow-ups applied before commit:** `/health` and `/alive` explicitly `.AllowAnonymous()`; endpoint-inventory test (every endpoint needs authorization unless allowlisted); `/api/health` served from a 30 s cached health result and the SPA no longer polls it (serverless SQL must be able to auto-pause); container fixtures build lazily; `strict` in `tsconfig.node.json`; lint also blocks `window/globalThis/self.fetch` and `XMLHttpRequest`; the drift check also catches untracked generated files. Still open for M7: readiness probes must not hit SQL on every call; consider pinning a SQL Server CU image tag.
