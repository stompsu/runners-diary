# M8 – Race proposals: plan

**Status:** Approved 2026-11-13 with all §11 recommended defaults (proposed 2026-11-13). **Part A implemented 2026-11-14** on `m8-race-proposals` (all steps 1–10 of §6.1, incl. the UI, E2E, review and its fixes); **awaiting approval and merge**; part B (G-R1…G-R4) untouched, nothing provisioned (§12, follow-ups §13) · **Date:** 2026-11-13, updated 2026-11-14 · **Author:** architect
**Builds on:**
- M2: `Runs`, the run-type catalogue, `Owner` filter, guest lifecycle and `DemoDataSeeder`.
- M3: month grid (ADR 0016), panel routes, run form (ADR 0018), fake API.
- M4: `MapView`, `LocationField`, the GPX geodesy in Domain.
- M5/M6: insights and the fact sheet. They are unchanged: signed-up races are ordinary runs.
- M7: `JobMode`, `job-guest-cleanup`, Entra-only SQL, gated provisioning, alerts.

**Related ADRs:** [0008](../adr/0008-run-date-and-time-storage.md) · [0010](../adr/0010-current-user-and-data-scoping.md) · [0011](../adr/0011-guest-accounts-lifecycle.md) · [0012](../adr/0012-request-validation.md) · [0013](../adr/0013-persistence-model-and-migrations.md) · [0016](../adr/0016-calendar-grid-and-url-state.md) · [0031](../adr/0031-identities-secrets-and-data-access.md) · [0032](../adr/0032-migrations-and-jobs-in-azure.md) · [0033](../adr/0033-production-maps-and-azure-maps.md) · [0034](../adr/0034-cost-guardrails-and-observability.md).
**New (accepted with the plan; amended 2026-11-13 after the backend/infra implementation):**
- [0035](../adr/0035-race-catalogue-sources-and-crawling-policy.md): sources and the crawling/legal policy;
- [0036](../adr/0036-race-catalogue-model-and-race-runs.md): catalogue model, upsert/dedup/removal, proposal → planned race run;
- [0037](../adr/0037-race-proposals-filtering-and-state.md): interests, filtering/ranking, per-user state, API/calendar shape;
- [0038](../adr/0038-race-import-job.md): the import job on Container Apps.

M8 is a post-MVP feature (brief §1.4 lists none of it; it touches no out-of-scope item). Like M7 it has an **offline part A** (code, Bicep, tests; nothing in the cloud) and **user-confirmed gates** G-R1…G-R4 (§6.2).

## 1. Source research summary

Research note of 2026-11-13: live requests with an identifying User-Agent, plus web search. robots.txt of running.life re-checked on 2026-11-13. **No usable official API exists for Polish races**, so M8 imports from a public calendar site under strict rules (ADR 0035).

| Source | Coverage (PL / Silesia / kinds) | Fields available | Access | Terms / robots / rate | Licensing / attribution | Stability | Legal risk |
|---|---|---|---|---|---|---|---|
| **running.life** | ≈ 600 upcoming PL events (≈ 30 listing pages × 20; 607 event URLs in the sitemaps); śląskie ≈ 70; road (city), trail, mountain; 45k worldwide | Listing JSON-LD `ItemList` of `SportsEvent`: name, url, date, locality, region (English voivodeship), country, **lat/lng**. Detail page: distances, type, organiser website, registration URL (often a signup platform), description (not taken) | Server-rendered HTML + JSON-LD | robots: only `/<lang>/map/` and `/demo-*` disallowed; no terms page; no stated rate limit | "© All Rights Reserved" footer; community-edited; affiliate links | Medium: a stable schema.org layer, but a page layout change breaks the detail parser | **Medium**: no terms means default copyright + database right. Mitigation: permission, facts only, attribution + link, weekly polite crawl |
| **kalendarzbiegowy.pl** | **2,202** events, all 16 voivodeships (śląskie 308); kinds street/cross/mountain/ultra/trail/OCR… | WP REST: title, link, modified, taxonomies (distance, kind, voivodeship). Event page JSON-LD: date (unpadded), place name + street address, prose | Public WordPress REST (`modified_after`, paging), plus one page per event for the date | robots allow all; no terms (privacy policy only) | No licence stated | Good: a standard WordPress API | **Medium** (same footing). Needs geocoding (no coordinates) and a 2.2k-page backfill |
| AIMS iCal | 426 world events, PL 4 | SUMMARY, dates, country, organiser URL, UID, LAST-MODIFIED | Official iCal feed for subscription | Offered for subscription | © AIMS; `ORGANIZER` is a person's name (never stored) | High | **Low** (offered for reuse) but irrelevant for Silesia |
| ahotu | 58k events, PL 1,156 | Full, incl. coordinates, price, signup | Commercial GraphQL, OAuth2; contact-only pricing | Contract | Licensed | High | Low with a contract; **cost unknown** |
| treningbiegacza.pl | 1,157 PL | Date/distances only on SSR pages | Internal headless-WP API | robots `Disallow: /api/`; `ai-train=no` | None | Medium | **High-ish**: an internal API of a commercial site |
| elektronicznezapisy.pl | 66 events | Table, signup links | HTML | Terms: rights to the operator, use with consent | None | Medium | Medium; low value |
| **Excluded** | maratonypolskie.pl (robots `Disallow: /` for all crawlers but Googlebot; terms forbid copying) · enduhub.com (terms forbid using its databases) · dostartu.pl (undocumented API, robots disallow) · b4sport / datasport (no API) · World Athletics (hidden GraphQL) · ITRA (bot protection) · UTMB (no API) · finishers / trainerday / hdsports / intervals.icu (no benefit, weaker footing) | | | | | | Excluded by robots/terms |

**Recommendation (agreed with the research):**
- **Primary: running.life.** It is the only source that ships coordinates and voivodeship in structured data, so M8 needs **no geocoding**. About 30 listing requests give the whole Polish snapshot; detail pages are needed only for new or changed events.
- **Secondary (phase 2, not in M8): kalendarzbiegowy.pl**, for coverage (≈ 3–4× running.life).
- **AIMS iCal** only if international majors are wanted (Q6).

Both Polish sources lack terms, so **permission e-mails go out before production import** (G-R1, outline §10). Facts only; attribution and a link back on every race.

**Cost estimate** (detail: ADR 0038 §6):

| Item | Estimate |
|---|---|
| Job runtime | ≈ 2 min a week (30–40 listings + tens of details at 1.5 s per request); ≈ 5–6 min per run during the first backfill (detail cap 150 per run, ≈ 4 runs to complete) |
| SQL | runs 10 min after the guest cleanup, inside the same Monday wake: +75–180 vCore-s per run ≈ ≤ 1 % of the monthly free 100k |
| Rows | ≈ 600–1,000 `Races` + ≈ 2–3k `RaceDistances` (< 2 MB) |
| Geocoding | 0 calls in M8 |
| Money | ≈ €0 a month (ACA Consumption free grant, SQL free offer; no new SKU) |

## 2. Goal & acceptance criteria

**Goal:** users and guests see **upcoming races that match their interests** in the monthly diary and a list. They can open race details, go to the official signup page, dismiss or mark races, and with "I signed up" turn a race into a **planned race run** that the rest of the app already understands. The catalogue is filled weekly by a polite, switchable import job.

**Out of scope:**
- the second source (kalendarzbiegowy) and geocoding of imported races;
- international sources;
- course GPX from sources (none provides it);
- notifications or e-mail;
- relevance scoring;
- `pl` translation (keys ready);
- race facts in the AI summary;
- user-submitted catalogue races.

