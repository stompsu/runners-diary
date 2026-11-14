# 0037 – Race proposals: interest profile, filtering and ranking, per-user state and the calendar integration

**Status:** Accepted (M8 plan approved 2026-11-13; amended 2026-11-13 after the endpoints implementation and 2026-11-14 after the SPA and the review, see the end; **§5 "the endpoints still answer" is superseded by the amendment**)
**Date:** 2026-11-13
**Related:** [0010](0010-current-user-and-data-scoping.md), [0016](0016-calendar-grid-and-url-state.md) (calendar), [0025](0025-insights-report-computation-and-api.md) (on-demand computation precedent), [0036](0036-race-catalogue-model-and-race-runs.md), [API contract](../api-contract.md#planned-in-m8-race-proposals)

## Context

- A "proposal" is a catalogue race that matches the caller's interests and that they have not dismissed.
- Proposals must appear in the monthly diary, visually distinct from runs, and in a list. Each user can dismiss a race, mark it interesting, or sign up (ADR 0036 §5).
- The catalogue is small: about 600 upcoming Polish races a year now, about 2–3k with a second source. A 6-week calendar grid therefore holds at most about 100–400 catalogue rows.

## Decision

### 1. Interest profile (`RaceInterestProfiles`, one row per user, `Owner` filter)

| Field | Rule (validated in Domain `RaceInterestRules`, ADR 0012 style) | Default for a new profile |
|---|---|---|
| `kinds` | subset of `RaceKind`, ≥ 1 | `Road`, `Trail` |
| `regionCodes` | ≤ 16 known codes (Domain `PolishRegions`, ISO 3166-2 `PL-02` … `PL-32`) | `[]` |
| `center` + `radiusMeters` | both or neither; radius 5,000–300,000 m (SI in the API too; the UI edits km) | none |
| `minDistanceMeters` / `maxDistanceMeters` | each 1,000–250,000 or null; min ≤ max | 5,000 / 42,195 |
| `horizonDays` | 7–365 | 120 |
| `keywords` | ≤ 100 characters, ≤ 5 whitespace-separated terms; each term 2–30 characters | null |
| `showInCalendar` | boolean | true |

- **Location match:** with neither `regionCodes` nor `center`, the whole country of the catalogue (`PL`) matches. With both, either one matching is enough (union).
- **No profile row means proposals are off for that user.** `listRaceProposals` answers `profileConfigured: false` with no items, and the SPA invites them to set interests.
- Guests get a seeded demo profile (plan §3.2), so the demo shows proposals at once.

### 2. Filtering and ranking: pure Domain over one projection

`RaceProposalBuilder.Build(profile, today, window, races, states, linkedRuns)`, in `RunnersDiary.Domain/Races/`, unit-tested.

**SQL step** (no spatial SQL needed at this size): a projection of catalogue races with `Date` in the effective window, `RemovedUtc IS NULL`, `DuplicateOfRaceId IS NULL`, plus their distances. This is a seek on the clustered `(Date)` index with a cap of 1,000 rows; a cap hit is logged and the response says `truncated: true`. Then two Owner-filtered reads:
- the caller's states for those race ids;
- the caller's runs with `CatalogRaceId` in those ids.

**Effective window:** `[max(from, today), min(to, today + horizonDays)]` in the caller's profile zone. Signed-up races are an exception: they show even outside the horizon.

**A race is proposed when all of these hold:**
- kind matches (Ultra also by any distance > 42,195 m);
- location matches (region code in the list, or haversine distance from `center` ≤ radius; reuses the Domain geodesy from `Gpx/`);
- at least one distance lies within [min, max] (a race without distances matches only when both bounds are null);
- **every** keyword term occurs in the folded name or locality (a narrowing filter: all terms must match, not any);
- not `Dismissed` (unless `includeDismissed=true`, used by the list's "Show dismissed").

**Signed-up races are always included** (state `signedUp`), even if the profile changed since.

**Ranking** (deterministic):
- Order by date. Within a day: signed-up first, then interested, then by proximity (distance to `center`, else 0), then name, then id.
- The calendar shows at most `MAX_BUBBLES` per day after the day's runs, and "+n" for the rest; the list shows everything.
- **No score is exposed.** A "recommended" relevance score is deferred until there is feedback data.

### 3. Per-user state (`RaceProposalStates`, key `(UserId, RaceId)`, `Owner` filter)

- **Stored states:** `Interested = 1`, `Dismissed = 2`, plus `RaceDateWhenMarked date` (for `dateChanged`, ADR 0036 §3) and `UpdatedAt`. Setting `None` deletes the row.
- **`signedUp` is derived**, from a run with `CatalogRaceId = race.Id` (ADR 0036 §5). It is never stored.
- **"New since last visit"** uses a single `RaceInterestProfiles.ProposalsSeenUtc` timestamp. A proposal is `isNew` when `race.ImportedUtc > ProposalsSeenUtc`. The SPA calls `markRaceProposalsSeen` when the user opens the proposals list.
  - This gives **no per-race "seen" rows** and no writes on read.
  - The badge is coarse (per user, not per race), which is enough for a weekly import.
- **Cap:** ≤ 2,000 state rows per user, ≤ 200 per guest (409 `proposal-state-limit`). Rows of purged races cascade away.

### 4. API shape and the calendar: a separate range endpoint, not part of `getCalendarMonth`

`GET /api/races/proposals?from&to&includeDismissed` (span ≤ 92 days, no paging; the window and the 1,000-row cap bound it). The diary calls it with the month's `gridStart`/`gridEnd`, alongside `getCalendarMonth`.

**Why not extend `getCalendarMonth`:**
- `getCalendarMonth` is the hottest endpoint and is consumed by tests, the fake API and M5/M6 expectations. Proposals would add a second data source (the shared catalogue), a profile dependency and a user toggle to it.
- A separate query can be **skipped entirely** when the toggle is off or no profile exists (no catalogue read).
- It can be cached longer (`staleTime` 5 min; the catalogue changes weekly) and is invalidated by different events (profile/state writes) than run writes.
- Its failure never blanks the calendar: runs render, and the proposals area shows an inline error.
- **Cost:** one more request per month view. Both requests are served from an SQL that is already awake. With the toggle on, prev/next prefetch also prefetches proposals.

**Other endpoints:**
- `getRaceInterests` / `updateRaceInterests` (PUT, full replace);
- `getRace` (details by id; 404 for unknown, removed-and-unrelated or purged; a duplicate id answers 200 with `canonicalId` set, and the SPA navigates there);
- `setRaceProposalState` (PUT `{ state }`);
- `markRaceProposalsSeen` (POST, 204);
- `signUpForRace` (POST → 201 `RunDto`).

Full table: [api-contract.md](../api-contract.md#planned-in-m8-race-proposals). Everything is **Auth** (users and guests) and on the general rate limiter. The catalogue reads carry no user data. Every user-row read and write goes through the `Owner` filter, and no request carries a user id.

### 5. Feature flag and attribution

`client-config.races = { enabled: boolean; sources: { id, name, url }[] }`, from `Races:Enabled`. Default: `true` in Development/Testing, **`false` in Production until gate G-R4**.
- With `enabled: false` the SPA hides the nav entry, the toggle and the bubbles.
- The endpoints still answer (an empty catalogue in production before import), so the flag needs no API branching.

### 6. AI summary

**Race facts never enter the M6 fact sheet** (default, plan Q8). The fact sheet already covers signed-up races as ordinary planned race runs (name, date, distance category), because they *are* runs. Catalogue-only facts (unsigned proposals, prices, organisers) stay out. This also respects the sources' `ai-train=no` signals.

## Consequences

- No new indexes beyond the clustered `(Date)` on `Races` and the filtered `(UserId, CatalogRaceId)` on `Runs`. No spatial index and no stored proposals. Each request makes 3 small queries.
- Ranking and filtering are exhaustively unit-testable without SQL (like M5's `InsightsReportBuilder`).
- When the catalogue grows past the 1,000-row cap per window (about > 8k races a year), SQL pre-filtering by region or a bounding box becomes necessary; `truncated` makes that visible.
- A profile change takes effect on the next request; nothing is precomputed.

## Alternatives considered

- **Proposals embedded in `CalendarMonthResponse`:** one request, but it couples the hottest contract to a feature flag and a profile, cannot be skipped when hidden, and shares one cache lifetime. Rejected.
- **Materialised per-user proposal rows built by the job:** fast reads, but O(users × races) rows, stale on every profile edit, and the job would write user data across users (it would bypass ADR 0010). Rejected.
- **Spatial `STDistance` filter in SQL:** correct, but needless at ≤ 400 rows per window, and it puts logic outside the Domain. Revisit only if `truncated` appears.
- **A per-race "seen" state:** precise "new" badges, but writes on every list view. Rejected for one timestamp.
- **A relevance score in the API:** premature without feedback, and it invites a "why this score" UI. Deferred.

## Amendment (2026-11-13, implementation)

The decision stands except for §5's "endpoints still answer". As built in `Api/Features/Races/` and `Infrastructure/Races/` (commit `4a07bf1`; plan §12 D13–D19; contract: [api-contract.md](../api-contract.md#added-in-m8-race-proposals)):
1. **Disabled feature (§5).** With `Races:Enabled=false` (read per request) the whole `/api/races` group answers **503 `urn:runnersdiary:problem:feature-unavailable`**, like `GET /api/maps/token` and AI generation. `client-config.races.enabled=false` keeps the SPA from calling them, so the 503 is only seen by stale or direct clients. This replaces "the endpoints still answer".
2. **Window (§4).** The 92-day span is counted **inclusively** (`to − from + 1 ≤ 92`). `catalogueUpdatedUtc` is the max `LastSeenUtc` over the whole catalogue, returned even without a profile.
3. **State request.** `SetRaceProposalStateRequest.state` is the enum **`RaceStateChoice`** (`None | Interested | Dismissed`), separate from the response enum `ProposalState`; `SignedUp` or an unknown value is a 400 on `state`.
4. **Details shape.** `RaceDetailsDto` is a **flat** record with every `RaceProposalDto` member plus the extras (no `allOf` composition in the OpenAPI document).
5. **Removed or duplicate races.** `setRaceProposalState` accepts only `None` (clearing a mark) for them; `Interested`/`Dismissed` → 404. `getRace` answers 404 for a removed race unless the caller is Interested or signed up, and 200 with `canonicalId` for a duplicate (as in §4).
6. **Signup answer order.** 404 (unknown, removed or duplicate) → 409 `race-already-linked` (with `runId`) → 400 (mapper errors) → 409 `run-limit`. A concurrent second signup that loses the unique index also answers 409 `race-already-linked`.
7. **Ids and options.** Source ids are `running-life` and `demo`; `client-config.races.sources` lists real sources only (demo is omitted). The state caps are options `Races:Proposals:MaxStatesPerUser` (2,000) and `Races:Proposals:MaxStatesPerGuest` (200).

## Amendment II (2026-11-14, SPA and review)

The decision stands. As built in the SPA (commit `409ba07`; plan §12.2 F1–F4, F8, F13) and after the review (plan §12.3):
1. **Grid bounds (§4).** The SPA computes the month's grid bounds itself (`gridBounds` in `web/src/features/diary/diaryMonth.ts`, the same Monday-first rule as `getCalendarMonth`'s `gridStart`/`gridEnd`), so `listRaceProposals` runs **in parallel** with `getCalendarMonth` and adjacent months are prefetched with their own bounds. A rule change on either side must change both. No unit test pins `gridBounds` yet (follow-up for qa-tester, plan §13).
2. **Failure (§4).** "The proposals area shows an inline error" is replaced: the proposals layer **never blocks the grid**, and a failed request simply shows **no proposals and no error UI**.
3. **Where proposals show (§4, §5).** The `showInCalendar` toggle gates the **grid bubbles** only. The Day panel's "Race proposals" section and the list panel show proposals whenever a profile exists; dismissed ones are fetched only with "Show dismissed" (`includeDismissed=true`).
4. **"Seen" (§3).** `markRaceProposalsSeen` is sent after the list panel's **first response**, not on mount; the "New" badges stay as they were when the panel opened.
5. **Toggle writes (§1).** The toggle sends the full-replace `PUT /api/races/interests` from the cached interests; an edit saved meanwhile elsewhere can be overwritten. Accepted (review L4).
6. **Freshness query.** `catalogueUpdatedUtc` (max `LastSeenUtc`) scans the catalogue on every proposals call. Accepted at ≈ 1k rows (review L3); revisit with an index or a cached value if the catalogue grows.
7. **`client-config.races` is optional for clients.** The SPA treats a missing `races` member (an older API during a rolling deploy) as disabled.

## References (checked 2026-11-13)

- EF Core 10 parameterized collections (`Contains` over id lists → scalar parameters with padding): https://learn.microsoft.com/ef/core/what-is-new/ef-core-10.0/whatsnew
- ADR 0025 (on-demand computation with projections, no server cache) is the precedent for §2.
