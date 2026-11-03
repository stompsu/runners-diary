# M3 – Calendar UI: plan

**Status:** Implemented — awaiting CI + approval (plan approved 2026-11-03; user accepted all defaults in §9; deviations in [§12](#12-implementation-notes--deviations-2026-11-03)) · **Date:** 2026-11-03 · **Author:** architect
**Builds on:** M1 (web shell, oxlint, `client.ts`, Vitest fetch stub, Playwright vs `vite preview`) and M2 as merged (API complete for the calendar; [M2 §11 deviations](M2-domain-runs-api.md#11-implementation-notes--deviations-2026-11-03)).
**Related ADRs:** [0002](../adr/0002-openapi-typescript-client.md) TS client · [0003](../adr/0003-frontend-stack-and-ui-kit.md) frontend stack · [0004](../adr/0004-web-hosting.md) web hosting · [0008](../adr/0008-run-date-and-time-storage.md) dates · [0009](../adr/0009-authentication-entra-and-guest-jwt.md) auth · [0011](../adr/0011-guest-accounts-lifecycle.md) guests · **new (proposed):** [0014](../adr/0014-spa-authentication-and-token-handling.md) SPA auth & tokens · [0015](../adr/0015-spa-runtime-configuration.md) runtime config · [0016](../adr/0016-calendar-grid-and-url-state.md) calendar grid & URL state · [0017](../adr/0017-client-dates-without-a-date-library.md) client dates · [0018](../adr/0018-run-forms-and-server-errors.md) forms & server errors · [0019](../adr/0019-frontend-test-strategy.md) frontend tests
**Contracts:** [`api-contract.md`](../api-contract.md) (+ `GET /api/client-config`, the only API change)

## 1. Goal & acceptance criteria

**Goal (brief §4 M3):** sign-in page with "Sign in" (Entra External ID via MSAL) and "Try as guest", demo-mode banner, month grid with bubbles, ISO week totals, month/year navigation with URL routing, day/month list panel, add/edit/delete flows, planned → completed, rating after completion, race details. Also: accessible (ARIA grid, keyboard), responsive (phone), i18n (en, typed keys, no literals).

**Out of scope:** location picker, geocoding and maps, GPX upload and track display (M4: the form keeps an existing `location` untouched); insights and charts (M5); AI (M6); deployment, CSP and web tier (M7); Polish translation; guest → user conversion; profile/settings page beyond the automatic time-zone sync.

| # | Criterion | How verified |
|---|---|---|
| AC1 | `/signin` is the only public page; every other route redirects anonymous visitors to `/signin?returnTo=<path>`. Only same-origin paths are accepted as `returnTo`. | Vitest (guard, sanitiser) · Playwright |
| AC2 | **Try as guest** → `POST /api/guest-sessions` → token stored (ADR 0014) → lands on `/diary/<me.today month>` showing the seeded runs. After 3 s pending, a "waking up the server" notice appears (no auto-retry of the POST). 429 and 503 show localized messages with the `Retry-After` wait. | Playwright (fake API with delay/faults) · full-stack journey |
| AC3 | **Sign in** is shown only when `GET /api/client-config` returns `entra`, and hidden (guest still works) when it is `null` or fails. With a tenant: redirect sign-in via the bridge page returns to `returnTo`; API calls carry an Entra token from `acquireTokenSilent`; sign-out returns to `/signin`. | Vitest + Playwright (authorize-URL assertion) · **manual with the real tenant** |
| AC4 | First authenticated load: for `kind = User` and `timeZoneIsDefault = true` the SPA sends the browser IANA zone in `PUT /api/me` (once; an invalid zone is ignored). It never does this for guests. | Vitest · manual (tenant) |
| AC5 | Demo banner for guests: brief §1.3 text and a countdown to `guestExpiresAt`. At expiry or on any 401 the session is cleared and `/signin` shows "Your demo has ended". **End demo** signs out locally. | Vitest (fake timers) · Playwright |
| AC6 | Month grid per ADR 0016: Monday first, 4–6 ISO weeks from `gridStart…gridEnd`, week column with completed totals (distance, time, elevation) and planned distance. Bubbles carry type colour and icon: planned dashed, completed filled, races with a trophy. Today is highlighted from `me.today`; days outside the month are dimmed. | Vitest · Playwright |
| AC7 | Navigation: prev/next, Today, month/year picker; URL `/diary/YYYY/MM` is bookmarkable and Back/Forward work; invalid or out-of-range months (outside 1950–2100) → Not found; `/` and `/diary` → `me.today`'s month. | Vitest · Playwright |
| AC8 | Day panel (click/Enter on a day) and month list (month header) list runs with **View details**, **Edit**, **Delete** (confirmation dialog). The panels are deep-linkable (`…/day/2026-11-14`, `…/list`). | Vitest · Playwright |
| AC9 | Add run from a day (date prefilled) or "Add run"; edit run. Fields per brief §1.1.3 except location/GPX; pace shown live; values sent in SI. Client rules mirror the API (ADR 0018); server 400s land on the matching fields (unmappable keys go to a form-level summary). | Vitest · Playwright · full-stack journey |
| AC10 | Planned → Completed via **Mark as completed**; rating (1–5 stars + comment) only for Completed runs; race section only for race types; moving back to Planned clears the rating. | Vitest · Playwright |
| AC11 | 409 concurrency: the form stays open with **Reload latest** / **Save mine anyway**; 409 run-limit, 404 (gone), 413 and 429 show localized messages; the PUT preserves untouched fields (`location`). | Vitest (`toWriteRequest` round trip) · Playwright |
| AC12 | Times per ADR 0008/0017: dates never shift through UTC (tests under `TZ=America/Los_Angeles` and `TZ=Pacific/Kiritimati`; Playwright project with `timezoneId` ≠ the user zone). New runs default to the profile zone; the zone field appears only when the user changes it; a run in another zone shows its zone label. | Vitest · Playwright |
| AC13 | Accessibility: ARIA grid + roving tabindex + keys (arrows, Home/End, PageUp/PageDown, Enter/Space), focus returns to the originating cell when a panel or dialog closes, labelled dialogs; axe scan shows no serious or critical violations on the sign-in page, calendar, day panel and form. | Vitest + Playwright + `@axe-core/playwright` |
| AC14 | Responsive: usable at 360×640 (no horizontal scroll; panels and dialogs as bottom sheets) and on desktop. | Playwright phone project · manual |
| AC15 | i18n: every user-facing string comes from typed keys in `web/src/i18n/en/*.json`; oxlint `react/jsx-no-literals` is on; every run-type code has a label and a colour token. | lint · Vitest |
| AC16 | `GET /api/client-config` per ADR 0015 (anonymous, no DB, cache header, inventory allowlist updated); OpenAPI and TS schema regenerated; all CI jobs green; `dotnet build` warning-free. | CI · Api.Tests |
| AC17 | Security: no tokens in URLs or logs; guest token only in `localStorage` key `rd.guestSession`; query cache cleared on sign-out/401; no `dangerouslySetInnerHTML`; results URL rendered only for `http(s)` with `rel="noopener noreferrer"`; no secrets or tenant ids committed. | reviewer · lint |

## 2. Files / projects by owning agent

### backend-dotnet (small; first commit)
```
src/RunnersDiary.Api/Features/ClientConfig/GetClientConfig.cs   # GET /api/client-config (anon, health group: no rate limiting, no DB)
src/RunnersDiary.Api/Auth/EntraAuthOptions.cs                    # + SpaClientId, ApiScope (optional); startup warning if Enabled && !SpaClientId
src/RunnersDiary.Api/openapi/runnersdiary-api.json               # regenerated
src/RunnersDiary.AppHost/AppHost.cs                              # + Auth__Entra__SpaClientId from Entra:SpaClientId (inside the existing Entra block)
tests/RunnersDiary.Api.Tests/ClientConfig/ClientConfigTests.cs   # entra null when disabled / when SpaClientId missing; derived authority + scope; Cache-Control; no auth needed
tests/RunnersDiary.Api.Tests/Security/EndpointAuthorizationTests.cs  # allowlist + /api/client-config
web/src/api/generated/schema.d.ts                                # regenerated (frontend-react commits it with step 2 if preferred)
```

### frontend-react
```
web/package.json, package-lock.json            # deps §7; script "test:tz" (date/calendar tests under two TZs)
web/.oxlintrc.json                             # + react/jsx-no-literals (allowed: punctuation/·/–), react/no-danger
web/vite.config.ts                             # build.rollupOptions.input: index.html + redirect.html
web/redirect.html, web/src/auth/redirectBridge.ts   # MSAL v5 bridge (ADR 0014 §3) — not wrapped in React
web/src/api/client.ts                          # auth middleware, non-problem error normalisation (ADR 0014 §6)
web/src/lib/api/problem.ts                     # problem → discriminated union (ADR 0018 §3)
web/src/lib/dates/plainDate.ts                 # ADR 0017
web/src/lib/format/{units,duration}.ts         # km, min/km pace, h:mm:ss, elevation
web/src/app/{providers,queryClient,routes}.tsx # retry policy (network/502/503/504 only, queries only), new route tree
web/src/auth/session.ts                        # session store (anonymous | guest | entra), getAccessToken, signOut(reason)
web/src/auth/guestSession.ts                   # localStorage rd.guestSession read/write/expiry
web/src/auth/entra/{EntraAuthBoundary.tsx,msalClient.ts}  # lazy MSAL (PublicClientApplication + MsalProvider), loginRedirect/logout
web/src/auth/{RequireAuth.tsx,returnTo.ts,useClientConfig.ts,useMe.ts,useTimeZoneSync.ts}
web/src/features/auth/{SignInPage.tsx,SlowStartNotice.tsx}
web/src/components/layout/{AppShell.tsx,DemoBanner.tsx,UserMenu.tsx}   # banner + sign-out/end demo
web/src/components/ui/*                        # shadcn add: dialog sheet alert-dialog field input textarea label select
                                               #   radio-group toggle-group popover tooltip skeleton alert separator sonner
                                               #   dropdown-menu collapsible
web/src/components/ResponsiveDialog.tsx        # Dialog ≥ sm, bottom Sheet < sm
web/src/features/calendar/{DiaryMonthPage.tsx,MonthHeader.tsx,MonthYearPicker.tsx,MonthGrid.tsx,DayCell.tsx,
                           RunBubble.tsx,WeekTotalsCell.tsx,useGridKeyboard.ts,calendarQueries.ts}
web/src/features/runs/{DayPanel.tsx,MonthListPanel.tsx,RunList.tsx,RunDetailsDialog.tsx,DeleteRunDialog.tsx,
                       RunFormDialog.tsx,RunForm.tsx,RaceFields.tsx,RatingField.tsx,RateRunDialog.tsx,TimeZoneField.tsx,
                       runFormSchema.ts,runFormMapping.ts,serverErrorMapping.ts,runMutations.ts,runTypes.ts}
web/src/features/diary/*                       # DiaryPage placeholder replaced; diaryMonth.ts year range 1950–2100, uses me.today
web/src/i18n/en/{common,auth,calendar,runs,runTypes,errors}.json; i18n/index.ts, i18next.d.ts   # namespaces, typed
web/src/index.css                              # run-type colour tokens (--run-race, --run-easy, …), planned/completed styles
```

### qa-tester
```
web/src/test/fakeApi/{index.ts,handlers.ts,seed.ts,faults.ts}   # ADR 0019 (pairs with frontend-react in step 2)
web/src/test/setup.ts, render.tsx              # fetch stub → fake; renderRoute with a signed-in guest/entra session
web/src/**/*.test.ts(x)                        # §6
web/e2e/{fixtures.ts,guest-journey.spec.ts,navigation.spec.ts,errors.spec.ts,auth.spec.ts,a11y.spec.ts}
web/e2e/fullstack/guest-journey.spec.ts        # real API, opt-in (E2E_FULLSTACK=1)
web/playwright.config.ts                       # projects: chromium (default), phone (390×844, timezoneId America/Los_Angeles);
                                               #   fullstack project only when E2E_FULLSTACK=1
```

### azure-infra (CI only)
```
.github/workflows/ci.yml   # frontend job: + `npm run test:tz`; optional new job `e2e-fullstack` (push to main + workflow_dispatch;
                           #   SQL + Azurite services, dotnet run API in Development with Entra disabled, vite preview + proxy) — if approved (§9 Q1)
```

### architect
`docs/` updates after implementation (deviations, ADR status), `api-contract.md` already lists `client-config` as planned.

## 3. Contracts

### 3.1 API change (only one)
`GET /api/client-config` → `200 { entra: { clientId, authority, apiScope } | null }`, anonymous, `Cache-Control: public, max-age=300` (ADR 0015). Config: `Auth:Entra:SpaClientId` (AppHost user-secrets `Entra:SpaClientId` → env), optional `Auth:Entra:ApiScope` (default `api://{Audience}/access_as_user`). **Nothing else in the M2 API changes.** Calendar, runs, me and run-types are used as they are.

### 3.2 Client routes
| Path | Access | Component |
|---|---|---|
| `/signin?returnTo&reason=expired\|sessionEnded\|signedOut` | public | `SignInPage` |
| `/redirect.html` | public, static (not a router route) | MSAL bridge |
| `/`, `/diary` | auth | redirect → `/diary/<me.today>` |
| `/diary/:year/:month` (+ `/day/:date`, `/list`, `/runs/new?date=`, `/runs/:runId`, `/runs/:runId/edit[?complete=1]`) | auth | ADR 0016 |
| `/insights` | auth | placeholder (M5) |
| `*` | auth | NotFound |

### 3.3 Data flow (TanStack Query via `$api`)
| Query/mutation | Used by | Cache |
|---|---|---|
| `getClientConfig` | sign-in page, boot | `staleTime: Infinity` |
| `createGuestSession` (mutation) | Try as guest | no retry |
| `getMe` | guard, banner, today, zone sync | `staleTime` 5 min |
| `updateMe` (mutation) | zone sync (users, once) | invalidates `getMe` |
| `listRunTypes` | grid, form | `staleTime: Infinity` |
| `getCalendarMonth(y, m)` | grid, day/month panels | 30 s; prefetch ±1 month |
| `getRun(id)` | details, edit, rate | 0 (always fresh `version`) |
| `createRun` / `updateRun` / `deleteRun` | form, rate, delete | invalidate all `getCalendarMonth`, `listRuns`, `getRun(id)` |

Retry: queries only, ≤ 3 times with backoff, only for network errors / 502 / 503 / 504; never for 4xx or mutations. A pending `getMe` or guest creation shows `SlowStartNotice` after 3 s.

### 3.4 Auth wiring (ADR 0014)
`client.ts`: `onRequest` → bearer from `session.getAccessToken()` (skips `/api/guest-sessions`, `/api/client-config`, `/api/health`); `onResponse` 401 → `session.signOut(kind === "guest" ? "expired" : "sessionEnded")` + `queryClient.clear()`. MSAL config: `{ auth: { clientId, authority, redirectUri: origin + "/redirect.html", postLogoutRedirectUri: origin + "/signin" }, cache: { cacheLocation: "sessionStorage" } }`; `loginRedirect({ scopes: [apiScope], state: returnTo })`. Guest storage: `localStorage["rd.guestSession"] = { accessToken, expiresAt, userId }`.

### 3.5 Form ↔ wire mapping (ADR 0018)
| Form field | Wire field | Conversion |
|---|---|---|
| `distanceKm` (string, `,` or `.`) | `distanceMeters` | × 1000, rounded to 1 m |
| `duration` `h:mm:ss` / `mm:ss` | `durationSeconds` | parse |
| `elevationGain` / `elevationLoss` (m) | `elevationGainMeters` / `elevationLossMeters` | as is |
| `race.targetTime` / `race.resultTime` | `race.targetTimeSeconds` / `race.resultTimeSeconds` | parse |
| `race.officialDistanceKm` | `race.officialDistanceMeters` | × 1000 |
| `startTime` `""` | `startTime: null` | empty → null |
| `timeZoneId` (hidden unless changed) | `timeZoneId` | POST: omit → profile zone; PUT: omit → keep run's zone |
| — (not rendered in M3) | `location` | copied from the loaded `RunDto` |
| — | `version` | from the loaded `RunDto` (PUT) |

Pace shown = `durationSeconds / (distanceMeters / 1000)` → `m:ss /km` (display only).

### 3.6 Run-type presentation
`runTypes.<code>` i18n label for each of the 20 codes in `data-model.md`. Colour families (CSS variables): `race` (all 6 race types, plus trophy icon), `easy` (easy, recovery), `long`, `quality` (tempo, intervals, fartlek, hillRepeats, progression, racePace, stridesDrills), `trail` (trailTraining), `treadmill`, `cross` (crossTraining), `rest` (rest). Unknown code → neutral token + `runTypes.unknown` label.

## 4. Implementation steps (each = one reviewable commit)

1. **backend-dotnet – client config.** Endpoint, options, AppHost env, tests, allowlist, regenerate OpenAPI + TS schema.
2. **frontend-react (+ qa-tester) – foundations.** Deps (§7), i18n namespaces + `jsx-no-literals`, `plainDate`/format libs, problem mapper, fake API + test setup rewired, query client retry policy. Tests for libs.
3. **frontend-react – auth.** Session store, guest storage, `client.ts` middleware, `RequireAuth` + `returnTo`, sign-in page, slow-start notice, demo banner + countdown, user menu (sign out / end demo), lazy MSAL boundary + `redirect.html` bridge + Vite multi-entry, zone sync. Update the M1 smoke specs for the guard. **Spike first, before building on it:** check MSAL v5 `loginRedirect` with the bridge page as `redirectUri` (does the bridge hand the response back to the app page as documented?). Fallback: pass `redirectUri: origin + "/"` on `loginRedirect` only, and keep the bridge for silent/iframe flows (needs both URIs registered).
4. **frontend-react – calendar.** `DiaryMonthPage`, header + picker + Today, `MonthGrid` with keyboard, bubbles, week totals, colour tokens, prefetch, phone layout.
5. **frontend-react – panels & details.** Day panel, month list, run details, delete confirmation, `ResponsiveDialog`, focus return.
6. **frontend-react – run form.** Schema, mapping, add/edit, race fields, rating, quick actions (Mark as completed, Rate), zone field, server-error mapping, 409/404/413/429 UX.
7. **qa-tester – E2E.** Mocked journeys + phone/TZ project + axe; full-stack spec; **azure-infra** adds `test:tz` to CI and (if approved) the `e2e-fullstack` job.
8. **User + main agent – manual Entra check** (when the tenant exists, §8): real sign-in, token accepted by the API (confirms the issuer string from M2), zone sync, sign-out. **reviewer** – token handling, open redirect, XSS surface, a11y, ADR consistency. **architect** – deviations, ADR status. Main agent updates CLAUDE.md and commits "M3: calendar UI"; stop for approval.

## 5. Risks

| Risk | Impact | Mitigation |
|---|---|---|
| MSAL v5 redirect bridge behaviour for `loginRedirect` not exercised yet; COOP subtleties | Sign-in loop or `redirect_uri_mismatch` | Spike at the start of step 3; documented fallback (§4 step 3); register both `/redirect.html` and `/`. |
| Entra tenant not ready during M3 | AC3/AC4 unverified with real tokens; CIAM issuer string (M2 risk) still open | Everything else is tested with stubs; AC3/AC4 manual check gates approval or is explicitly deferred by the user. |
| Safari blocks third-party cookies → silent renewal fails after the 24 h SPA refresh token | Users re-sign-in daily | Accepted (ADR 0014); sign-in is one click with the IdP SSO cookie. |
| Full-replace `PUT` drops fields the M3 form doesn't render | Silent data loss (location, later GPX-derived fields) | `toWriteRequest(values, base)` copies them; round-trip unit test against a full `RunDto` fixture; reviewer check. |
| Fake API drifts from the real API's behaviour | Green tests, broken app | Typed against `schema.d.ts`; full-stack journey on `main`; fake updated in the same PR as API rule changes. |
| Date shifts via `new Date(string)` creeping in | Runs on the wrong day for users west of UTC | ADR 0017 helpers; multi-TZ test runs; reviewer rule. |
| Cold start: ACA API + serverless SQL resume (M7) on first guest creation | 20–60 s wait, user abandons or double-clicks (burns the 5/h IP quota) | Slow-start notice after 3 s; button disabled while pending; no auto-retry of the POST; 429 message with wait. |
| `react/jsx-no-literals` noise (option names differ between oxlint versions) | Lint friction | Configure against the installed oxlint version; allow-list punctuation; `src/components/ui/**` exempt. |
| shadcn `Field` / sonner / radix versions vs React 19.3 peer deps; msal-react 5 peer range vs React 19.3 | Install conflicts | Check peers at install; pin; report deviations. |
| Keyboard grid + panel focus management complexity | a11y regressions | Dedicated RTL tests per key; axe in E2E; APG pattern. |
| Run-type colours fail contrast or colour-blind distinction | a11y | Shape cues (dashed/filled/trophy) + text; contrast checked in review. |
| Bundle growth (MSAL ≈ large) | Slower first load on phones | MSAL lazy chunk only when Entra configured and on the sign-in path; check `vite build` sizes in the report. |

## 6. Test plan outline (qa-tester)

**Vitest + RTL (jsdom; fetch → fake API)**
- `plainDate`: parse/format round trip; `addMonths` clamps (Jan 31 + 1 → Feb 28/29); ISO weekday for known dates; leap years; labels unchanged under `TZ=America/Los_Angeles` and `Pacific/Kiritimati` (`test:tz`).
- Format: km (2 decimals), pace `m:ss /km` (incl. rounding to 60 s → next minute), `h:mm:ss` parse (accepts `mm:ss`, rejects `1:75`), elevation.
- `problem.ts`: each problem `type`/status → union kind; `Retry-After` seconds and HTTP-date; non-JSON 502 → `unavailable`; network `TypeError` → `unavailable`.
- `runFormMapping`: SI conversions; empty → null; **round trip `toWriteRequest(toFormValues(dto), dto)` equals the writable subset of `dto`** (location preserved, no read-only members); POST omits `timeZoneId` unless changed.
- `runFormSchema`: one case per API rule (rating only Completed; race only for race types; rest-day no metrics; HR order; lengths; ranges); switching to Planned clears the rating.
- `serverErrorMapping`: `distanceMeters` → `distanceKm` error; `race.resultTimeSeconds` → `race.resultTime`; `body`/unknown → summary.
- Auth: guest session persisted/restored/expired-discarded; middleware adds the bearer (not on anon paths); 401 → signed out with the right reason + cache cleared; `returnTo` sanitiser rejects `//evil`, `/\evil`, `https://…`, accepts `/diary/2026/11?x`; guard redirects anonymous users; Sign in hidden when `entra: null` / config error; zone sync sends `PUT /api/me` only for `User` + default zone, once; demo banner countdown and expiry sign-out (fake timers).
- Calendar: renders the weeks/days of the fake month; week totals text; planned vs completed classes; race icon; today `aria-current`; keyboard: arrows/Home/End/PageUp/PageDown (URL change + focus), Enter opens the day panel, Escape closes it and focus returns to the cell; invalid URL → NotFound; Today button.
- Panels/forms: day panel lists the day's runs; delete confirm → DELETE → grid updates; add from a day prefills the date; Mark as completed presets status; rating visible only when Completed; 409 concurrency both actions; 409 run-limit message; 404 on save closes + toast.
- i18n: every `runTypes.<code>` for the 20 seeded codes exists; every code has a colour family.

**Playwright (mocked fake API, `vite preview`, Chromium desktop + phone/TZ project)**
- `guest-journey`: `/` → `/signin` → Try as guest → calendar of the fake "today" month with bubbles → click a day → add a planned run (date prefilled) → bubble dashed → open → Mark as completed (distance, duration) → bubble filled, week totals updated → Rate 4★ + comment → details show the rating → delete with confirmation → bubble gone.
- `navigation`: deep link `/diary/2026/02`, next/prev/Today, picker to another year, Back/Forward, `/diary/2026/13` Not found, deep link to `…/runs/<id>/edit`.
- `errors`: slow guest creation (5 s delay) shows the waking-up notice; 429 and 503 on guest creation; 401 mid-session → `/signin?reason=expired` with message; 409 concurrency on save.
- `auth`: Sign in hidden with `entra: null`; with `entra` set, clicking Sign in navigates to `https://<sub>.ciamlogin.com/…authorize?…client_id=…&redirect_uri=…%2Fredirect.html&scope=…access_as_user…` (intercepted + aborted).
- `a11y`: axe (serious/critical = fail) on sign-in, calendar, day panel, run form; keyboard-only add-run path.
- Phone project (390×844, `timezoneId` America/Los_Angeles, fake today in Europe/Warsaw): no horizontal scroll, panels as sheets, dates match the fake data exactly.

**Full-stack (opt-in / `main` only):** the same guest journey against the real API (Development, Entra disabled): seeded month ≥ 4 runs, add → complete → rate → delete round trip, URL navigation.

**Api.Tests:** `ClientConfigTests` (§2) + allowlist update; the 401 sweep still passes (client-config excluded as anonymous).

**Manual (tenant needed):** sign-in via email OTP, return to `returnTo`, `GET /api/me` 200 with `kind = User`, zone updated from the browser, sign-out, sign-in from the guest banner. Also phone check on a real device (Safari) via the LAN Vite URL.

## 7. Dependencies

### npm (`web/`; pin exact current versions at install, check React 19.3 peer ranges)
| Package | Dep | Why |
|---|---|---|
| `@azure/msal-browser` (5.x) | prod | Entra External ID sign-in/token acquisition (brief; ADR 0014); the bridge entry `redirect-bridge`. |
| `@azure/msal-react` (5.x) | prod | `MsalProvider`/hooks: redirect handling and interaction state in React (brief names MSAL React). |
| `react-hook-form` (7.x) | prod | Form state with uncontrolled inputs; named in the brief and ADR 0003. |
| `zod` (4.x) | prod | Client validation schema mirroring API rules (ADR 0018). |
| `@hookform/resolvers` (5.x) | prod | `zodResolver` glue between RHF and zod 4. |
| `sonner` (via `npx shadcn add sonner`) | prod | Accessible toasts for save/delete/404 feedback; shadcn's standard toast. |
| shadcn components (§2) | — (copied code; radix-ui already installed) | Dialog/Sheet/AlertDialog/Field/Select/Popover…; may pull `@radix-ui`-based peers through `radix-ui`. |
| `@axe-core/playwright` | dev | Automated a11y scan in E2E (AC13). |

**Rejected:** MSW and openapi-msw (ADR 0019: one fake serves both runners), date-fns, dayjs, Luxon and Temporal polyfills (ADR 0017), TanStack Router (ADR 0003; RR v8 data mode stays), react-day-picker / shadcn Calendar and FullCalendar (ADR 0016), zustand (the session store is ~50 lines with `useSyncExternalStore`).
**NuGet:** none.

## 8. What the user must do

1. **Finish the Entra tenant (M2 §10)** and, in the SPA registration (*Authentication → Single-page application*), register **`http://localhost:5173/redirect.html`**. The bridge spike succeeded (§12), so this is the only local redirect URI needed; the fallback `http://localhost:5173/` is not required. Then `dotnet user-secrets set "Entra:SpaClientId" "<SPA client id>" --project src/RunnersDiary.AppHost` (already in the M2 checklist) and send the exact `issuer` string from the metadata document (M2 §10 step 8). Needed for **AC3, AC4** and the manual check; everything else proceeds without it.
2. Decide the open questions in §9.
3. Optional: test on a phone (Safari) over the LAN during step 8.

## 9. Open questions — answered by the user (2026-11-03: plan defaults accepted)

1. **Full-stack E2E in CI**: yes — `e2e-fullstack` job on push to `main` + manual trigger (not on PRs).
2. **Guest token**: `localStorage` (`rd.guestSession`).
3. **Entra session**: per tab (MSAL `sessionStorage`).
4. **Tenant not ready by the end of M3**: M3 may be approved with AC3/AC4 deferred to a short follow-up check.
5. **Week column on phones**: distance only (time/elevation in the month list).
6. **Units**: UI stays metric in M3; imperial display deferred.

## 10. Assumptions

1. The M2 API is used unchanged except for `GET /api/client-config`; calendar data drives the day and month panels (no `listRuns` call needed in M3, though it stays available).
2. Location stays read-only/hidden in M3 and is preserved on edit; M4 adds the picker.
3. Guests cannot change their profile zone in M3 (Europe/Warsaw); no profile/settings page in M3.
4. MSAL is used only in redirect mode with the sessionStorage cache; no custom domain, so no `knownAuthorities`.
5. "Sign in" from a guest session discards the guest session (no conversion).
6. The full-stack journey runs the API in Development (guest limits 100/h), so CI is not rate-limited.
7. Brief §1.1.3 "start time" is optional (ADR 0008); planned runs commonly omit it.
8. Weekly totals shown are the API's (running types only, ADR M2 §9 Q5).
9. React Router v8 data mode, TanStack Query and openapi-react-query stay as set up in M1; no route loaders for API calls.

## 11. Docs checked (2026-11-03)

- MSAL v5 redirect bridge (updated 2026-10-23): https://learn.microsoft.com/entra/msal/javascript/browser/redirect-bridge
- msal-browser v4 → v5 migration: https://github.com/AzureAD/microsoft-authentication-library-for-js/blob/dev/lib/msal-browser/docs/v4-migration.md
- MSAL caching: https://learn.microsoft.com/entra/msal/javascript/browser/caching
- React SPA in an external tenant (authority format; updated 2026-06-15): https://learn.microsoft.com/entra/identity-platform/tutorial-single-page-app-react-prepare-app
- msal-react releases (v5.0.2 major 2026-01-17 … v5.7.0 2026-08-28): https://github.com/AzureAD/microsoft-authentication-library-for-js/releases/tag/msal-react-v5.7.0
- shadcn Field: https://ui.shadcn.com/docs/components/field · RHF guide: https://ui.shadcn.com/docs/forms/react-hook-form
- `@hookform/resolvers` v5.4.2 (zod 4.3 fix): https://github.com/react-hook-form/resolvers/releases/tag/v5.4.2
- Node 26 Temporal: https://www.helpnetsecurity.com/2026/05/07/node-js-26-released/ · Safari gap (secondary): https://lilting.ch/en/articles/nodejs-26-current-temporal-undici
- oxlint `react/jsx-no-literals`: https://oxc.rs/docs/guide/usage/linter/rules/react/jsx-no-literals
- MSW v2 line (secondary): https://summary.ecosyste.ms/projects/377922

**Not verified / secondary only:**
- Exact current versions of RHF, zod, resolvers, msal-browser and `@axe-core/playwright`, and msal-react's React 19.3 peer range: pin at install.
- Safari Temporal status after May 2026.
- MSAL v5 `loginRedirect` + bridge end-to-end behaviour: spike in step 3.
- oxlint `jsx-no-literals` option names in 1.89.
- React Router v8 middleware: not used, so not checked.
- Playwright and WAI-ARIA APG pages: not re-fetched (stable).

*(Versions, the bridge behaviour and the oxlint options were settled by the implementation — see §12.)*

## 12. Implementation notes & deviations (2026-11-03)

| # | Deviation from this plan | Reason |
|---|---|---|
| 1 | **`@azure/msal-react` not used** (installed, then removed). `@azure/msal-browser` **5.27.0** is used directly (`createStandardPublicClientApplication`, `handleRedirectPromise`) behind a small `EntraController` interface in `auth/session.ts` (`acquireToken`, `login`, `logout`); `auth/entra/msalClient.ts` implements it. `AuthBootstrap` lazy-loads `auth/entra/EntraAuthBoundary.tsx` (renders nothing) only when `client-config.entra` is set. | The session store already owns state and tokens; `MsalProvider`/hooks would duplicate it and pull React-bound MSAL into the tree. The interface keeps MSAL types out of the main chunk and makes the boundary mockable. |
| 2 | Package versions: `@hookform/resolvers` **7.0.1** (not 5.x), zod **4.6.7**, react-hook-form **7.91.0**, sonner **2.0.8**, `@axe-core/playwright` **4.14.0** (dev). | Current versions at install; resolvers 7 supports zod 4 and RHF 7.91. |
| 3 | shadcn components added: popover, skeleton, dialog, alert-dialog, input, textarea, label, field, separator, native-select, sonner (without `next-themes`). **Not added**: sheet, select, radio-group, toggle-group, tooltip, alert, dropdown-menu, collapsible. `components/ResponsiveDialog.tsx` is one Radix dialog styled as a bottom sheet below `sm` (`hooks/useIsPhone.ts`). Rating = native radio group; selects = `native-select`. | Fewer dependencies; native controls are accessible on phones by default; a single dialog component keeps focus handling in one place. |
| 4 | **Redirect-bridge spike confirmed** (MSAL v5 `loginRedirect` with `redirectUri = /redirect.html`; the bridge caches the response and navigates back to the page that started sign-in, where `handleRedirectPromise` completes it). **No fallback**: `http://localhost:5173/` need not be registered (§8 step 1 is reduced to `/redirect.html`). Checked by `auth/entra/authorizeRequest.test.ts` (real MSAL) and `e2e/auth.spec.ts` (intercepted `/authorize`). | — |
| 5 | MSAL `state` **dropped**: `loginRedirect({ scopes: [apiScope] })` only. The return path survives as `/signin?returnTo=…`, which the sign-in page re-sanitises after the redirect. A sessionStorage hint `rd.entra` puts the session into a `restoring` state on reload so the guard waits instead of bouncing. | One sanitiser for every return path; `state` would be a second, unsanitised channel. |
| 6 | `postLogoutRedirectUri = <origin>/signin` is **unverified** until the tenant exists. | Needs a real IdP logout. |
| 7 | Silent-token errors: any MSAL `AuthError` **other than network errors** (`network_error`, `post_request_failed`) → local sign-out with reason `sessionEnded` (covers `interaction_required`, iframe timeouts under Safari ITP, `no_tokens_found`). Network errors propagate as a failed request. **Interaction-required never redirects from the fetch middleware**; the user signs in again from `/signin`. A user cancel at the IdP (`user_cancelled`, `access_denied`, `AADB2C90091`) returns to sign-in silently (no reason banner). | A redirect fired from inside a fetch would discard in-flight work and can loop; a stuck "half signed-in" app is worse than one click. |
| 8 | Calendar grid is a **`<table role="grid">`** (oxlint `jsx-a11y/prefer-tag-over-role`), **week column first** as the row header, grid labelled with **`aria-label`** ("Calendar for November 2026") instead of `aria-labelledby`. Day-cell names come from **sr-only text inside the cell**, not `aria-label` (axe `label-content-name-mismatch`); weekday headers = `aria-hidden` short name + sr-only long name. | Lint + axe findings; the heading text ("Diary – …") is not the right grid name. |
| 9 | **Rate** is `…/runs/:runId?rate=1` (the details route opens `RateRunDialog` for Completed runs), not a separate route. | One route per run view; deep-linkable and Back-friendly. |
| 10 | Panel routes are **lazy** (`features/runs/panelRoutes.ts`, React Router `lazy`) and **preloaded** once the grid has rendered (`preloadPanels`). Neighbour months are prefetched once the visible month has loaded (not "when idle"). | Keeps RHF + zod out of the initial bundle without a click-time delay. |
| 11 | **Nested focus return**: action links carry `state={ returnFocus: <selector> }`; `useClosePanel` (`navigate(-1)` for in-app opens, `replace` to the month for deep links) hands the selector to the dialog that remounts, which focuses it in `onOpenAutoFocus` (`features/runs/returnFocus.ts`). Closing to the grid focuses the originating day cell. **Browser Back does not restore nested focus.** | Sibling panel routes remount the previous dialog, whose default first-tabbable focus would otherwise win. |
| 12 | Guest session hardening: **cross-tab sync** via the `storage` event (removal elsewhere signs this tab out; a different token replaces the session and drops caches); **skew-corrected expiry** — remaining lifetime is measured on the server clock from the response `Date` header and applied to the client clock (stored `expiresAt` is client epoch ms); on sign-out the **mutation cache is cleared** and all queries except `client-config` are removed; a **session-epoch guard** stops a late mutation from invalidating the next session's queries; a 401 signs out only if the request's bearer matches the current session; **`/api/me` refetches on window focus** (overnight tab: `today`, guest expiry). | Review findings (see below). |
| 13 | Forms: **untouched numbers keep the exact loaded SI value** (`numberOrBase`; e.g. a GPX-derived 10 234.567 m is not rewritten as 10 235 m); `toWriteRequest` is wrapped in `withAllWriteMembers`, a **type-level exhaustiveness check over `RunWriteRequest`** (a new API member fails `typecheck`); inputs use RHF `register` with **native selects** (one `Controller`); live pace announced through a **debounced** sr-only `<output>`; month changes announced by `MonthHeader`. The Rate dialog's 409 refetches the latest version, keeps the typed rating and asks the user to save again. | Review findings; avoids silent data change on a full-replace PUT. |
| 14 | Build: Vite 8 `build.rolldownOptions` (not `rollupOptions`) with `input` = `index.html` + `redirect.html` and **`advancedChunks` vendor groups** (react, router, query, i18n). Initial load ≈ **600 kB raw / ≈ 195 kB gzip**; MSAL (≈ 204 kB) and the run form (≈ 146 kB) are lazy chunks. | Stable vendor chunk names; MSAL/zod/RHF stay out of the initial load. |
| 15 | Tests: **one fake API (no MSW)** in `web/src/test/fakeApi/` serves Vitest (global `fetch` stub, `src/test/fake.ts`) and Playwright (`fake.fulfillRoute` in `e2e/support.ts`). Spec files: `e2e/{smoke,auth,calendar,runs,journeys}.spec.ts` + `diary.phone.spec.ts`. **Vitest flake root cause**: the first lazy-chunk import raced the `findBy*` timeout; fixed by awaiting `loadAllPanels()` in `src/test/render.tsx` and `asyncUtilTimeout: 4000` (plus `testTimeout` 15 s). | — |
| 16 | Playwright projects **`chromium`** and **`phone-la`** (360×640, touch, `timezoneId: America/Los_Angeles`, matches `*.phone.spec.ts`) instead of a 390×844 "phone" project. | 360 px is the AC14 width. |
| 17 | Full-stack journey: `web/e2e-fullstack/journey.spec.ts` with its own `playwright.fullstack.config.ts` (`npm run test:e2e:fullstack`, `FULLSTACK_BASE_URL`, no `webServer`), not `e2e/fullstack/` + `E2E_FULLSTACK=1`. New CI job **`e2e-fullstack`** (push to `main` + `workflow_dispatch`): plain `docker run` SQL Server + Azurite, `dotnet run` the API (Development, Entra disabled, per-run random SQL password and guest key, masked), `vite preview` with `API_HTTP` proxy — **not the AppHost**. Verified locally by a replica run. CI `concurrency` cancels **only superseded PR runs**; pushes to `main` always complete. | No DCP/dashboard/volumes to bootstrap in CI; same images as the test fixtures. |
| 18 | `GET /api/client-config` returns **`entra: null`** when Entra is disabled **or** `SpaClientId` is missing (startup **warning** `EntraSpaClientIdMissing`, no failure). Default scope is `api://{Audience}/{RequiredScope}` (`RequiredScope` defaults to `access_as_user`); `SpaClientId`/`ApiScope` live on `EntraAuthOptions` in `Auth/AuthOptions.cs`. Mapped in the anonymous `AllowAnonymous().DisableRateLimiting()` group with health. | As ADR 0015; file location differs from §2. |
| 19 | i18n namespaces: `common, auth, calendar, runs, runTypes, errors`. oxlint `react/jsx-no-literals` + `react/no-danger` on; `jsx-no-literals` and `prefer-tag-over-role` off for the copied shadcn files in `components/ui/` (listed explicitly); `jsx-no-literals` off for tests and E2E specs. | — |

**Verified (2026-11-03):** Vitest **488/488** (15 consecutive clean runs); `test:tz` **341 × 2** (America/Los_Angeles, Pacific/Kiritimati); Playwright **50/50** (`chromium` + `phone-la`, incl. axe on sign-in, calendar, day panel, run form); full-stack journey **1/1**; .NET **693/693** (to be confirmed on CI); web lint, typecheck, Prettier and build clean; no OpenAPI/TS drift.

**Review follow-ups applied before commit:**
guest session cross-tab `storage` sync; skew-corrected guest expiry from the `Date` header; mutation cache cleared on sign-out and a session-epoch guard on post-mutation invalidation; 401 handled only for the current session's bearer; `/api/me` refetch on window focus; untouched numeric fields keep their exact SI values; `toWriteRequest` type-level exhaustiveness over `RunWriteRequest`; native selects; debounced pace announcement and month-change announcement; grid naming per axe (`aria-label`, sr-only cell text); silent-token `AuthError`s other than network errors end the session; MSAL `state` dropped in favour of the sanitised `returnTo`; CI concurrency limited to superseded PR runs.

**Still open:**
- **AC3/AC4 deferred** (§9 Q4): real Entra sign-in, sign-out (`postLogoutRedirectUri`), token accepted by the API and zone sync — once the tenant exists; this also settles the **M2 issuer string**.
- **M7**: serve `/redirect.html` **without COOP** and with `Cache-Control: no-store`; CSP (`script-src 'self'`, `connect-src`/`frame-src` for `ciamlogin.com`).
- Browser **Back does not restore nested focus** (only in-app Close does); accepted for M3.