| # | Criterion | How verified |
|---|---|---|
| AC1 | `RunnersDiary.Domain/Races/` implements normalisation (distance text → m, kind, voivodeship → ISO code), `FactsHash`, the merger (new / unchanged / changed / date change / missed scans / removal / restore; incomplete scan changes nothing), the duplicate matcher, interest rules, the proposal builder (window clamp, kinds incl. derived ultra, region ∪ radius, distance overlap, keywords all-terms, dismissed, signed-up always), and `RaceRunMapper` (ADR 0036 §5 table). Domain still has no package references. | Domain.Tests |
| AC2 | Migration `RaceCatalogue` creates `Races`, `RaceDistances`, `RaceInterestProfiles`, `RaceProposalStates`, and `Runs.CatalogRaceId` with the filtered unique index; it is additive (expand rule). Guest expiry removes the guest's profile and state rows (cascade), and deleting a linked run un-signs the proposal. | Api.Tests (Testcontainers) |
| AC3 | Endpoints of §4.1 behave as specified (validation matrix, 404s, 409s, headers). OpenAPI + `web/src/api/generated/schema.d.ts` are regenerated; the api-contract CI job is green. | Api.Tests + CI |
| AC4 | Isolation: guest A's interests, dismissals and signups never change guest B's or user C's responses. `signUpForRace` creates the run under the caller only. All new endpoints are **Auth** in the inventory test, and the 401 sweep covers them. | Api.Tests |
| AC5 | A fresh guest (local/CI demo catalogue) sees ≥ 3 proposals in the current or next month in Silesia, none from the far-away demo races, and none past. | Api.Tests + full-stack |
| AC6 | Calendar: proposal bubbles are **visually distinct** from planned (dashed) and completed (filled) runs (dotted border, flag icon, muted fill, name not type), with a legend entry and a toggle that hides them (persisted as `showInCalendar`). Runs take precedence within `MAX_BUBBLES`. Each day cell's accessible name includes its proposals, and the grid keeps one tab stop with arrow-key navigation (ADR 0016). | Vitest + Playwright + axe |
| AC7 | Race details: name, date (+ time), place, kind, distances, elevation, price, organiser, signup deadline, source attribution with a link, map with a pin (lazy `MapView`). External links use `target="_blank" rel="noopener noreferrer"`, `referrerPolicy="no-referrer"` and are filtered through `safeUrl`. Actions: Open signup page, I signed up, Interested, Not interested / Undo. Removed and date-changed banners. | Vitest + Playwright |
| AC8 | "I signed up" with a distance choice (when > 1) creates a planned race run (ADR 0036 mapping) and opens it. The calendar, the month list and insights show it. A second signup → 409 with a link to the existing run. A date change shows the banner and the explicit "Move my run" action; nothing moves automatically. | Api.Tests + Playwright + full-stack |
| AC9 | Settings → Race interests (`/settings/races`): kinds, voivodeships, place + radius (existing `LocationField` + map preview with a radius circle), distance range with presets, horizon, keywords, show-in-calendar, and source attribution. react-hook-form + zod, server errors per ADR 0018. Responsive, keyboard-usable. | Vitest + Playwright (desktop + phone-la) |
| AC10 | `--job race-import`: disabled → exit 0 **with no SQL connection opened**; lock held → exit 0; the Demo source imports idempotently (second run: 0 changes); the RunningLife adapter on synthetic fixtures yields the expected facts; robots/UA/interval/304/stop rules/host allow-list/size cap behave per ADR 0035 §4; layout change → exit 1. | Infrastructure tests (fake `HttpMessageHandler`) + Api.Tests |
| AC11 | Bicep: `job-race-import` (cron param, `id-rd-race-import`, flags default off), `Races__Enabled` on `api`, the failed-execution alert, grant script table-level grants; `az bicep build/lint` clean. `deploy.yml` updates and rolls back the job's image. **Nothing provisioned in part A.** | CI `deploy-artifacts` + review |
| AC12 | i18n: new lazily registered `races` namespace, plus grid-level keys in `calendar.json`; no literals; region and kind labels as keys. Initial bundle grows ≤ 1.5 kB gzip; the races chunks are `LAZY_ONLY`. **Result: +3.0 kB, target missed, accepted (§12.2 F12).** | CI (`bundle-budget`) |
| AC13 | `dotnet build` warning-free; all CI jobs green; the full-stack suite includes a race-proposal journey. | CI |

## 3. Files / projects by owning agent

### 3.1 backend-dotnet
```
src/RunnersDiary.Domain/Races/
  Race.cs, RaceSource.cs (RunningLife = 1, Demo = 100), RaceKind.cs, RaceFacts.cs   # normalised import record
  RaceNormalizer.cs          # "10 km", "21,1 km", "półmaraton", "maraton", "5K" -> metres; kind text -> RaceKind; drop < 1,000 m
  PolishRegions.cs           # ISO 3166-2:PL codes <-> English/Polish names (16 rows, tested)
  CountryTimeZones.cs        # "PL" -> Europe/Warsaw; unknown -> null
  RaceFactsHasher.cs         # canonical JSON + SHA-256
  RaceCatalogueMerger.cs     # ADR 0036 §2 (pure; returns inserts/updates/removals/restores)
  RaceDuplicateMatcher.cs    # ADR 0036 §4 (name folding, token Jaccard, 3 km)
  RaceInterestProfile.cs, RaceInterestRules.cs, RaceProposalState.cs (IUserOwned)
  RaceProposalBuilder.cs     # ADR 0037 §2
  RaceRunMapper.cs           # ADR 0036 §5 -> RunValues (passes RunRules)
src/RunnersDiary.Domain/Runs/Run.cs                         # + CatalogRaceId (set only via LinkToCatalogRace in Create)
src/RunnersDiary.Infrastructure/Persistence/Configurations/{Race,RaceInterestProfile,RaceProposalState}Configuration.cs
src/RunnersDiary.Infrastructure/Persistence/Configurations/RunConfiguration.cs   # + CatalogRaceId, FK SET NULL, filtered unique index
src/RunnersDiary.Infrastructure/Persistence/RunnersDiaryDbContext.cs             # DbSets; Owner filter on profile + state (not on Races)
src/RunnersDiary.Infrastructure/Persistence/Migrations/<ts>_RaceCatalogue.cs
src/RunnersDiary.Infrastructure/Races/
  RaceProposalService.cs     # 3 projections -> RaceProposalBuilder (ADR 0037 §2)
  RaceSignupService.cs       # mapper + run caps (reuses RunLimitGuard logic) + unique-index 409
  DemoRaceCatalogue.cs       # loads seed/races/demo-races.json; SeedOnStartup (Development/Testing only)
src/RunnersDiary.Infrastructure/RaceSources/
  IRaceSource.cs, RaceImportOptions.cs, RaceImportService.cs (lock, index read, crawl, transactional write, purge)
  PoliteHttpClient.cs, RobotsTxt.cs (RFC 9309 subset), JsonLd.cs (AngleSharp + System.Text.Json)
  RunningLife/RunningLifeSource.cs, RunningLife/RunningLifeParser.cs
  Demo/DemoRaceSource.cs
src/RunnersDiary.Infrastructure/Guests/DemoDataSeeder.cs     # + demo RaceInterestProfile (§3.2)
src/RunnersDiary.Infrastructure/DependencyInjection.cs       # race-import job profile (persistence + RaceSources only)
src/RunnersDiary.Api/Features/Races/
  RaceContracts.cs, GetRaceInterests.cs, UpdateRaceInterests.cs, ListRaceProposals.cs, MarkRaceProposalsSeen.cs,
  GetRace.cs, SetRaceProposalState.cs, SignUpForRace.cs, RacesEndpoints.cs
src/RunnersDiary.Api/Features/Runs/{RunContracts.cs, RunMapping.cs}     # RunDto.catalogRaceId (read-only)
src/RunnersDiary.Api/Features/ClientConfig/*                            # + races
src/RunnersDiary.Api/Jobs/JobMode.cs                                    # + race-import
src/RunnersDiary.Api/ProductionConfigurationValidator.cs                # demo catalogue/source forbidden in Production; job settings
src/RunnersDiary.Api/openapi/runnersdiary-api.json                      # regenerated
src/RunnersDiary.AppHost/AppHost.cs                                     # Races__Enabled=true, Races__DemoCatalogue__SeedOnStartup=true
seed/races/demo-races.json, seed/races/README.md                        # synthetic catalogue (§3.2)
Directory.Packages.props                                                # + AngleSharp (justification: ADR 0035 §6)
```

### 3.2 Demo data (backend-dotnet)
- **`seed/races/demo-races.json`:** about 40 **invented** races, source `Demo`, dates as `{ "week": n, "day": "Sun" }` relative to the current ISO week in Europe/Warsaw (−2 … +24). Contents:
  - ≥ 12 in śląskie near Gliwice, Katowice, Bielsko-Biała and Rybnik, with town-centre coordinates;
  - 3 elsewhere (Gdańsk, Wrocław, Zakopane);
  - all kinds; one race with 3 distances; one ultra; one OCR;
  - one race whose date the second seed run moves, to exercise `dateChanged` in tests (a test-only fixture variant).
  - Names are obviously synthetic ("Demo Gliwice Night 10K"). No URLs to real sites (`sourceUrl` null, `signupUrl` `https://example.org/demo-signup`).
- **Guest demo profile:**
  - kinds Road, Trail, Mountain; `regionCodes ["PL-24"]`;
  - centre Gliwice (50.2945, 18.6714) with a 60 km radius;
  - distances 5,000–42,195 m; horizon 120 days; `showInCalendar` true.
- **Startup check:** the existing `DemoDatasetStartupCheck` pattern validates the race seed file (parses, normalises, ≥ 10 future races in PL-24) when seeding is on.

