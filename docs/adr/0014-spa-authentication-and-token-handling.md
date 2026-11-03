# 0014 – SPA authentication: MSAL v5 redirect flow, guest token storage, one auth middleware

**Status:** Accepted (M3 plan approved 2026-11-03) · Amended 2026-11-03 (implementation, see the end)
**Date:** 2026-11-03

## Context

The SPA has two ways to sign in (brief §1.2–1.3, ADR 0009):
- **Real users**: Entra External ID (external tenant, `https://<subdomain>.ciamlogin.com/`), MSAL in the browser, v2 access token for scope `api://<api-client-id>/access_as_user`.
- **Guests**: `POST /api/guest-sessions` returns one HS256 token valid for 24 h, the same as the account (`exp = ExpiresAt`). There is no refresh. Losing the token means losing the demo diary.

Every route except `/signin` needs a signed-in caller. The API answers 401 problem+json for missing, expired or revoked tokens. Guests are also revoked by cleanup. The SPA may only call the API through `web/src/api/client.ts` (ADR 0002). The Entra tenant may not exist while M3 is built, so guests must work without it.

Facts checked on 2026-11-03:
- `@azure/msal-react` is on major **5** (5.0.2 GA 2026-01-17; 5.7.0 on 2026-08-28) and depends on `@azure/msal-browser` 5.x.
- msal-browser v5 adds a **redirect bridge**. Popup flows, silent iframe flows (`ssoSilent`, the iframe fallback of `acquireTokenSilent`) and the bridge-assisted redirect flow all expect `redirectUri` to point to a small same-origin page. That page calls `broadcastResponseToMainFrame()` from `@azure/msal-browser/redirect-bridge`. It must be bundled, served without `Cross-Origin-Opener-Policy`, sent with `Cache-Control: no-store`, and registered as the exact redirect URI. For Vite, the page is a second build entry, `redirect.html`.
- In v5, `navigateToLoginRequestUrl` moved from the config to the `handleRedirectPromise({...})` options. `storeAuthStateInCookie` was removed.
- The cache location defaults to `sessionStorage`. The external-tenant React tutorial uses authority `https://<subdomain>.ciamlogin.com/`. It sets `knownAuthorities` only for custom URL domains.

## Decision

1. **Libraries**: `@azure/msal-browser` 5.x only (5.27.0 at implementation), **without `@azure/msal-react`**. MSAL sits behind a small `EntraController` interface declared in `auth/session.ts` (`acquireToken(): Promise<string | null>`, `login()`, `logout()`), implemented in `auth/entra/msalClient.ts`, so MSAL types never reach the main chunk. MSAL is **lazy-loaded**: `AuthBootstrap` uses `React.lazy` to load `auth/entra/EntraAuthBoundary.tsx` only when the runtime config contains Entra settings (ADR 0015). The boundary renders nothing: it creates the app (`createStandardPublicClientApplication`), runs `handleRedirectPromise()`, restores the cached account and registers the controller with the session store. Guests and Entra-less environments never download MSAL.
2. **Interaction type: redirect**, not popup. Popups are unreliable on phones, where the app is used after a run, and popup blockers interfere. Flow: `loginRedirect({ scopes: [apiScope] })` (**no `state`**) → `redirectUri = <origin>/redirect.html` (the bridge page) → the bridge navigates back to the page that started sign-in (`/signin?returnTo=…`), where the boundary's `handleRedirectPromise()` completes it and the sign-in page navigates to the **re-sanitised** `returnTo`. A sessionStorage hint (`rd.entra`) marks a pending or active Entra session, so a reload shows a `restoring` state instead of bouncing to `/signin`. `postLogoutRedirectUri = <origin>/signin` (unverified until the tenant exists). `cacheLocation: "sessionStorage"` is the MSAL default and Microsoft's "more secure" recommendation. Authority: `https://<subdomain>.ciamlogin.com/`, without `knownAuthorities` (no custom domain).
3. **Redirect bridge**: `web/redirect.html` + `web/src/auth/redirectBridge.ts` call `broadcastResponseToMainFrame()`. The file is added as a second Vite build input (`build.rolldownOptions.input` in Vite 8) and is never wrapped in React or the router. Registered redirect URIs: `http://localhost:5173/redirect.html` now; `https://<web-fqdn>/redirect.html` in M7. The M7 web tier must serve it without COOP and with `no-store`.
4. **Guest token storage: `localStorage`** under key `rd.guestSession`, holding `{ accessToken, expiresAt, userId }`. `expiresAt` is stored as epoch ms **on the client clock**, corrected for clock skew: the remaining lifetime is measured on the server clock (`expiresAt − Date` response header) and added to the client's `Date.now()`. The entry is discarded on read when `expiresAt` has passed. It is removed on sign-out, on 401 and when the demo expires. Tabs stay in sync through the `storage` event: a removal in another tab signs this tab out; a different token replaces this tab's session and drops its caches.
   - *Why not memory*: a reload would lose the guest's diary, and a new guest would be created, which spends the per-IP quota of 5 per hour.
   - *Why not sessionStorage*: a second tab or a reopened browser would lose the diary for the same reason.
   - *XSS*: an XSS payload can read localStorage, sessionStorage and in-memory tokens, because it can call the API from the page. Where the token is stored does not change the impact. Mitigations happen at the source: React escaping only (`react/no-danger` lint), no HTML rendering of user text, strict CSP in M7 (`script-src 'self'`), and few dependencies. The token's worth is low: one throwaway 24 h demo account that can never be turned into a real user (ADR 0009 `Kind = Guest` check).
