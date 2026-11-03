# 0005 – CI pipeline shape (GitHub Actions)

**Status:** Accepted (M1)
**Date:** 2026-11-03

## Context

Repo is `github.com/stompsu/runners-diary` (branch `main`). M1 acceptance requires CI building and testing both apps. API integration tests use Testcontainers (Docker is available on GitHub-hosted Ubuntu runners). Deploy (OIDC) is M7 and lives in a separate workflow.

## Decision

One workflow, `.github/workflows/ci.yml`, triggered on `pull_request` and `push` to `main`; `permissions: contents: read`; `concurrency` group per ref with `cancel-in-progress: true`. All jobs on `ubuntu-latest` (x64 — no SQL Server arm64 issue). Three parallel jobs:

1. **backend**
   - `actions/checkout`, `actions/setup-dotnet@v5` with `global-json-file: global.json`, NuGet cache.
   - `dotnet restore` → `dotnet build -c Release --no-restore` (warnings are errors) → `dotnet test -c Release --no-build --report-xunit-trx` (MTP; Testcontainers SQL + Azurite).
   - Upload TRX results as an artifact.
2. **frontend** (`working-directory: web`)
   - `actions/setup-node` with `node-version-file: web/.nvmrc`, npm cache.
   - `npm ci` → `npm run lint` → `npm run typecheck` → `npx prettier --check .` → `npm test -- --run` → `npm run build`.
   - `npx playwright install --with-deps chromium` → `npm run test:e2e` (smoke against `vite preview`, API mocked). Upload Playwright report on failure.
3. **api-contract** (drift guard)
   - setup-dotnet + setup-node; `dotnet build src/RunnersDiary.Api` (regenerates `openapi/runnersdiary-api.json`) → `npm --prefix web ci` → `npm --prefix web run generate:api` → `git diff --exit-code -- src/RunnersDiary.Api/openapi web/src/api/generated`.

Actions are pinned to major versions in M1 (`actions/checkout@v7`, `setup-dotnet@v6`, `setup-node@v7`, `upload-artifact@v7`); Dependabot (`.github/dependabot.yml`) weekly for `github-actions`, `nuget`, `npm`. Branch protection requiring the three jobs is a GitHub setting the **user** applies (not automated).

## Consequences

- PRs can't merge stale generated clients.
- Backend job needs Docker (Testcontainers) — fine on hosted runners; ~1–2 min extra for SQL container start.
- No AppHost end-to-end test in CI for M1 (would need Docker + npm + long startup); the `dotnet run` acceptance check is manual (see plan). Can be added later with `Aspire.Hosting.Testing`.

## Alternatives considered

- **Single job** — simpler but slower and mixes toolchains; failures less readable.
- **Azure DevOps** — repo is on GitHub; Actions is free for public repos / generous for private.
- **SHA-pinned actions** — stronger supply-chain posture; revisit in M7 when the deploy workflow gets OIDC credentials.

## References

- setup-dotnet `global-json-file`: https://github.com/actions/setup-dotnet
- MTP / `--report-xunit-trx` with .NET 10 SDK: https://learn.microsoft.com/dotnet/core/testing/migrating-vstest-microsoft-testing-platform
