# 0002 – OpenAPI document and generated TypeScript client

**Status:** Accepted (M1)
**Date:** 2026-11-03

## Context

The frontend must call the API only through a generated, type-safe client (CLAUDE.md). ASP.NET Core 10 has built-in OpenAPI generation (`Microsoft.AspNetCore.OpenApi`), emitting **OpenAPI 3.1** by default, and can generate the document **at build time** via `Microsoft.Extensions.ApiDescription.Server` (it launches the app entry point with a mock server, assembly name `GetDocument.Insider`). The frontend uses TanStack Query v5.

## Decision

**Server side**
- `builder.Services.AddOpenApi()` + `app.MapOpenApi()` (runtime JSON at `/openapi/v1.json`, Development only).
- Build-time generation enabled in `RunnersDiary.Api.csproj`:
  ```xml
  <OpenApiDocumentsDirectory>$(MSBuildProjectDirectory)/openapi</OpenApiDocumentsDirectory>
  <OpenApiGenerateDocumentsOptions>--file-name runnersdiary-api</OpenApiGenerateDocumentsOptions>
  ```
  → **`src/RunnersDiary.Api/openapi/runnersdiary-api.json`** (the single source of truth; committed).
- Code that needs real infrastructure (Aspire client integrations, DB) is skipped when `Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider"`, as the docs recommend.
- Every endpoint declares its response types (`TypedResults`, `.Produces<>()`/`ProducesProblem`) and an `operationId` via `.WithName(...)` so generated names are stable.

**Client side — `openapi-typescript` + `openapi-fetch` + `openapi-react-query`**
- `npm --prefix web run generate:api` = `openapi-typescript ../src/RunnersDiary.Api/openapi/runnersdiary-api.json -o src/api/generated/schema.d.ts`.
- `web/src/api/client.ts` (hand-written, tiny): `createFetchClient<paths>({ baseUrl: "/" })` + `createClient(fetchClient)` → `$api`. Auth middleware (bearer token from MSAL / guest token) is added here in M2/M3 via `fetchClient.use(...)`.
- Components use `$api.useQuery("get", "/api/health")` etc.; nothing imports `fetch` directly (lint rule `no-restricted-globals`/`no-restricted-imports` in M1).
- Both the JSON document and `schema.d.ts` are committed; CI fails on drift (ADR 0005).

## Consequences

- The generated artifact is a single `.d.ts` file — zero generated runtime code, small readable diffs, nothing to break at runtime.
- No generated per-operation hooks; call sites name the path and method. Acceptable and explicit.
- Zod schemas for forms remain hand-written (react-hook-form + zod), deliberately separate from wire types.
- Building the API regenerates the JSON; forgetting to run `generate:api` is caught by CI.

## Alternatives considered

| Option | Why not chosen |
|---|---|
| **Hey API `@hey-api/openapi-ts`** (+ TanStack Query plugin) | Excellent output (queryOptions, keys, mutations) but still pre-1.0 (0.9x) with frequent breaking config changes; larger generated runtime surface. Reasonable to revisit. |
| **Orval** | Mature, generates hooks + zod + MSW mocks; heavier generated code and config, Axios-centric history; zod-from-OpenAPI would couple form validation to wire shapes. |
| **Kiota** | Microsoft-supported but builder-style client, no TanStack Query integration, verbose for a React app. |
| **NSwag TS client** | Adds a .NET-side generator and older patterns; built-in OpenAPI makes it unnecessary. |

## References

- ASP.NET Core OpenAPI (3.1 default in .NET 10, build-time generation, `GetDocument.Insider`): https://learn.microsoft.com/aspnet/core/fundamentals/openapi/aspnetcore-openapi?view=aspnetcore-10.0
- openapi-typescript (OpenAPI 3.0/3.1, v7): https://openapi-ts.dev/introduction
- openapi-react-query: https://openapi-ts.dev/openapi-react-query/
- Hey API TanStack Query plugin: https://heyapi.dev/openapi-ts/plugins/tanstack-query
- Comparison overview (secondary): https://www.pkgpulse.com/guides/orval-vs-openapi-typescript-vs-kubb-openapi-client-2026
