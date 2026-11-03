# 0015 – SPA runtime configuration via anonymous `GET /api/client-config`

**Status:** Accepted (M3 plan approved 2026-11-03) · Amended 2026-11-03 (implementation, see the end)
**Date:** 2026-11-03

## Context

MSAL needs non-secret Entra values: the SPA client id, the authority (`https://<subdomain>.ciamlogin.com/`) and the API scope (`api://<api-client-id>/access_as_user`). These values must:
- work in Vite standalone (`npm run dev`), under Aspire, and in the Azure web container (M7);
- work without a rebuild per environment, and without committing tenant ids (they live in AppHost user-secrets, ADR 0009);
- express "Entra not configured" so that the SPA hides **Sign in** while guests keep working.

The API already holds the tenant id, subdomain and audience. It decides whether the `Entra` scheme is enabled (ADR 0009 §4). Showing a "Sign in" button that the API would then reject is the inconsistency to avoid. The web tier for M7 is still undecided between Aspire's YARP static site and nginx (ADR 0004).

## Decision

1. New anonymous endpoint **`GET /api/client-config`** (operationId `getClientConfig`), mapped in the health group: anonymous, no rate limiting, no database access.
   ```ts
   ClientConfigResponse = {
     entra: { clientId: string; authority: string /* https://<subdomain>.ciamlogin.com/ */; apiScope: string } | null
   }
   ```
   `Cache-Control: public, max-age=300`.
2. `entra` is non-null only when the API's `Entra` scheme is enabled **and** `Auth:Entra:SpaClientId` is set. `authority` is derived from `TenantSubdomain`. `apiScope` defaults to `api://{Audience}/{RequiredScope}` (`RequiredScope` defaults to `access_as_user`, the scope the API enforces) and can be overridden with `Auth:Entra:ApiScope`. If Entra is enabled but `SpaClientId` is missing, the API logs a startup warning and returns `entra: null`. This is not a startup failure, because sign-in is optional for guests.
3. AppHost passes `Entra:SpaClientId` from its user-secrets as `Auth__Entra__SpaClientId`, in the same block as the other Entra values. In Azure (M7) it becomes an azd/Bicep parameter (non-secret).
4. The SPA loads the config once at boot through the generated client: `$api.useQuery("get", "/api/client-config")` with `staleTime: Infinity`. Until the config loads, the sign-in page shows "Try as guest" right away and a placeholder for "Sign in". If the config fails to load, Sign in is hidden and guests still work.
5. Endpoint inventory/allowlist (ADR 0007/0009) gains `/api/client-config`. It is non-secret by construction: client ids, the authority and the scope are visible in every login URL anyway.

## Consequences

- One code path in every environment: the Vite proxy, Aspire and the M7 web proxy all forward `/api`. There is nothing to template into static files and no per-environment build.
- The SPA's "Sign in" availability always matches the API's Entra state.
- The sign-in page depends on one API round trip. In Azure that call can hit an API cold start: it scales from zero but needs **no SQL**, so there is no serverless resume. The guest button does not wait for it.
- One more anonymous endpoint, static and cheap. It does not count against the per-caller limiter, like `/api/health`.

## Alternatives considered

- **Vite build-time env (`VITE_ENTRA_*`)**: needs a rebuild per environment and invites committed `.env` files. Rejected.
- **`/config.json` served by the web tier**: needs a Vite dev middleware *and* a production implementation (nginx `envsubst` or YARP config). It couples M3 to the undecided ADR 0004. It can also disagree with the API's Entra state.
- **Inline `<script>` config injected into `index.html` at container start**: needs a CSP hash or nonce, and a web-tier-specific implementation as above.

## References

- ADR 0004 (web tier undecided), ADR 0009 §4 (Entra enablement rule), M2 plan §10 (non-secret ids, AppHost user-secrets).

## Amended 2026-11-03 (implementation)

Decision unchanged; details as built ([M3 plan §12](../plans/M3-calendar-ui.md#12-implementation-notes--deviations-2026-11-03) #18):
- §1: mapped in the anonymous `/api` group shared with health (`AllowAnonymous().DisableRateLimiting()`); the response is built once at startup.
- §2: the default scope is derived from `RequiredScope`, so the SPA always requests the scope the API checks. `SpaClientId` and `ApiScope` are on `EntraAuthOptions` (`Auth/AuthOptions.cs`). Missing `SpaClientId` with Entra enabled logs the startup warning `EntraSpaClientIdMissing`.
- §4: the config is the one query kept when the query cache is cleared on sign-out (it is session-independent). "Sign in" stays disabled until the lazy MSAL boundary has registered its controller.