### 3.3 maps-gpx
```
web/src/components/map/types.ts, MapView.tsx, maplibre/*, azure/*   # + `area?: { center: LatLon; radiusMeters: number }` (64-vertex polygon, outline + 10 % fill)
web/src/components/map/markers.ts                                     # + createRaceElement (flag icon, role=img, label)
src/RunnersDiary.Domain/Gpx/Geodesy (existing haversine)              # exposed for Races (no copy)
```

### 3.4 frontend-react
```
web/src/app/routes.tsx                     # /settings -> /settings/races (lazy); diary panel routes races, races/:raceId
web/src/components/layout/AppShell.tsx     # nav "Settings" when client-config.races.enabled
web/src/features/races/
  RaceInterestsPage.tsx, raceInterestsSchema.ts, RegionPicker.tsx, KindPicker.tsx, DistanceRangeField.tsx, RadiusField.tsx
  raceQueries.ts (interests, proposals, race, mutations + invalidation), raceFormat.ts (distances "10 / 21.1 km", price)
  RaceDetailsDialog.tsx, RaceSignupForm.tsx, DateChangedBanner.tsx, RaceSourceAttribution.tsx
  ProposalsListPanel.tsx, ProposalRow.tsx
web/src/features/calendar/
  ProposalBubble.tsx (+ ProposalDot), CalendarLegend.tsx, ProposalsToggle.tsx
  DayCell.tsx, MonthGrid.tsx, DiaryMonthPage.tsx, calendarQueries.ts   # merge proposals per date; prefetch with months
web/src/features/runs/{DayPanel.tsx, panelRoutes.ts, runQueries.ts, RunDetailsDialog.tsx}   # day section "Race proposals"; invalidate proposals on run writes; "From race listing" link
web/src/i18n/en/races.json (new, lazy), calendar.json, common.json, i18n/lazyBundles.ts, i18n/i18next.d.ts
web/scripts/bundle-budget.mjs              # LAZY_ONLY += races chunks
web/src/api/generated/schema.d.ts          # regenerated
```

### 3.5 azure-infra
```
infra/modules/identities.bicep    # + id-rd-race-import
infra/modules/registry.bicep      # + AcrPull for it
infra/modules/jobs.bicep          # + job-race-import (ADR 0038 §3)
infra/modules/alerts-budget.bicep # + alert-rd-race-import-failed (Executions, state = failed)
infra/main.bicep, infra/main.parameters.json   # params raceImportEnabled, raceImportRunningLife, raceImportCron, racesUiEnabled
                                                # (azd env RACE_IMPORT_ENABLED, RACE_IMPORT_RUNNING_LIFE, RACE_IMPORT_CRON, RACES_UI_ENABLED)
infra/sql/grant-app-users.sql     # + race-import user, table-level grants only
infra/docker/migrate.sh           # + RACE_IMPORT_IDENTITY_NAME / _CLIENT_ID
.github/workflows/deploy.yml      # job image update + previous + rollback
.github/workflows/ci.yml          # e2e-fullstack: Races__Enabled / DemoCatalogue env for the API container
```

### 3.6 qa-tester
```
tests/RunnersDiary.Domain.Tests/Races/*                     # §9
tests/RunnersDiary.Api.Tests/RaceSources/*                 # no Infrastructure test project exists; adapter tests live here (no container needed)
tests/RunnersDiary.Api.Tests/Races/*, Isolation/IsolationMatrixTests.cs, Security/EndpointAuthorizationTests.cs, Jobs/RaceImportJobTests.cs
tests/fixtures/races/   # SYNTHETIC HTML/JSON-LD imitating the source structure (never copies): listing-p1.html, listing-empty-layout.html,
                        # detail-multi-distance.html, detail-no-distances.html, jsonld-malformed.html, jsonld-deep.html, robots-*.txt
web/src/test/fakeApi/races.ts + fixtures; web/src/features/races/*.test.tsx; calendar tests
web/e2e/races.spec.ts, races.phone.spec.ts; web/e2e-fullstack/races.spec.ts
```

### 3.7 reviewer
Checklist for M8:
- **Data access:** IDOR on state, signup and run links; the `Owner` filter on the new user tables; no `Races` writes from the API; no user id in requests.
- **Crawling:** ADR 0035 compliance (UA, interval, robots, stop rules, host allow-list, no fetch of stored URLs); facts only (no description fields anywhere in the model).
- **Untrusted input:** HTML/JSON-LD parsing limits; stored URLs rendered only via `safeUrl`.
- **Infra:** least privilege of the job identity; migration expand rule; cost.
- **UI:** a11y of the grid with proposals.

### 3.8 architect
After implementation: ADR status changes and amendments, deviations (§12), `data-model.md`, `api-contract.md` and `architecture.md` moved from "Planned in M8" to "Added in M8".

## 4. Contracts

