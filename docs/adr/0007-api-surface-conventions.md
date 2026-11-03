# 0007 – API surface conventions (routes, health, errors, security seams)

**Status:** Accepted (M1)
**Date:** 2026-11-03

## Context

M1 creates the first endpoints. Decisions about route prefixes, health endpoints and error format are cheap now and expensive later; auth arrives in M2 and must be secure-by-default.

## Decision

1. **All application endpoints live under `/api`** (a `MapGroup("/api")`), so the Vite proxy and the production web tier (ADR 0004) forward one prefix without rewriting. No URL versioning yet; the OpenAPI document is named `v1`. Breaking changes are avoided rather than versioned while the only client ships with the API.
2. **Health**:
   - `GET /alive` — liveness, only the `self` check (ServiceDefaults tag `live`). Plain text `Healthy`.
   - `GET /health` — readiness, all checks (SQL `RunnersDiaryDbContext`, Blob). Plain text `Healthy|Degraded|Unhealthy`, HTTP 200/503.
   - Both mapped in **all environments** (ServiceDefaults template maps them only in Development — change that), excluded from OpenAPI, anonymous, and must not leak check details.
   - `GET /api/health` — typed JSON for the SPA shell and the generated client: `{ "status": "Healthy" | "Degraded" | "Unhealthy", "version": "1.0.0+<sha>" }`; 200 when Healthy/Degraded, 503 otherwise. Anonymous. This is the M1 "proof" endpoint of the OpenAPI → TS pipeline.
3. **Errors**: `AddProblemDetails()` + `UseExceptionHandler()` + `UseStatusCodePages()` → RFC 9457 `application/problem+json` for every error, including 404/405 and unhandled exceptions (no stack traces outside Development).
4. **Security seams (implemented in M2, documented now)**:
   - M2 adds authentication schemes (Entra External ID + guest JWT) and a **fallback authorization policy requiring an authenticated user**; endpoints opt *out* explicitly with `.AllowAnonymous()` — today only `/alive`, `/health`, `/api/health`, and (M2) guest-token issuance.
   - M2 adds an endpoint-inventory test that fails if any endpoint is anonymous and not on that allowlist.
   - The caller's user id comes from a single `ICurrentUser` abstraction (M2); feature handlers never read claims directly.
5. **Vertical slices**: `Features/<Area>/<UseCase>.cs` containing endpoint mapping + request/response records + validator + handler; each area exposes one `Map<Area>Endpoints(this IEndpointRouteBuilder)` extension called from `Program.cs`.

## Consequences

- One proxy rule (`/api`) everywhere; health probes for ACA are `/alive` (liveness) and `/health` (readiness/startup).
- Readiness reports Unhealthy while serverless SQL resumes — ACA startup probe timeouts must allow for that (M7).

## Alternatives considered

- Exposing check details in `/health` JSON — information disclosure for an anonymous endpoint; rejected.
- Only the ServiceDefaults endpoints (no `/api/health`) — then M1 has no typed endpoint to prove client generation end-to-end.

## References

- ASP.NET Core OpenAPI: https://learn.microsoft.com/aspnet/core/fundamentals/openapi/aspnetcore-openapi?view=aspnetcore-10.0
- Aspire service defaults (health endpoints): https://aspire.dev/fundamentals/service-defaults/
- RFC 9457: https://www.rfc-editor.org/rfc/rfc9457
