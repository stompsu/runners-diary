# 0009 – Authentication: Entra External ID + app-issued guest JWT behind one policy scheme

**Status:** Accepted (M2 plan approved by the user 2026-11-03; implemented in M2)
**Date:** 2026-11-03

## Context

The brief (§1.2, §1.3, §2 Auth) requires: every page and endpoint authenticated; real users via Microsoft Entra External ID (external tenant, MSAL in the SPA, JWT bearer on the API); guests via a short-lived JWT **issued by the API** (role `guest`), going through the same authorization pipeline; signing key in Key Vault; a combined policy accepting either. The external tenant does not exist yet (the user creates it), so M2 must work — and be tested — without it. ADR 0007 already fixed the seams: fallback policy requiring an authenticated user, explicit `.AllowAnonymous()` allowlist, endpoint-inventory test, single `ICurrentUser`.

## Decision

1. **Two JWT bearer schemes + one policy scheme** (`Microsoft.AspNetCore.Authentication.JwtBearer`, no `Microsoft.Identity.Web`):
   - `Entra` — `Authority = https://{tenantSubdomain}.ciamlogin.com/{tenantId}/v2.0` (OIDC metadata + signing keys), `Audience = {apiClientId}` (v2 access tokens; the API registration sets `api.requestedAccessTokenVersion = 2`), `ValidIssuers` from config (default: `https://{tenantId}.ciamlogin.com/{tenantId}/v2.0`; the user confirms the exact value from the tenant's metadata document). `OnTokenValidated` requires the `scp` claim to contain `access_as_user` and resolves/provisions the internal user (JIT, see 0010).
   - `Guest` — symmetric HS256, key ≥ 256 bit from `Auth:Guest:SigningKey` (+ optional `Auth:Guest:PreviousSigningKeys` for rotation, validation only), issuer `Auth:Guest:Issuer` (default `runnersdiary-guest`), audience `runnersdiary-api`, `ClockSkew = 1 min`, algorithms restricted to HS256. The key is the UTF-8 bytes of the configured string (≥ 32 bytes). `OnTokenValidated` checks that `sub` refers to an existing `Users` row with **`Kind = Guest` and `ExpiresAt` in the future** — a leaked guest key can therefore never impersonate a real user, and a token can never outlive its account even if cleanup lags. A positive result is memory-cached for **min(5 min, time left until `ExpiresAt`)**; guest cleanup **evicts** the entry of every guest it deletes, so a deleted guest is refused immediately.
   - `Smart` (default authenticate/challenge scheme, `AddPolicyScheme`) — `ForwardDefaultSelector` reads the bearer token **without validating it** (`JsonWebTokenHandler.ReadJsonWebToken`), forwards to `Guest` when `iss` equals the guest issuer, otherwise to `Entra` (or to `Guest` when Entra is not configured, which then fails cleanly). Any exception while reading the token falls through to that default, whose validator answers 401 (never 500). Only one handler validates each request; no double-validation noise in logs.
   - `MapInboundClaims = false` on both schemes (keep `oid`, `tid`, `scp`, `sub`, `role` as-is). `RoleClaimType = "role"`.
2. **Authorization**: `SetDefaultPolicy` and **`SetFallbackPolicy` = `RequireAuthenticatedUser()`** (on the `Smart` scheme). The `/api` route group additionally calls `.RequireAuthorization()` so every API endpoint carries explicit `IAuthorizeData` metadata (the existing inventory test keys on it). Allowlist (`.AllowAnonymous()`): `/alive`, `/health`, `/api/health`, `POST /api/guest-sessions` (plus `/openapi/v1.json`, mapped in Development only). No role-based policies in M2; `ICurrentUser.IsGuest` drives later guest limits (M4 uploads, M6 AI).
3. **Guest token**: `sub` = internal user id (GUID), `role = guest`, `jti`, `iat`, `nbf`, **`exp` = the guest account's `ExpiresAt`** (24 h after creation; no refresh token). Minted with `JsonWebTokenHandler` (transitive dependency of JwtBearer).
4. **Entra configuration rule** (`Auth:Entra:Enabled`, `TenantId`, `TenantSubdomain`, `Audience`):
   - `Enabled=false` → the `Entra` scheme is not registered, a warning is logged, guests work (AppHost without a tenant, tests).
   - **Any** of `TenantId`/`TenantSubdomain`/`Audience` set, or `Enabled=true` → all three are required (`TenantId` a GUID); a **partial config fails startup in every environment, Development included**. A half-configured login is never silently disabled.
   - Nothing set and `Enabled` unset → disabled with a warning in Development (and during build-time OpenAPI generation); **startup failure outside Development**.
   The default authorization policy also requires the server-added `rd_uid` claim, so a validated token without a resolved internal user is not authorized.
5. **Signing key management**: never in the repo. Local: Aspire secret parameter `guest-token-signing-key` (`GenerateParameterDefault { MinLength = 44, Special = false }`, verified on Aspire 13.6.4) generated and persisted to the AppHost user-secrets, passed to the API as `Auth__Guest__SigningKey`. Standalone API in Development without a key: an ephemeral random key is generated at startup (warning logged; guest tokens die on restart). Non-Development without a key: startup failure. Azure (M7): Key Vault secret referenced by a Container Apps secret, read via managed identity.
6. **Tests** mint their own tokens: the test factory sets `Auth:Entra:*` to a fake tenant and post-configures the `Entra` scheme with a static `OpenIdConnectConfiguration` (test RSA key, issuer `https://{fakeTenantId}.ciamlogin.com/{fakeTenantId}/v2.0`) so the real Entra code path (scope check, JIT provisioning) runs without network access. Guest tokens come from the real `POST /api/guest-sessions`; negative cases (expired, wrong key, wrong issuer, `alg: none`, guest key with a real user's id) are minted by the tests.

## Consequences

- One place (`Smart` selector) decides which validator runs; adding a third issuer later (e.g. a dev IdP) is a one-line change.
- The guest key is symmetric: anyone holding it can mint guest tokens — mitigated by the `Kind = Guest` DB check, short 24 h exp, Key Vault storage and rotation support.
- Guest tokens cannot be revoked individually before `exp`; deleting the guest row by cleanup revokes immediately (cache eviction); a delete by any other path (a future "end demo" endpoint) must evict too, otherwise it revokes within the 5-minute cache window. The cache is per replica.
- An unknown `/api/*` path answers 401 to anonymous callers (fallback policy) and 404 to authenticated ones.
- CIAM issuer quirks (ciamlogin vs login.microsoftonline issuer formats reported in community threads) are handled by configuration, not code; if they bite, switching the `Entra` scheme to `Microsoft.Identity.Web` is localized to `Auth/AuthenticationSetup.cs`.
- Every 401 is RFC 9457 problem details (via `UseStatusCodePages`) with a `WWW-Authenticate: Bearer` header.

## Alternatives considered

- **`Microsoft.Identity.Web`** for the Entra scheme — handles AAD issuer validation edge cases, but pulls MSAL and is designed around a single default scheme; we only validate tokens. Kept as fallback.
- **Default policy listing both schemes** (`AuthorizationPolicyBuilder("Entra", "Guest")`) — simpler, but both handlers run on every request (one always fails and logs), and an unconfigured Entra scheme would try to fetch metadata.
- **Asymmetric (RS256/ES256) guest keys** — useful only if a third party validated guest tokens; the API is both issuer and validator. Revisit if guest tokens ever leave the API boundary.
- **Short guest token (e.g. 1 h) + refresh endpoint** — more moving parts for a 24 h throwaway account; rejected by the user (2026-11-03) in favour of one 24 h token.
- **Cookie session for guests** — would require CSRF protection and a different pipeline from real users (brief wants the same pipeline).

## References (checked 2026-11-03)

- Multiple schemes / default policy / policy schemes: https://learn.microsoft.com/aspnet/core/security/authorization/limitingidentitybyscheme?view=aspnetcore-10.0 (page dated 2026-08-11)
- Protect an ASP.NET Core API (external tenant `Instance` = `https://<subdomain>.ciamlogin.com/`): https://learn.microsoft.com/entra/identity-platform/tutorial-web-api-dotnet-core-build-app
- `api.requestedAccessTokenVersion` (null ⇒ v1; set 2): https://learn.microsoft.com/entra/identity-platform/reference-microsoft-graph-app-manifest
- External tenant issuer/metadata reports (community, unverified officially): https://learn.microsoft.com/answers/questions/2181369/the-jwt-token-created-by-my-external-entra-id-tena , https://learn.microsoft.com/answers/questions/1464702/authorization-broken-well-known-openid-configurati
- Aspire parameters (user-secrets `Parameters:*`, `secret`, `persist`): https://aspire.dev/fundamentals/external-parameters/ , https://learn.microsoft.com/dotnet/api/aspire.hosting.parameterresourcebuilderextensions.addparameter
