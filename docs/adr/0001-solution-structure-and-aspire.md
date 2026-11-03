# 0001 – Solution structure and .NET Aspire usage

**Status:** Accepted (M1)
**Date:** 2026-11-03

## Context

Runners Diary needs a .NET 10 API, a React SPA, SQL Server and Blob Storage that a single developer can run locally with one command, and a structure that keeps domain logic (load metrics, GPX metrics) free of infrastructure. Aspire is now versioned 13.x (latest stable `13.6.4`, 2026-10-24); the Node integration was renamed to `Aspire.Hosting.JavaScript` in 13.0 and offers `AddViteApp`.

## Decision

1. **Solution file**: `RunnersDiary.slnx` (XML format; default for `dotnet new sln` in the .NET 10 SDK). SDK pinned in `global.json` (`10.0.400`, `rollForward: latestFeature`), which also selects Microsoft.Testing.Platform as the test runner (`"test": { "runner": "Microsoft.Testing.Platform" }`) — required by xUnit v3 MTP v2 packages on the .NET 10 SDK.
2. **Projects** (all `net10.0`):
   | Project | Kind | References |
   |---|---|---|
   | `RunnersDiary.AppHost` | `Sdk="Aspire.AppHost.Sdk/13.6.4"`, Exe | Api (project resource) |
   | `RunnersDiary.ServiceDefaults` | class lib (`IsAspireSharedProject`) | OpenTelemetry, health checks, resilience, service discovery |
   | `RunnersDiary.Api` | `Microsoft.NET.Sdk.Web`, Minimal APIs, `Features/<Area>/<UseCase>.cs` | ServiceDefaults, Infrastructure, Domain |
   | `RunnersDiary.Infrastructure` | class lib | Domain (EF Core, Blob, later Maps/AI) |
   | `RunnersDiary.Domain` | class lib, **no package references** | — |
   Dependency rule: `Api → Infrastructure → Domain`; `Domain` references nothing. Enforced by review in M1 and by an architecture test in M2 (when Domain has content).
3. **Central build settings**: `Directory.Build.props` (Nullable, `TreatWarningsAsErrors`, `AnalysisLevel=latest`, `AnalysisMode=Recommended`, `EnforceCodeStyleInBuild`, `ImplicitUsings`), `Directory.Packages.props` (central package management), `.editorconfig` for analyzer severity tuning (each suppression commented).
4. **Aspire is the local orchestrator only.** AppHost resources and names (these names are contracts — connection-string and env-var names derive from them):
   - `sql` → `AddSqlServer("sql")` with `WithDataVolume()` + `WithLifetime(ContainerLifetime.Persistent)`; database `runnersdiary` via `AddDatabase("runnersdiary")`.
   - `storage` → `AddAzureStorage("storage").RunAsEmulator(...)` (Azurite, data volume, persistent); blob service `blobs` via `AddBlobs("blobs")`. Blob *containers* are added in M4.
   - `api` → `AddProject<Projects.RunnersDiary_Api>("api")` `.WithReference(db).WithReference(blobs).WaitFor(db).WaitFor(blobs)` `.WithHttpHealthCheck("/health")`.
   - `web` → `AddViteApp("web", "../../web")` `.WithReference(api).WaitFor(api)`. Aspire sets `PORT` and injects `API_HTTP`/`API_HTTPS`.
5. **Client integrations in the API**: `Aspire.Microsoft.EntityFrameworkCore.SqlServer` (`AddSqlServerDbContext<RunnersDiaryDbContext>("runnersdiary")`) and `Aspire.Azure.Storage.Blobs` (blob service client for `"blobs"`). Both contribute health checks, so `/health` reflects SQL + Blob readiness.
6. The SQL password is the Aspire-generated `sql-password` parameter (stored in user-secrets by Aspire) — never committed.
7. A C# project-based AppHost (not the single-file `#:sdk` AppHost nor the TypeScript AppHost), because the solution already has several projects and `Projects.*` typed references are wanted.

## Consequences

- `dotnet run --project src/RunnersDiary.AppHost` is the one local entry point; Docker must be running.
- The Aspire CLI is optional (useful for `aspire add`, `aspire update`); the AppHost runs with the plain .NET SDK.
- M7 deployment can use Aspire's publish model / azd, but that is decided in M7 (and in ADR 0004 for the web tier).
- SQL Server has no native arm64 image; on Apple Silicon it runs under Rosetta (see plan risks).

## Alternatives considered

- **docker-compose** instead of Aspire — no service discovery, telemetry dashboard, or typed wiring; the brief mandates Aspire.
- **Single-file AppHost** — fewer files but less conventional with a multi-project `.slnx`.
- **`.sln`** — legacy format; `.slnx` is the .NET 10 default and supported by VS/Rider/`dotnet`.

## References

- Aspire SDK / AppHost project: https://aspire.dev/get-started/aspire-sdk/
- JavaScript apps in the AppHost (`AddViteApp`, `PORT`, auto npm install): https://aspire.dev/integrations/frameworks/javascript.md
- `Aspire.Hosting.JavaScript` package: https://www.nuget.org/packages/Aspire.Hosting.JavaScript/13.6.0
- `Aspire.Hosting.AppHost` versions: https://www.nuget.org/packages/Aspire.Hosting.AppHost
- SQL Server hosting integration: https://aspire.dev/integrations/databases/sql-server/sql-server-host/
- Azure Storage Blobs hosting (Azurite): https://aspire.dev/integrations/cloud/azure/azure-storage-blobs/azure-storage-blobs-host/
- `.slnx` default in .NET 10: https://learn.microsoft.com/en-us/dotnet/core/compatibility/sdk/10.0/dotnet-new-sln-slnx-default
- MTP in .NET 10 `dotnet test`: https://learn.microsoft.com/dotnet/core/testing/migrating-vstest-microsoft-testing-platform