5. **One auth session store** (`web/src/auth/session.ts`): a tiny external store read with `useSyncExternalStore`, with no new dependency. State is `restoring | anonymous{reason} | guest{userId, expiresAt} | entra{name}`, with reason `expired | sessionEnded | signedOut`; the token itself is kept outside the React-visible state. A session **epoch** is bumped on every change. It exposes `getAccessToken()`:
   - guest: returns the stored token, or signs out with reason `expired` if the token has expired.
   - entra: `acquireTokenSilent({ scopes: [apiScope], account })`. **Any MSAL `AuthError` other than a network error** (`network_error`, `post_request_failed`) signs out with reason `sessionEnded`: `interaction_required`, iframe-renewal timeouts (Safari ITP), `no_tokens_found`, no account. Network errors propagate as a failed request and keep the session. The middleware never starts an interactive redirect; the user signs in again from `/signin` with one click (the IdP SSO cookie usually skips the prompt). A user cancel at the IdP (`user_cancelled`, `access_denied`, `AADB2C90091`) returns to sign-in without a reason banner.
6. **`client.ts` middleware** (`fetchClient.use`), the only place that touches tokens:
   - `onRequest` adds `Authorization: Bearer <token>` to every `/api` request except the anonymous ones (`POST /api/guest-sessions`, `GET /api/client-config`, `GET /api/health`).
   - `onResponse` handles 401 on an authenticated request **whose bearer belongs to the current session** (a late 401 from an older session is ignored): it signs out (guest → `expired`, entra → `sessionEnded`), and the route guard sends the user to `/signin?reason=…&returnTo=…`. It does not retry.
   - On every sign-out, `AuthBootstrap` cancels queries, **clears the mutation cache** and removes every query except the session-independent `client-config`. Post-mutation invalidation checks the session epoch, so a mutation that finishes after a sign-out never refreshes the next session's queries.
   - Error responses that are not problem+json (proxy 502/504 HTML during a cold start) become a synthetic problem object `{ status }`, so callers always get one error shape.