As planned; the implemented contract (503 when disabled, `RaceStateChoice`, flat `RaceDetailsDto`, answer order) is in [api-contract.md](../api-contract.md#added-in-m8-race-proposals) and §12 (D13–D19).

### 4.1 Endpoints (all **Auth**, users and guests; general rate limiter)

| Method & path | operationId | Success | Errors |
|---|---|---|---|
| `GET /api/races/interests` | `getRaceInterests` | 200 `RaceInterestsResponse` (`configured: false` + defaults when no row) | 401 |
| `PUT /api/races/interests` | `updateRaceInterests` | 200 `RaceInterestsResponse` | 400, 401 |
| `GET /api/races/proposals?from&to&includeDismissed` | `listRaceProposals` | 200 `RaceProposalListResponse`, `Cache-Control: private, no-store` | 400 (`from`, `to`: 1950–2100, `to ≥ from`, span ≤ 92 days; `includeDismissed` boolean), 401 |
| `POST /api/races/proposals/seen` | `markRaceProposalsSeen` | 204 (sets `ProposalsSeenUtc`; no-op without a profile) | 401 |
| `GET /api/races/{raceId}` | `getRace` | 200 `RaceDetailsDto` | 401, 404 (unknown; purged; removed and neither interested nor linked) |
| `PUT /api/races/{raceId}/state` | `setRaceProposalState` | 200 `RaceUserStateDto` | 400, 401, 404, 409 `proposal-state-limit` |
| `POST /api/races/{raceId}/signup` | `signUpForRace` | 201 `RunDto` + `Location: /api/runs/{id}` | 400 (`raceId` past race; `distanceMeters` not offered or missing when > 1; `runTypeId` not a race type; `startTime`; `targetTimeSeconds`), 401, 404 (unknown, removed or duplicate race), 409 `race-already-linked` (extension `runId`), 409 `run-limit` |
| `GET /api/client-config` (changed) | `getClientConfig` | 200 + `races` | — |
| Runs endpoints (changed) | `getRun`, `listRuns`, `createRun`, `updateRun`, `getCalendarMonth` | `RunDto` gains read-only `catalogRaceId` | unchanged; `updateRun` preserves the link |

New problem types:
- 409 `urn:runnersdiary:problem:race-already-linked` (`runId` = the caller's existing run);
- 409 `urn:runnersdiary:problem:proposal-state-limit` (users 2,000, guests 200 rows).

### 4.2 Shapes
```ts
RaceKind = "Road" | "Trail" | "Mountain" | "Ultra" | "CrossCountry" | "Ocr" | "Other"   // PascalCase like RunStatus
ProposalState = "None" | "Interested" | "Dismissed" | "SignedUp"                         // SignedUp is derived, never sent

RaceInterestsResponse = {
  configured: boolean;
  kinds: RaceKind[]; regionCodes: string[];                     // ISO 3166-2, e.g. "PL-24"
  center: { latitude: number; longitude: number; name: string | null } | null; radiusMeters: number | null;   // 5,000–300,000; UI edits km
  minDistanceMeters: number | null; maxDistanceMeters: number | null;
  horizonDays: number; keywords: string | null; showInCalendar: boolean; updatedAt: string | null
}
UpdateRaceInterestsRequest = Omit<RaceInterestsResponse, "configured" | "updatedAt">     // full replace; rules ADR 0037 §1

RaceProposalListResponse = {
  from: string; to: string; effectiveFrom: string; effectiveTo: string;   // clamp to [today, today + horizon]; effectiveTo < effectiveFrom = empty
  profileConfigured: boolean; catalogueUpdatedUtc: string | null; truncated: boolean;
  items: RaceProposalDto[]                                                  // date, then rank (ADR 0037 §2)
}
RaceProposalDto = {
  raceId: string; name: string; date: string; startTime: string | null;
  locationName: string | null; regionCode: string | null; countryCode: string;
  location: { latitude: number; longitude: number } | null;
  kind: RaceKind; distancesMeters: number[];                                // ascending
  state: ProposalState; linkedRunId: string | null; isNew: boolean; dateChanged: boolean;
  source: string                                                            // source id -> client-config.races.sources
}
RaceDetailsDto = RaceProposalDto & {
  timeZoneId: string | null; surface: "Road" | "Trail" | "Track" | "Treadmill" | "Mixed" | null;
  elevationGainMeters: number | null; price: { amount: number; currency: string } | null;
  signupDeadline: string | null; organizerName: string | null;
  signupUrl: string | null; eventUrl: string | null; sourceUrl: string | null;
  removed: boolean; previousDate: string | null; canonicalId: string | null; lastSeenUtc: string; updatedUtc: string
}
SetRaceProposalStateRequest = { state: "None" | "Interested" | "Dismissed" }
RaceUserStateDto = { raceId: string; state: ProposalState; linkedRunId: string | null; dateChanged: boolean }
SignUpForRaceRequest = { distanceMeters?: number | null; startTime?: string | null /* HH:mm */;
                         runTypeId?: number | null /* Race category */; targetTimeSeconds?: number | null /* 1..604800 */ }
ClientConfigResponse.races = { enabled: boolean; sources: { id: string; name: string; url: string }[] }
RunDto.catalogRaceId: string | null
```

### 4.3 Schema delta (migration `RaceCatalogue`; full tables in [data-model.md](../data-model.md#added-in-m8-race-proposals))

| Object | Summary |
|---|---|
| `Races` | Catalogue (no user id, **no Owner filter**). Clustered `(Date)`, `UX_Races_Source_External`, `IX_Races_DuplicateOf` (filtered). |
| `RaceDistances` | `(RaceId, DistanceMeters)` PK, FK cascade. |
| `RaceInterestProfiles` | `UserId` PK, FK → `Users` cascade, Owner filter. |
| `RaceProposalStates` | `(UserId, RaceId)` PK clustered, FKs → `Users` cascade and `Races` cascade, Owner filter. |
| `Runs.CatalogRaceId` | `uniqueidentifier NULL`, FK → `Races` SET NULL, `UX_Runs_User_CatalogRace (UserId, CatalogRaceId) WHERE CatalogRaceId IS NOT NULL`. |

### 4.4 Domain signatures (sketch)
```csharp
public sealed record RaceFacts(RaceSource Source, string ExternalId, string Name, DateOnly Date, TimeOnly? StartTime,
    string? LocationName, GeoPoint? Point, string CountryCode, string? RegionCode, RaceKind Kind, Surface? Surface,
    IReadOnlyList<double> DistancesMeters, double? ElevationGainMeters, string? SignupUrl, string? EventUrl, string? SourceUrl,
    decimal? PriceAmount, string? PriceCurrency, DateOnly? SignupDeadline, string? OrganizerName);
MergePlan RaceCatalogueMerger.Plan(IReadOnlyList<RaceIndexEntry> existing, IReadOnlyList<RaceFacts> scanned,
    bool scanComplete, DateOnly today, DateTimeOffset now, int removeAfterMissedScans);
IReadOnlyList<RaceProposal> RaceProposalBuilder.Build(RaceInterestProfile profile, DateOnly today, DateOnly from, DateOnly to,
    IReadOnlyList<RaceSnapshot> races, IReadOnlyDictionary<Guid, RaceProposalState> states,
    IReadOnlyDictionary<Guid, LinkedRun> linkedRuns, DateTime? seenUtc, bool includeDismissed);
RunValues RaceRunMapper.ToPlannedRun(RaceSnapshot race, RaceSignupChoice choice, string profileTimeZoneId);
```

### 4.5 Configuration (defaults in `appsettings.json`; Production overrides via Bicep env)

| Key | Default | Notes |
|---|---|---|
| `Races:Enabled` | `false` (AppHost/tests: `true`) | → `client-config.races.enabled` |
| `Races:DemoCatalogue:SeedOnStartup` | `false` (AppHost/tests/CI full-stack: `true`) | **Production validator rejects `true`** |
| `Races:Proposals:MaxCatalogueRows` | 1000 | → `truncated` |
| `RaceImport:Enabled` | `false` | Global kill switch; disabled = exit 0, no SQL |
| `RaceImport:Sources:RunningLife:Enabled` / `:Demo:Enabled` | `false` / `false` | Demo forbidden in Production |
| `RaceImport:MinInterval` | `00:00:01.500` | Values < 1 s rejected at startup |
| `RaceImport:MaxListingPages` / `MaxDetailPages` | 40 / 150 | Per run |
| `RaceImport:DetailRefreshAfter` | 28 days | |
| `RaceImport:RemoveAfterMissedScans` | 2 | |
| `RaceImport:RetainPastDays` | 180 | Q9 |

Job CLI: `dotnet RunnersDiary.Api.dll --job race-import` → exit 0 / 1, with the outcomes of ADR 0038 §1.

### 4.6 Client queries and invalidation

| Query | Cache | Invalidated by |
|---|---|---|
| `getRaceInterests` | `staleTime` 10 min | `updateRaceInterests` (also sets the cache from the response) |
| `listRaceProposals(gridStart, gridEnd)` | `staleTime` 5 min; `keepPreviousData`; prefetched with adjacent months when shown | `updateRaceInterests`, `setRaceProposalState`, `signUpForRace`, `createRun` / `updateRun` / `deleteRun` (signed-up and `dateChanged` derive from runs) |
| `getRace(id)` | `staleTime` 5 min | `setRaceProposalState`, `signUpForRace`, run update/delete |
| `signUpForRace` success | — | + `getCalendarMonth`, `listRuns`, `getInsightsReport` (the shared run-write invalidation helper) |

The calendar issues `listRaceProposals` only when `client-config.races.enabled`, the profile is configured and `showInCalendar` is true.

## 5. UI

- **Settings → Race interests (`/settings/races`, lazy).** One form, saved explicitly; phone = one column. Sections:
  1. *What*: kind checkboxes, with an Ultra hint ("also any race longer than a marathon").
  2. *Where*:
     - a voivodeship checkbox list (16, English name with the Polish name in brackets, "Select all" / "Clear");
     - and/or "Near a place": the existing `LocationField` (picker dialog + place search when geocoding is on), a radius number + range input (5–300 km), and a `MapView` preview with the radius circle (`area`).
     - Hint: both = either matches; neither = all of Poland.
  3. *Distance*: min/max in km with presets (5K, 10K, Half, Marathon, Ultra → 42.2–250).
  4. *When*: horizon select (30 / 60 / 90 / 120 / 180 / 365 days).
  5. *Keywords* (optional).
  6. *Show proposals in the diary* switch.
  7. *Sources*: attribution list with links, and the line "Race facts come from public calendars; always check the organiser's page".
- **Diary grid:**
  - **`ProposalBubble`** (desktop):
    - dotted 1 px border in the muted ink, transparent fill, a `Flag` icon, the race **name** (truncated) and its distances ("10 / 21.1 km");
    - a star for Interested, a check for SignedUp (the signed-up run's own bubble also shows, so the proposal bubble is hidden for signed-up races to avoid duplication);
    - `aria-hidden`, click-only like `RunBubble`.
  - **`ProposalDot`** (phone): a hollow flag glyph.
  - **Order within a day:** runs, then proposals, with a shared `MAX_BUBBLES` and "+n".
  - **`CalendarLegend`** below the grid: Completed (filled), Planned (dashed), Race (trophy), **Race proposal (dotted, flag)**. The proposal entry is present only while proposals are shown.
  - **`ProposalsToggle`** in the month header: a switch with `aria-pressed` that updates `showInCalendar` optimistically. Without a profile it is a "Set up race proposals" link instead.
  - **Accessibility:** the cell's accessible name adds `"…; 2 race proposals: Demo Gliwice Night 10K, 10 km; …"` (`calendar:cell.proposals`). Enter on a cell opens the Day panel, which gains a "Race proposals" section with real links/buttons (the keyboard path). Grid keyboard navigation is unchanged.
  - A "Race proposals" button next to "Month list" opens the list panel.
- **Proposals list panel (`/diary/:y/:m/races`):**
  - rows grouped by date, each with name, place, kind, distances, a "New" badge and state;
  - actions: Details, Interested toggle, Not interested / Undo;
  - "Show dismissed" switch; "Race list updated {date}" with a staleness hint after 21 days;
  - calls `markRaceProposalsSeen` on open.
- **Race details dialog (`/diary/:y/:m/races/:raceId`, bottom sheet on phones):**
  - **Header:** name, date and time, place and region, kind, distances, elevation, price, organiser, signup deadline (with "closes in n days" or "closed"), and the attribution "Source: running.life" (link).
  - **Map:** lazy `MapView` with the flag pin (`marker`), plus coordinates as text. Without coordinates: "No map position from the source".
  - **Actions:**
    - **Open signup page** (primary, external, `safeUrl`, `noopener noreferrer`, `no-referrer`; disabled with a reason if there is no URL);
    - **Event website**;
    - **I signed up** → `RaceSignupForm` (distance radio when > 1, optional start time and target time) → on success, toast + navigate to `runs/:runId`;
    - **Interested** (toggle);
    - **Not interested** / **Undo**.
  - **When signed up:** "View my planned run". `DateChangedBanner`: "The race listing says {new}; your planned run is on {old}" with **Move my run to {new}** (a full `PUT /api/runs/{id}` built with `runFormMapping`; a 409 concurrency conflict → reload prompt). For a signed-up race, `dateChanged` is simply `run.Date ≠ race.Date`, so moving the run (or the listing changing back) clears it.
  - **When Interested:** the banner offers **OK**, which re-sends `Interested` and so refreshes `RaceDateWhenMarked`.
  - **When removed:** the banner "No longer listed by the source".
- **Run details dialog:** for linked runs, a "From race listing" link to the race details.
- **i18n** (`races.json`): `nav`, `interests.*`, `kinds.<RaceKind>`, `regions.<code>`, `proposals.*`, `details.*`, `signup.*`, `banners.*`, `attribution.*`. In `calendar.json`: `cell.proposals_one/_other`, `legend.*`, `proposalsToggle.*`, `proposalBubble`. Race **names, localities and organisers stay in the source language** (Polish); UI strings are EN, with keys ready for `pl`.

## 6. Implementation steps and confirmation gates

### 6.1 Part A: offline (each step = one reviewable commit on `m8-race-proposals`)

Progress (2026-11-14): **all done**: 1 (`07f9edf`), 2 (`6b5cffe`), 3 (`4a07bf1`), 4 (`c4dbc61`), 5 (`139f70f`), 6 (`f5cffca`), 7 (`409ba07`), 8 (`f29f930`), 9 (`080e3f3`), 10 (review 2026-11-14; fixes in `aaa5cf9` and `07251cb`; docs `005ecce` + this pass). Deviations: §12; follow-ups: §13.

1. [x] **backend-dotnet (+ qa-tester): Domain `Races/`.** All pure logic of §3.1 with hand-written unit tests (§9). No API change.
2. [x] **backend-dotnet: persistence and demo.** Configurations, migration `RaceCatalogue` (check the cascade ⚠ of ADR 0036 first, on Testcontainers SQL), `Run.CatalogRaceId`, `DemoRaceCatalogue` + seed file + startup check, guest demo profile, AppHost env.
3. [x] **backend-dotnet: endpoints.**
   - `Features/Races/*`, `RunDto.catalogRaceId`, `client-config.races`;
   - OpenAPI → `npm --prefix web run generate:api`;
   - Api.Tests (validation, isolation, inventory, cascade on guest cleanup, run delete un-signs, date change).
4. [x] **backend-dotnet: importer.**
   - **Spike first:** at most 1 listing page + 2 detail pages of running.life, fetched once by hand with the project UA, to pin selectors. They are **not committed**; qa-tester writes synthetic look-alike fixtures from the observed structure.
   - Then `PoliteHttpClient`, `RobotsTxt`, `RunningLifeParser`/`Source`, `DemoRaceSource`, `RaceImportService`, `--job race-import`, validator rules, AngleSharp pin.
5. [x] **maps-gpx:** `MapView` `area` + race marker.
6. [x] **frontend-react: settings page** (+ the qa-tester fake API for races).
7. [x] **frontend-react: calendar integration and panels.** Bubbles, legend, toggle, day section, list panel, details dialog, signup flow, banners, invalidations, i18n, bundle budget.
8. [x] **azure-infra:** Bicep, grant script, `migrate.sh`, `deploy.yml`, CI full-stack env; `az bicep build/lint`, actionlint, shellcheck.
9. [x] **qa-tester: E2E** (fake API, phone, axe; full-stack journey).
10. [x] **reviewer**, then **architect** (docs, deviations). The main agent updates CLAUDE.md, commits "M8: race proposals" and stops for approval.

### 6.2 Part B: user-confirmed gates (each needs the user's explicit "yes"; same procedure as M7)

| Gate | What | Cloud change |
|---|---|---|
| **G-R1** | The user sends the permission e-mail to running.life through **the site's contact page** (outline §10). Optionally also kalendarzbiegowy, for phase 2. Record the date and outcome in §12 (no addresses). | none |
| **G-R2** | Provision with all flags **off**: `azd hooks run preprovision` → `azd provision` → `azd hooks run postprovision`. This creates `id-rd-race-import`, `AcrPull`, `job-race-import`, the alert, and the new env vars. Then the merged M8 deploy via `deploy.yml` (the migration job creates the tables and grants). **Order matters:** approve the deploy right after the provision and before the next Monday 03:25 UTC, because until the deploy the job still runs the M7 API image, which does not know `--job race-import` and would exit 1 (one failed-execution alert, harmless). A deploy approved *before* the provision also works: the grant script skips the race-import user while its identity is unset, and `deploy.yml` skips the job while it does not exist. Live check as a guest: the UI is hidden (`races.enabled=false`). | identity, job, alert, env; schema |
| **G-R3** | **Only after permission (or the user's explicit decision, Q1):** `azd env set RACE_IMPORT_ENABLED true`, `RACE_IMPORT_RUNNING_LIFE true` → provision (`--no-state` if only env changed) → **one manual run** `az containerapp job start -n job-race-import -g rg-runnersdiary-prod` → check the summary log, counts and 10 random races against the source, plus the robots line in the log. | env change, one job execution |
| **G-R4** | `azd env set RACES_UI_ENABLED true` → provision → live check: guest sees Silesian proposals; signup → planned run; the toggle hides them; axe spot check. | env change |

**Rollback / kill:** set `RACE_IMPORT_ENABLED=false` (the job exits without SQL) and/or `RACES_UI_ENABLED=false` (UI hidden), then provision. To purge one source's rows, a runbook `DELETE` is run by the migrator identity on the user's request.

## 7. Cost

See §1 and ADR 0038 §6: **≈ €0 a month**, SQL free-allowance impact ≤ 1 % (it shares the Monday wake), no geocoding, no new SKU. The $25 RG budget and the existing alerts are unchanged; one free metric alert is added.

## 8. Risks

| Risk | Impact | Mitigation |
|---|---|---|
| The operator refuses, or never answers | No production catalogue | Feature stays dark (`RACES_UI_ENABLED=false`); phase 2 source or ahotu (Q1/Q6); manual race entry still works. |
| Database right / copyright claim | Legal | Facts only, permission first, attribution + link, weekly ≤ 1 req/s, immediate kill switch, purge runbook (ADR 0035). |
| Source layout change | Import stops | Zero-events check → exit 1 → alert; fixtures make the parser fix quick; stale data is labelled by `catalogueUpdatedUtc`. |
| Community-edited data is wrong (date, place) | User signs up for a wrong date | "Always check the organiser's page" copy; signup URL prominent; `dateChanged` banner. |
| Parser bug on hostile HTML | Corrupt catalogue | AngleSharp (spec parser, no loaders), size/depth caps, job identity limited to two tables (ADR 0038 §2). |
| SSRF via URLs in source content | Internal calls | Host allow-list for fetches; stored URLs are never fetched server-side; SPA `safeUrl`. |
| SQL wake budget | Free offer exhausted | Weekly, shares the cleanup wake; disabled job opens no connection; `free_amount_remaining` alert exists. |
| The `SET NULL` cascade or its permissions are rejected | Migration/purge fails | Verified in step 2; fallback in ADR 0036. |
| Grid clutter with many proposals | Unreadable calendar | Runs first, shared `MAX_BUBBLES`, toggle, profile filters, "+n" → day panel. |
| A11y regression in the grid | Screen-reader users lose context | Proposals in the cell's accessible name; the day panel as the keyboard path; axe in CI. |
| `dateChanged` shows after the user deliberately moves a linked run | A persistent banner in the details | It is only a banner in the details plus a small badge in the list (never a modal). The copy says "differs from the race listing". If users find it noisy, an acknowledge flag is a small additive change. |
| Bundle growth | Slower first load | Lazy routes and namespace; ≤ 1.5 kB initial budget. |

## 9. Test plan outline (qa-tester)

**Domain.Tests (`Races/`, hand-computed):**
- **Normaliser:** "10 km", "10,0 km", "21,1 km", "21.0975 km", "półmaraton", "maraton", "5K", "100 mil" (→ 160,934 m), "1 km dla dzieci" dropped, junk text → no distance; kind texts (city/street → Road, terenowy/trail → Trail, górski → Mountain, ultra, OCR/przeszkody); English voivodeship names → codes (all 16, case and diacritics).
- **Hasher:** order-insensitive distances; coordinate rounding; a URL change changes the hash.
- **Merger:** each table row of ADR 0036 §2; missed scans 0 → 1 → 2 → removed; restore on reappearance; past races not counted; incomplete scan → no counter changes; date change sets `PreviousDate`.
- **Matcher:** same race with "XII Bieg" vs "Bieg … 2027" → match; 4 km apart → no match; different dates → no match; canonical = older.
- **Interest rules:** every validation edge (radius without centre, 4 km, 301 km, min > max, 17 regions, unknown code, 6 keywords, horizon 6 / 366).
- **Proposal builder:** window clamp (today, horizon, from > today); kinds incl. derived ultra; region ∪ radius; haversine edge at exactly the radius; distance overlap incl. no distances; keywords all-terms with diacritic folding (`Łódź` ~ `lodz`); dismissed hidden unless included; signed-up always and outside the horizon; ordering ties; `isNew` vs `seenUtc`; `dateChanged` both ways.
- **Run mapper:** the ADR 0036 §5 table row by row; ±1 % category edges (9,900 / 10,100 / 10,101); multi-distance without a choice → error; past race → error; result passes `RunRules`; time zone from the race vs the profile fallback.
- **Demo:** the seed parses, and ≥ 10 future PL-24 races for every "today" across a year.

**Infrastructure (fake `HttpMessageHandler`, synthetic fixtures):**
- **UA and requests:** UA header exact; requests spaced ≥ `MinInterval` (`TimeProvider` fake); config < 1 s rejected.
- **robots:** disallow honoured; 404 robots = allow; 503 robots = abort source.
- **Conditional and stop rules:** conditional headers sent; 304 keeps facts; 403/429 stop without retry; three 5xx stop; same-host redirect followed; cross-host redirect refused; body > 2 MB aborted; detail URL on a foreign host skipped.
- **Parser:** listing → facts (coordinates, region code); a detail with 3 distances; malformed JSON-LD skipped with a warning; JSON depth > 32 rejected; `<script>` / HTML in names stored as text; an empty-layout page → layout-change outcome; no description field reaches `RaceFacts` (reflection test over the record's members).

**Api.Tests (Testcontainers):**
- **Endpoints:**
  - the validation matrix per endpoint (camelCase keys);
  - `configured: false` defaults; PUT then GET round trip;
  - proposals for a fresh guest (AC5); `includeDismissed`; `truncated` with a lowered cap;
  - `getRace` 404s; duplicate → `canonicalId`; removed + interested → 200 with `removed: true`.
- **Signup and runs:**
  - signup creates a planned race run (all mapped fields), `Location` header, a second signup → 409 with `runId`, run cap → 409;
  - `updateRun` keeps `catalogRaceId`; deleting the run → the proposal is `None` again;
  - date change via a second demo seed → `dateChanged` true and the run unchanged.
- **Guests and isolation:**
  - guest cleanup removes the profile and states (and runs) of an expired guest;
  - isolation matrix (guest/guest, user/user, guest/user) for the state, profile and signup;
  - the endpoint inventory (all Auth) and the 401 sweep;
  - `client-config.races`.
- **Job mode:**
  - disabled → exit 0 and `SqlActivityInterceptor` count 0;
  - Demo source run twice → second run 0 inserts/updates; lock held by another session → exit 0;
  - layout-change fixture → exit 1;
  - least-privilege principal with only the two table grants can run the import and the purge (cascade ⚠).

**Vitest + RTL:**
- bubble rendering per state, phone dot, `MAX_BUBBLES` with runs first;
- the cell's accessible name with proposals; legend presence; the toggle's optimistic update and rollback on error;
- settings form (zod rules mirror the server, presets, region select all/clear, radius requires a place, server error mapping);
- details dialog (attribution, external link attributes via `safeUrl`, a `javascript:` URL not rendered as a link, banners, signup distance radio);
- invalidation after signup and after a run delete; i18n keys exist; `test:tz` for the today clamp label under LA and Kiritimati.

**Playwright (fake API):**
- `races.spec`: proposals visible and distinct (screenshot-free checks: `data-kind="proposal"`, dotted class, legend entry); toggle hides them and stays hidden after reload; open details from a bubble; Open signup page has the correct attributes; I signed up → the planned race bubble appears and the proposal bubble disappears; Not interested → gone, Undo → back; list panel "New" badge; settings page saves and filters (e.g. remove PL-24 → Silesian proposals gone); keyboard path cell → day panel → proposal; axe (serious/critical = fail) on the grid with proposals, the details dialog and the settings page.
- `races.phone.spec` (360×640, LA): dots, bottom-sheet details, settings usable, no horizontal scroll, axe.

**Full-stack (`e2e-fullstack/races.spec.ts`, main only):** guest → diary shows demo proposals in Silesia → details → I signed up (choose 21.1 km) → planned race run on that date in the calendar and the month list → `/insights` lists it as a planned race → delete the run → the proposal shows again.

**Manual (G-R3/G-R4):** compare 10 imported races with the source; VoiceOver over a cell with proposals; phone Safari.

## 10. Permission e-mail outline (the user sends it; G-R1)

Send it through **the site's contact page**, in Polish or English:
1. Who: a private, non-commercial runner's diary (open source, repository link), hosted in the EU.
2. What we would take: only facts of upcoming Polish races (name, date, place and coordinates, distances, type, the signup and event links, the organiser's organisation name), **never descriptions, images or personal data**.
3. How: weekly, a few dozen page requests, ≤ 1 request/s, identifying User-Agent, honouring robots.txt, conditional requests.
4. Attribution: "Source: <site>" with a link to the race page on every race, plus a sources list.
5. Control: we stop immediately and delete their data on request; a contact for that.
6. Question: "May we do this? Do you prefer a feed or an API we should use instead?"

## 11. Open questions (recommended defaults in bold)

1. **Source and permission:** **running.life as the only M8 source; import stays off until the operator agrees; no answer within 30 days = stay off and revisit**. Alternatives: go live after informing them without waiting, or choose kalendarzbiegowy first.
2. **Geocoding:** **none in M8 (source coordinates only; races without coordinates match by voivodeship only)**. For phase 2: **Azure Maps via the existing adapter with a separate job budget (≈ 60/day) so API + job stay < 5,000/month**, or Nominatim (free, but periodic scripts are limited to 4 req/min and it needs attribution), or no geocoding (region match only).
3. **Import frequency:** **weekly, Monday 03:25 UTC (shares the cleanup's SQL wake)**, or twice a week (≈ +2 % of the SQL allowance), or daily (≈ 14–18 %, not recommended).
4. **Date changes:** **banner + explicit "Move my run" button, never automatic**, or auto-move linked planned runs.
5. **Dedup:** **in M8 within-source only (cross-source ready)**, or postpone the matcher until a second source exists.
6. **International races:** **no**. Alternatives: AIMS iCal (official feed, ≈ 400 majors, low risk), or ask ahotu for partner access (cost unknown).
7. **Naming / language:** **race names, places and organisers in the source language; UI in English with `pl` keys ready**.
8. **Race facts in the AI summary:** **no** (only signed-up races, as ordinary planned runs, which already happens), or add catalogue facts to a future fact sheet version.
9. **Keeping past races:** **delete 180 days after the race date (runs keep their copy)**, or 365 days, or forever.
10. **Guests in production:** **guests see the real (attributed) catalogue for Silesia once G-R4 is done**, or guests only ever see the synthetic demo catalogue.
11. **Second source timing:** **a later milestone (M8.1) after permission**, or include kalendarzbiegowy in M8.
12. **Navigation:** **a new "Settings" nav entry with "Race interests" as its first page**, or a top-level "Races" page combining the list and interests.
13. **Default interests for new real users:** **none (they opt in on the settings page)**, or a default of the whole of Poland, road + trail, 120 days.

## 12. Implementation notes & deviations

**Backend, importer and infra implemented 2026-11-13** (§6.1 steps 1–6 and 8); **UI, E2E and review done 2026-11-14** (steps 7, 9, 10). Nothing provisioned, part B untouched. Frontend deviations: §12.2; review and fixes: §12.3; verification: §12.4; follow-ups: §13.

### 12.1 Deviations (backend, importer, infra)

| # | Plan said | Built | Why / where recorded |
|---|---|---|---|
| D1 | AngleSharp, version open (ADR 0035 §6 ⚠) | AngleSharp **1.10.2**, Infrastructure only | ADR 0035 amend. §1 |
| D2 | robots.txt 4xx = allow all | **403/429 on robots.txt = stop the source** (the normal stop rule, exit 0); other 4xx = allow all | Conservative: a refusal is not a missing file. ADR 0035 amend. §2 |
| D3 | — | A 200 detail page with no recognisable fact (no distances, links or type) is **ignored** for a race that already has facts; the stored facts stay | Layout-change guard for detail pages. ADR 0035 amend. §3 |
| D4 | Kind from the detail page's type text | Kind from the **listing card's type anchor** (`label-*` class / Polish label); detail `[data-race-type]` overrides; name/chips only as a last guess. Non-running cards (triathlon, duathlon, aquathlon, fitness, multisport) are skipped | The listing JSON-LD has no type and lists other sports. ADR 0035 amend. §4 |
| D5 | `PoliteHttpClient` over the usual client stack | Plain `HttpClient` (`AllowAutoRedirect=false`, gzip/br), **no `IHttpClientFactory` / resilience handler**; redirects hand-rolled (≤ 3, same host, https); size cap on the decompressed body; logs paths only | The standard handler retries, default logging writes full URLs. ADR 0035 amend. §5 |
| D6 | Spike pages pin the selectors | Done once by hand, **not committed**; `tests/fixtures/races/` is synthetic | As planned. ADR 0035 amend. §6 |
| D7 | `RaceDistances` replaced as a whole per hash change | EF child collection `Race.Distances` of `RaceDistance(RaceId, DistanceMeters)`; `ApplyFacts` **diffs** the rows | No needless delete/insert. ADR 0036 amend. §1 |
| D8 | `SET NULL` ⚠, NO ACTION fallback | `FK_Runs_Races_CatalogRaceId` ON DELETE SET NULL **accepted by SQL Server**; a principal with only the two table grants imports and purges with the cascades (`RaceImportJobTests`) | Fallback not needed. ADR 0036 amend. §2 |
| D9 | `CK_Races_*` only | + `CK_RaceInterestProfiles_{Center,Distance,Horizon,Kinds,Radius}`, `CK_RaceProposalStates_State` (`IN (1, 2)`), index `IX_Runs_CatalogRaceId` | Defence in depth; FK index for the SET NULL. ADR 0036 amend. §3, data model |
| D10 | Purge `Date < today − 180` | Skips canonical rows a surviving duplicate still references (self-FK NO ACTION), clears pointers among the deleted rows first | FK safety. ADR 0036 amend. §4 |
| D11 | New races read within the detail budget | A new race whose detail page was not read is **deferred** (not inserted) to a later run; first backfill ≈ 4 weekly runs at `MaxDetailPages` 150 | No listing-only rows without distances. ADR 0036 amend. §5 |
| D12 | `ToPlannedRun(race, choice, zone)` | `ToPlannedRun(race, choice, zone, today)` + `TryToPlannedRun(…, out values, out errors)` | Past-race rule in Domain; errors without exceptions. ADR 0036 amend. §6 |
| D13 | `Races:Enabled=false`: endpoints still answer (ADR 0037 §5) | The whole `/api/races` group answers **503 `feature-unavailable`** (read per request) | Same as maps token and AI. ADR 0037 amend. §1 |
| D14 | Span ≤ 92 days | Counted **inclusively** (`to − from + 1 ≤ 92`) | ADR 0037 amend. §2 |
| D15 | `state: "None" \| "Interested" \| "Dismissed"` | Enum `RaceStateChoice`; `SignedUp` or unknown → 400 on `state` | ADR 0037 amend. §3 |
| D16 | `RaceDetailsDto = RaceProposalDto & {…}` | **Flat** record with the same members | Simple OpenAPI schema. ADR 0037 amend. §4 |
| D17 | State on a removed/duplicate race unspecified | Only `None` (clear); `Interested`/`Dismissed` → 404 | ADR 0037 amend. §5 |
| D18 | Signup errors unordered | **404 → 409 `race-already-linked` → 400 → 409 `run-limit`** | ADR 0037 amend. §6 |
| D19 | Source ids, caps, freshness unspecified in config | Ids `running-life`, `demo` (demo not in `client-config`); `Races:Proposals:MaxStatesPerUser` 2,000 / `MaxStatesPerGuest` 200; `catalogueUpdatedUtc` = max `LastSeenUtc` | ADR 0037 amend. §7 |
| D20 | Index read, then crawl with no connection open (ADR 0038 §1) | The session app lock is held on **one idle connection for the whole run**, released explicitly (`sp_releaseapplock`) | A session lock dies with its connection. ADR 0038 amend. §1 |
| D21 | — | The merge runs on rows **re-read inside** the per-source transaction (the retry strategy reruns the whole attempt) | ADR 0038 amend. §2 |
| D22 | Exit 0 / 1 outcomes | Exit 1 only for layout change, robots failure, unhandled exception, SQL failure; 403/429 and 3 × 5xx stop the source with **exit 0**. **Known gap:** a persistent outage or block never alerts | ADR 0038 amend. §3 |
| D23 | Alert dimension value ⚠ | `Executions` Total, `state` = `Failed`, hourly over 24 h, > 0; **`Failed` unverified until G-R3** | ADR 0038 amend. §4 |
| D24 | `deploy.yml` updates and rolls back the job | Only **when the job exists**; skipped before G-R2 | ADR 0038 amend. §5 |
| D25 | — | `id-rd-race-import` added to the deployer's Managed Identity Operator list (`github-oidc.bicep`) | `job update` must assign the UAMI. ADR 0038 amend. §6 |
| D26 | Job env per ADR 0038 §3 | As planned (`replicaTimeout` 1,800 s); **no blob setting** (job profile needs none; test) | ADR 0038 amend. §7 |
| D27 | One bad item = (implicitly) a failed `Race.Create` in the source transaction → exit 1 | **Per-item validation** (review H1): the parser and the source check each item with `RaceFacts.TryNormalize` (the same rules as `Race.Create`) and count failures as `Rejected`; `RaceImportService` also catches `DomainRuleException` per insert/update and continues; `Rejected` is in the summary log line | One over-long name must not stop the catalogue. ADR 0035 amend. II §1, ADR 0038 amend. II §1 |

### 12.2 Deviations and notes (frontend, steps 5–7; commits `139f70f`, `f5cffca`, `409ba07`)

| # | Plan said | Built | Why / where recorded |
|---|---|---|---|
| F1 | The diary calls proposals with the month's `gridStart`/`gridEnd` (ADR 0037 §4) | Bounds computed **client-side** (`gridBounds` in `web/src/features/diary/diaryMonth.ts`, the same Monday-first rule as the API), so the proposals query runs **in parallel** with `getCalendarMonth` (not after it) and adjacent months are prefetched with their own bounds | No request waterfall. ADR 0037 amend. II §1 |
| F2 | "Its failure never blanks the calendar … the proposals area shows an inline error" | The proposals layer **never blocks the grid**; a failed request shows **no proposals and no error UI** | Proposals are optional decoration; an error banner over the diary would be noisier than the loss. ADR 0037 amend. II §2 |
| F3 | Proposals are fetched only with `showInCalendar` on (§4.6) | That rule holds for the **grid**. The **Day panel** section and the **list panel** show proposals whenever a profile exists, even with `showInCalendar` off; dismissed ones are fetched only with "Show dismissed" (`includeDismissed=true`) | The toggle hides the bubbles, not the feature. ADR 0037 amend. II §3 |
| F4 | `markRaceProposalsSeen` "on open" | Sent **after the first list response**, not on mount; the "New" badges are a **snapshot at open** (the cache is patched to `isNew: false`, no refetch) | A mount-time call could race the list and clear badges before they were seen. ADR 0037 amend. II §4 |
| F5 | — | The two M8 409 problems (`race-already-linked` with `runId`, `proposal-state-limit`) are read in `web/src/features/races/raceProblems.ts` | One place for problem `type` URNs |
| F6 | "On success, toast + navigate to `runs/:runId`" | Navigation uses **`replace`**, so Back does not reopen the signup form | |
| F7 | Actions per state unspecified | A **removed** race disables both "Open signup page" and "I signed up"; a **past or duplicate** race disables only "I signed up" | Mirrors the API's 404/400 rules |
| F8 | `ProposalsToggle` updates `showInCalendar` optimistically | It sends a **full-replace `PUT /api/races/interests`** built from the cached interests. An edit saved meanwhile in another tab could be overwritten. **Accepted** (low; review L4) | The contract is full replace by design (ADR 0037 §1) |
| F9 | — | **Escape** in race details opened from the Day panel returns to the Day panel; a second Escape returns focus to the grid cell | Keyboard path of AC6 |
| F10 | Nav "Settings" (step 6) | The label is `common:nav.settings` (the shell's eager namespace, so the lazy `races` bundle is not pulled into the initial load); region names are `races:regions.<code>.en` / `.pl`; distance presets 5K 4.5–5.5, 10K 9.5–10.5, Half 21–21.5, Marathon 42–42.5, Ultra 42.2–250 km; shadcn `checkbox` and `switch` added (the Radix base was already a dependency) | AC12 |
| F11 | `MapView` `area` + race marker (step 5) | `area` plus `markerKind` / `markerLabel`; the circle colour is a hex of `--primary` (the map styles need a literal colour); the antimeridian is **not handled** (Poland only) | |
| F12 | AC12: initial bundle grows ≤ 1.5 kB gzip | Initial load **181.8 kB gzip (186,125 B)** vs **183,068 B** after step 6, i.e. **+3.0 kB** for the grid integration. **AC12's ≤ 1.5 kB target is missed**; the overall budget (limit 201.5 kB) passes with ≈ 15 kB headroom. **Accepted** | The bubbles, legend and toggle live in the eagerly loaded month grid; the races chunks themselves are `LAZY_ONLY` |
| F13 | — | The SPA crashed when `client-config` had no `races` (an older API during a rolling deploy); fixed with optional chaining (`data?.races?.enabled`) in the E2E commit `080e3f3` | Rolling deploys mix API and web versions |

### 12.3 Review (reviewer agent, 2026-11-14) and fixes

| Finding | Severity | Outcome |
|---|---|---|
| **H1** One invalid listing item (name > 200 characters, date outside 1950–2100, URL > 2,048 after escaping) failed the whole source | High | **Fixed** (working tree, final backend commit): `RaceFacts.TryNormalize` in `RunningLifeParser` / `RunningLifeSource`; invalid items counted as `Rejected`; `RaceImportService` catches `DomainRuleException` per race and continues; a page whose items are **all invalid** is not a layout change (D27) |
| **M1** Rollout order between the G-R2 provision and the M8 deploy | Medium | **Fixed** in `aaa5cf9`: `grant-app-users.sql` skips the race-import user while `RACE_IMPORT_IDENTITY_NAME` is empty; `deploy.yml` tolerates only "not found" when reading the job's image (any other `az` error fails the step); G-R2 text in §6.2 |
| **L1** `Content-Signal` header logged unbounded | Low | **Fixed**: control characters stripped, capped at 200 characters |
| **L2** `truncated` proposals not logged | Low | **Fixed**: warning `Race proposals truncated to {Max} catalogue rows …` |
| **L3** `MaxAsync(LastSeenUtc)` scans the catalogue on every proposals call | Low | **Accepted** (≈ 1k rows); revisit if the catalogue grows (an index on `LastSeenUtc` or a cached value) |
| **L4** Toggle full-replace PUT can overwrite a concurrent edit | Low | **Accepted** (F8) |
| Nit: default port on redirects / targets | Nit | **Fixed**: `IsAllowedTarget` requires https, the source host, no user info and the **default port**; the parser's detail-URL check does the same |
| Nit: body decoding ignored the charset | Nit | **Fixed**: the declared `charset` is used when .NET knows it, else UTF-8 |
| Nit: the MapLibre attribution link inside the map has no `rel` / `referrerpolicy` | Low | **Follow-up (maps-gpx)**, §13; library-generated markup |
| Nit: the "Loading map…" placeholder fails axe colour contrast while it shows | Low | **Follow-up (frontend-react / maps-gpx)**, §13 |

### 12.4 Verification (2026-11-14)

- `dotnet build` warning-free.
- **Domain.Tests** 1,385; **Api.Tests** 823 at the last full run (Testcontainers ran; a final re-run after the last backend fixes was pending when this was written); **Ai.Tests** 765 + 6 skipped live evals.
- **Vitest** 1,070 / 1,070, green with `--maxWorkers=4`. With the default worker count, unrelated run-panel tests flake under load: a pre-existing timing sensitivity, not caused by M8 (follow-up §13).
- **Playwright** (fake API) 126 passed, 3 pre-existing skips (desktop + phone-la), incl. 18 race tests; axe clean; zero CSP violations.
- **Full stack:** `web/e2e-fullstack/races.spec.ts` passed against the AppHost stack.
- `az bicep build/lint`, actionlint, shellcheck clean; `dotnet list package --vulnerable` and `npm audit --omit=dev` clean.
- **E2E gaps** (covered by unit/integration tests only): the phone date-changed banner, the 409 on a second signup, the duplicate banner. The specs have **not yet run against the nginx web image under the real CSP**; CI `deploy-artifacts` does that on the PR.

### 12.5 Notes

- **CI/AppHost:** `Races__Enabled=true` and `Races__DemoCatalogue__SeedOnStartup=true` in `AppHost.cs` and the `e2e-fullstack` API container (`ci.yml`).
- **Done (azure-infra):** the comment above `alert-rd-race-import-failed` in `infra/modules/alerts-budget.bicep` now says hourly (`PT1H`) over 24 h, matching the value.
- **Outside M8 scope (`b6f334c`):** Aspire packages bumped to **13.6.5** for GHSA-f7vh-h5g3-7jrh; EF Core pins (`SqlServer.NetTopologySuite`, `Design`) aligned to **10.0.14**, the version Aspire 13.6.5 resolves; the `System.Security.Cryptography.Pkcs` security pin (CVE-2026-84008) dropped, since 10.0.14 no longer needs it.

## 13. Follow-ups and open items for the user (2026-11-14)

**For the user (decisions or actions):**
1. **G-R1:** send the permission e-mail to running.life (outline §10) **before** the import is enabled (G-R3). Record the date and the outcome in §12 (no addresses).
2. **Outage alerting:** should a persistent 403/429 block or a weeks-long outage of the source alert? Today it exits 0 and shows only as a stale `catalogueUpdatedUtc` and in the summary logs (D22). Options: leave it; a log-based alert on `StoppedByServer` for N consecutive runs; an alert on `catalogueUpdatedUtc` older than 21 days.
3. **Dependabot:** the open Aspire bump PR is superseded by `b6f334c` and can be closed after the merge.
4. **G-R3:** verify that the `Failed` value of the `state` dimension matches the first real execution (D23); correct the alert if not.
5. **M8.1 (optional):** kalendarzbiegowy.pl as the second source (Q11), after permission; needs geocoding (Q2).
6. **AC12:** accept the +3.0 kB initial-bundle growth (F12), or ask frontend-react to move the proposals layer into a lazy chunk.

**For agents (low, not blocking the merge):**
- **maps-gpx:** the MapLibre attribution link inside the map has no `rel="noopener noreferrer"` / `referrerpolicy` (library-generated; patch after render or via a custom attribution control).
- **frontend-react / maps-gpx:** the "Loading map…" placeholder fails axe colour contrast while it shows.
- **qa-tester:** E2E for the phone date-changed banner, the 409 on a second signup and the duplicate banner (§12.4); a Vitest case for `gridBounds` (`web/src/features/diary/diaryMonth.ts`) against the API's `gridStart`/`gridEnd` rule (F1; no direct test found on 2026-11-14).
- **qa-tester / azure-infra:** if CI's Vitest flakes in the run-panel tests, bound the workers (`--maxWorkers=4`) or raise `testTimeout` in CI.
- **backend-dotnet (only if the catalogue grows):** L3, the `MaxAsync(LastSeenUtc)` scan per proposals call.

## Assumptions

1. "Proposals" are suggestions only. The app never signs anyone up anywhere; "I signed up" records the user's own statement.
2. The catalogue is Poland-only in M8, and every race's zone is `Europe/Warsaw`.
3. Race facts are not personal data, and the organiser's *organisation* name is not personal data. Personal organiser names are filtered out heuristically and never stored deliberately.
4. Signed-up races counted as planned race runs in insights and the AI summary is desired (it follows from them being runs).
5. A linked run edited by the user stays linked. The link is informational and never syncs data back.
6. The existing general rate limiter suffices for the new endpoints (no new policy); the run caps bound signups.
7. The `api` image may contain AngleSharp even though only job mode uses it.
8. No new CI job is needed; the existing jobs pick up the new tests, and `deploy-artifacts` lints the Bicep.

## Docs checked (2026-11-13)

- Container Apps jobs: https://learn.microsoft.com/azure/container-apps/jobs (updated 2026-09-16)
- `Microsoft.App/jobs` metrics (`Executions`, dimension `state`): https://learn.microsoft.com/azure/azure-monitor/reference/supported-metrics/microsoft-app-jobs-metrics
- Azure Maps transactions (Search billing, non-billed codes; updated 2026-09-15): https://learn.microsoft.com/azure/azure-maps/understanding-azure-maps-transactions
- EF Core 10 what's new: https://learn.microsoft.com/ef/core/what-is-new/ef-core-10.0/whatsnew · `ExecuteUpdate`/`ExecuteDelete`: https://learn.microsoft.com/ef/core/saving/execute-insert-update-delete · SQL Server spatial mappings: https://learn.microsoft.com/ef/core/providers/sql-server/spatial
- Nominatim usage policy: https://operations.osmfoundation.org/policies/nominatim/
- running.life robots.txt: https://running.life/robots.txt
