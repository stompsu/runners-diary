# 0019 – Frontend tests: one in-memory fake API for Vitest and Playwright, plus one full-stack journey

**Status:** Accepted (M3 plan approved 2026-11-03) · Amended 2026-11-03 (implementation, see the end)
**Date:** 2026-11-03

## Context

M3 turns the SPA from a shell into an app with about 10 endpoints, two auth kinds and many error states. The brief asks for Vitest + RTL and **Playwright E2E on the key flows**. M3's key flow is: try as guest → populated calendar → add → complete → rate → delete → month navigation via URL.

Current setup (M1): Vitest stubs `fetch` globally (`src/test/setup.ts`). Playwright runs against `vite preview` with `page.route` mocks. The CI `frontend` job has no .NET or Docker. The `backend` job already runs Testcontainers SQL.

Options:
- **MSW** v2 (current major; 2.15 as of mid-2026). It works in Node, which suits Vitest. In the browser it needs a service worker shipped in a special build, which complicates the `vite preview` E2E.
- **Real API in E2E**: highest fidelity, but the job needs .NET + SQL container + Azurite + API + preview proxy, which means minutes and flakiness on every PR.

## Decision

1. **One stateful, in-memory fake API** in `web/src/test/fakeApi/`. It is a pure function `handle(request: Request): Promise<Response>` built by `createFakeApi({ today, zone, entra, runs })`. It implements every M2 endpoint plus `GET /api/client-config`, with these behaviours:
   - per-token data isolation;
   - 401 without a token;
   - `version` bump → 409 on stale version;
   - a subset of validation 400s with camelCase keys;
   - run-limit 409 when seeded near the cap;
   - injectable faults (delay, 429/503 with `Retry-After`, 502 HTML).

   The fake is **typed against `schema.d.ts`**, so wire-shape drift fails `typecheck`. Seed data mirrors the 20-type catalogue and a small demo month.
   - **Vitest**: the existing global `fetch` stub delegates to the fake (`src/test/setup.ts`, `src/test/fake.ts`), and a test can override single responses. The render helper awaits `loadAllPanels()` once so lazy panel routes never race the async-util timeout (`asyncUtilTimeout: 4000`, `testTimeout` 15 s); this was the root cause of the only flake found.
   - **Playwright**: `page.route("**/api/**", route => fake.fulfillRoute(route))` per test (`e2e/support.ts`). No service worker, no special build, and the preview stays a production build.
2. **Vitest + RTL** cover the libraries and the components: date and format helpers, the problem mapper, `toWriteRequest`/`toFormValues`, the auth session and middleware, the route guard and `returnTo` sanitising, grid keyboard behaviour, form rules and server-error mapping, and the 409 flows.
3. **Playwright (mocked, every PR, existing `frontend` job)**: the key journeys in project `chromium`, plus project `phone-la` (`*.phone.spec.ts`) at a 360×640 touch viewport with `timezoneId: "America/Los_Angeles"` (ADR 0017). An `@axe-core/playwright` scan runs on the sign-in page, the calendar, the day panel and the run form, and fails on *serious* or *critical* violations.
4. **Full-stack journey (real API)**: a single spec, `web/e2e-fullstack/journey.spec.ts`, in its own directory with its own config (`playwright.fullstack.config.ts`, no `webServer`), run with `npm run test:e2e:fullstack` and `FULLSTACK_BASE_URL=…` (default `http://localhost:5173`, the Aspire-launched web). In CI it is a separate `e2e-fullstack` job on **push to `main` and `workflow_dispatch` only** (approved by the user), not on PRs: plain `docker run` SQL Server + Azurite (same image tags as the test fixtures), `dotnet run` the API (Development, `Auth:Entra:Enabled=false`, per-run random SQL password and guest key), `vite preview` with the `/api` proxy, Playwright. The AppHost is not used in CI (no DCP, dashboard or volumes to bootstrap). It catches drift between fake and real behaviour (validation, rules, guest seeding) without slowing PRs.
5. Entra sign-in is **not** automated against a real tenant. Component tests mock the MSAL module boundary (`auth/entra/msalClient.ts`). A Vitest test drives **real MSAL v5** `loginRedirect` against stubbed metadata and asserts the `/authorize` request (`authorizeRequest.test.ts`). One Playwright test with `client-config.entra` set asserts that "Sign in" navigates to `https://<subdomain>.ciamlogin.com/…/authorize` with the right `client_id`, `scope` and `redirect_uri=…/redirect.html`, never carrying a token; the navigation is intercepted. Real sign-in is a manual check once the tenant exists.

## Consequences

- No new mocking dependency. One fake serves two runners, and it is reusable for M4–M6 endpoints.
- The fake can drift *behaviourally* from the API. The full-stack journey on `main` and the shared types limit that, and reviewers check the fake whenever an API rule changes.
- PR CI time grows only by the mocked journeys, an estimated 1–2 minutes.

## Alternatives considered

- **MSW (Node + browser)**: mature and popular. It would need a service worker in a test build for Playwright, plus a dependency. `openapi-msw` would add typing, as another dependency. Revisit if the fake grows unwieldy.
- **Real API E2E on every PR**: best fidelity, but the slowest and flakiest path (SQL container start, seeding). Kept to `main`.
- **Playwright `page.route` with ad-hoc per-test JSON (status quo)**: does not scale to multi-step stateful journeys like add → complete → rate → delete.

## References (checked 2026-11-03)

- MSW v2 current line (2.15.0, mid-2026, secondary): https://summary.ecosyste.ms/projects/377922 ; MSW v2 with Vitest/Playwright overview: https://qaskills.sh/blog/msw-api-mocking-complete-guide
- Playwright `page.route`, `timezoneId` emulation (stable APIs; not re-fetched): https://playwright.dev/docs/network , https://playwright.dev/docs/emulation
- ⚠ `@axe-core/playwright` current version not verified; pin at install. *Installed: 4.14.0.*

## Amended 2026-11-03 (implementation)

Decision unchanged; edited in place to match M3 as built ([plan §12](../plans/M3-calendar-ui.md#12-implementation-notes--deviations-2026-11-03) #15–#17):
- §1: Playwright uses `fake.fulfillRoute(route)`; Vitest preloads lazy panels and sets `asyncUtilTimeout` 4000 (flake fix).
- §3: projects `chromium` and `phone-la` (360×640, was 390×844).
- §4: `web/e2e-fullstack/` + `playwright.fullstack.config.ts` + `FULLSTACK_BASE_URL` (was `e2e/fullstack/` + `E2E_FULLSTACK=1`); CI job approved and added (plain containers, not service containers or the AppHost). CI `concurrency` cancels only superseded PR runs, so every push to `main` runs the job.
- §5: added a real-MSAL authorize-request unit test.