7. **Route guard**: a layout route `RequireAuth` wraps every route except `/signin`. While the session is `restoring` it shows a status message. When the session is anonymous it renders `<Navigate to="/signin?returnTo=<path>&reason=…">`; after a deliberate sign-out (`signedOut`) no `returnTo` is kept. `returnTo` is accepted only as a same-origin path: it must start with a single `/` and must not start with `//` or `/\`. That prevents open redirects. Route `loader`s are not used for auth, which keeps ADR 0003's "no API calls in loaders".
8. **Guest expiry UX**: the demo banner counts down from `expiresAt`. At expiry the session signs out with reason `expired`, and `/signin` shows "Your demo has ended". **End demo** clears the local session. No server call is made; cleanup removes the account after expiry. Switching from a guest session to **Sign in** first clears the guest session.

## Consequences

- One code path for both token kinds. Components never see tokens.
- A guest keeps the diary across reloads and tabs for 24 h.
- Entra users re-authenticate per browser session (sessionStorage). SPA refresh tokens last 24 h. After that, silent renewal needs the hidden iframe, and therefore third-party cookies on `ciamlogin.com`, which Safari blocks. Users may then see a one-click re-sign-in. This is accepted.
- The bridge adds one static page and one registered redirect URI, which differs from the M2 checklist's `http://localhost:5173/` (the user updates the registration).
- M7 CSP must allow `connect-src https://<subdomain>.ciamlogin.com` (token endpoint) and `frame-src https://<subdomain>.ciamlogin.com` (silent renewal).

## Alternatives considered

- **`@azure/msal-react` (`MsalProvider` + hooks)**: planned originally. Dropped during implementation: the session store already owns auth state, so the provider's interaction state would be a second source of truth, and it would bind MSAL into the React tree instead of one lazy, mockable controller.
- **MSAL `state` for the return path**: a second channel next to `/signin?returnTo=`, which would also need sanitising. Dropped.
- **Popup flow**: keeps SPA state, but popups are blocked or awkward on mobile, and v5 COOP handling relies on the bridge anyway.
- **Guest token in memory or sessionStorage**: see 4. They give no real XSS benefit and lose data on reload or across tabs.
- **HttpOnly cookie for guests through a BFF**: the strongest storage option, but it needs CSRF protection and a second auth pipeline. The brief wants guests on the same bearer pipeline as users (ADR 0009).
- **MSAL `localStorage` cache**: SSO across tabs, but Microsoft recommends sessionStorage, and the SPA refresh-token limit (24 h) caps the benefit.
- **React Router middleware or loaders as the guard**: possible in data mode, but a component guard is simpler to test and keeps loaders unused.

## References (checked 2026-11-03)

- Redirect bridge setup, Vite multi-page, no COOP (updated 2026-10-23): https://learn.microsoft.com/entra/msal/javascript/browser/redirect-bridge
- msal-browser v4 → v5 migration (bridge, `handleRedirectPromise` options, removed cache options): https://github.com/AzureAD/microsoft-authentication-library-for-js/blob/dev/lib/msal-browser/docs/v4-migration.md
- MSAL caching (sessionStorage default): https://learn.microsoft.com/entra/msal/javascript/browser/caching
- React SPA for external tenants (authority `https://<subdomain>.ciamlogin.com/`, `knownAuthorities` only for custom domains; updated 2026-06-15): https://learn.microsoft.com/entra/identity-platform/tutorial-single-page-app-react-prepare-app
- msal-react releases (5.0.2 major 2026-01-17, 5.7.0 2026-08-28; not used in the end): https://github.com/AzureAD/microsoft-authentication-library-for-js/releases/tag/msal-react-v5.7.0

## Amended 2026-11-03 (implementation)

The decision (redirect flow with the v5 bridge, sessionStorage MSAL cache, guest token in localStorage, one store, one middleware) is unchanged. Edited in place to match M3 as built ([plan §12](../plans/M3-calendar-ui.md#12-implementation-notes--deviations-2026-11-03)):
- §1: `@azure/msal-browser` directly behind `EntraController`, lazy `EntraAuthBoundary`; no `@azure/msal-react` / `MsalProvider`.
- §2: MSAL `state` dropped; return path via the re-sanitised `/signin?returnTo=`; `restoring` state via the `rd.entra` hint. The bridge spike succeeded, so no fallback `redirectUri = /` is needed.
- §4: skew-corrected guest expiry (client-clock epoch ms) and cross-tab `storage` sync.
- §5–6: every non-network `AuthError` from silent renewal → `sessionEnded`; no redirect from the fetch middleware; 401 only for the current session's bearer; mutation cache cleared and epoch guard on sign-out.
- §7: `restoring` state; no `returnTo` after a deliberate sign-out.
- Still to verify with the real tenant: sign-in end to end, `postLogoutRedirectUri`, the API's issuer validation (M2).
