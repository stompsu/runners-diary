# 0036 – Race catalogue model, upsert/dedup/removal rules, and turning a proposal into a planned race run

**Status:** Accepted (M8 plan approved 2026-11-13; amended 2026-11-13 after the implementation, see the end; the ⚠ cascade check in the references is resolved there)
**Date:** 2026-11-13
**Related:** [0008](0008-run-date-and-time-storage.md) (dates), [0010](0010-current-user-and-data-scoping.md) (scoping), [0011](0011-guest-accounts-lifecycle.md) (guests), [0013](0013-persistence-model-and-migrations.md) (mapping), [0032](0032-migrations-and-jobs-in-azure.md) (expand rule), [0035](0035-race-catalogue-sources-and-crawling-policy.md) (sources), [data model](../data-model.md#planned-in-m8-race-proposals)

## Context

- The catalogue is **shared reference data**: one row per race, written only by the import job and read by every user.
- Per-user data (interests, dismissals) must stay under the `Owner` filter.
- A user who signs up must get an ordinary **planned run of a race type**: the calendar, insights, AI summary and GPX upload already work on runs.
- Sources change: dates move, events disappear, and the same event can appear twice (community duplicates now, and a second source later).

## Decision

### 1. Catalogue tables (no `UserId`, **no `Owner` filter**: a documented exception like `GeocodeUsage`)

**`Races`** (full column list in the data model). Its parts:
- **Identity:**
  - natural key `(Source tinyint, ExternalId varchar(100))`, unique;
  - `Id` = `Guid.CreateVersion7()`, the PK, non-clustered;
  - **clustered index `(Date)`** (hot query: a date window).
- **Facts:**
  - `Name`, `Date date`, `StartTime time(0) NULL`;
  - `TimeZoneId varchar(64) NULL`: IANA, derived from the country (`PL` → `Europe/Warsaw`); NULL when unknown;
  - `LocationName`, `Point geography NULL` (same NTS converter as `Runs.Location_Point`);
  - `CountryCode char(2)`, `RegionCode varchar(6) NULL` (ISO 3166-2, e.g. `PL-24` for śląskie);
  - `Kind tinyint`, `Surface tinyint NULL` (the existing `Surface` enum), `ElevationGainMeters float NULL`;
  - `SignupUrl`, `EventUrl`, `SourceUrl`;
  - `PriceAmount decimal(9,2) NULL` + `PriceCurrency char(3) NULL`;
  - `SignupDeadline date NULL`, `OrganizerName nvarchar(150) NULL`.
- **Import bookkeeping:**
  - `FactsHash binary(32)`, `ImportedUtc`, `LastSeenUtc`, `UpdatedUtc`;
  - `PreviousDate date NULL` + `DateChangedUtc datetime2(0) NULL`;
  - `MissedScans tinyint`, `RemovedUtc datetime2(0) NULL`;
  - `DuplicateOfRaceId uniqueidentifier NULL` (self-FK, NO ACTION);
  - `DetailETag`, `DetailLastModified`, `DetailFetchedUtc`.

**`RaceDistances`** is a child table `(RaceId, DistanceMeters)` PK with FK → `Races` ON DELETE CASCADE, at most 12 rows per race.
- **A child table, not a JSON column:** the sets are small; EF 10 maps them as an ordinary one-to-many; and it needs no dependency on the `json` type / `UseAzureSql` switch (EF 10 would otherwise migrate JSON columns to `json`).
- The domain still sees `IReadOnlyList<double>`.

**`RaceKind`** (`tinyint`, a string in the API): `Road = 0`, `Trail = 1`, `Mountain = 2`, `Ultra = 3`, `CrossCountry = 4`, `Ocr = 5`, `Other = 6`.
- "Ultra" is also **derived**: a race with any distance > 42,195 m matches an `Ultra` interest whatever its `Kind`.

Domain entity: `RunnersDiary.Domain/Races/Race` (factory + `ApplyFacts`). It is **not** `IUserOwned`, so the ownership guard ignores it.
- The API context only reads it (handlers never call `Add`/`Update` on `Races`; reviewer check).
- Writes come only from the import job's anonymous context ([0038](0038-race-import-job.md)).

### 2. Idempotent upsert per source (Domain `RaceCatalogueMerger`, pure; Infrastructure applies the result)

Input: the normalised `RaceFacts` of one run, plus `scanComplete` (true only when every listing page was fetched and parsed). For each fact, keyed by `(Source, ExternalId)`:

| Case | Action |
|---|---|
| New | Insert. `ImportedUtc = LastSeenUtc = UpdatedUtc = now`. |
| Same `FactsHash` | `LastSeenUtc = now`, `MissedScans = 0`, `RemovedUtc = NULL` (a reappearance is restored). |
| Hash differs | Update facts and distances; `UpdatedUtc = now`. If `Date` changed: `PreviousDate = old date`, `DateChangedUtc = now`. |

- `FactsHash` = SHA-256 over the canonical JSON of the normalised facts (sorted distances, rounded coordinates at 6 decimals, URLs as stored).
- **Disappearance:** after a **complete** scan, every non-removed race of that source **dated after today (Europe/Warsaw)** that was not seen gets `MissedScans + 1`.
  - At `MissedScans ≥ 2` (`RaceImport:RemoveAfterMissedScans`) it gets `RemovedUtc = now`.
  - Races dated today or earlier are never counted, because listings drop past events naturally.
  - An incomplete or aborted scan changes no counters.
- **Retention:** the job deletes races with `Date < today − 180 days` (`RaceImport:RetainPastDays`) at the end of a successful run (Q9).
  - Distances and proposal states cascade.
  - Runs linked to a purged race keep all their own data; their link becomes NULL (§5).
- All writes of a run happen in **one transaction** per source after the crawl, so readers never see a half-applied snapshot. A run never deletes a race of another source.

### 3. Date changes and removal, as the user sees them

- The catalogue simply holds the new date, so proposals move in the calendar.
- A linked planned run is **never moved automatically**: the run is the user's data.
- Instead `RaceProposalDto.dateChanged = true` when the user's linked run's `Date ≠ race.Date`, or when an `Interested` state's `RaceDateWhenMarked ≠ race.Date`.
- The details view then shows "The date changed from {old} to {new}" with an explicit **"Move my run to {new}"** button. It uses the existing `PUT /api/runs/{id}` with the full run, so it needs no new endpoint and no concurrency exception.
- A removed race (`RemovedUtc` set) disappears from proposals. Its details still load for users who are interested or linked, with a banner "No longer listed by the source; check with the organiser".

### 4. Matching and dedup across (and within) sources (Domain `RaceDuplicateMatcher`, pure)

- **Candidates:** same `Date`, and the points ≤ 3 km apart (haversine, reusing `Gpx` geodesy); when a point is missing, the same normalised locality.
- **Match** when the name similarity is ≥ 0.8. The measure is token-set Jaccard on the folded name: lowercase, diacritics removed (`ł` → `l`), and noise tokens dropped (`bieg`, `run`, `edycja`, roman/ordinal edition numbers, years).
- **Canonical row:** the older row (smaller `ImportedUtc`, then source priority) stays canonical; the other gets `DuplicateOfRaceId`. Proposals and details hide duplicates (details redirect to the canonical id).
- **The matcher runs inside the import transaction** over the upserted races and the catalogue rows on the same dates. In M8 it catches community duplicates within running.life; with a second source it merges across sources. Merging never deletes; a split later just clears the column.

### 5. A proposal becomes a planned race run (`POST /api/races/{raceId}/signup`)

The link lives **on the run**: new column **`Runs.CatalogRaceId uniqueidentifier NULL`**, FK → `Races.Id` **ON DELETE SET NULL**, with filtered unique index `UX_Runs_User_CatalogRace (UserId, CatalogRaceId) WHERE CatalogRaceId IS NOT NULL` (one linked run per race per user → 409 `race-already-linked`).
- **Why on the run, not on a per-user state row:**
  - deleting the run removes the link with no extra code;
  - a purged race leaves a valid run;
  - "signed up" is **derived** from the run's existence, with one source of truth;
  - it avoids a second cascade path `Users → RaceProposalStates → Runs`, which SQL Server rejects.
- The column is set **only** by the signup endpoint. `PUT /api/runs/{id}` keeps it, because `RunWriteRequest` has no such member and `Run.Update` does not touch it. `RunDto` exposes it read-only as `catalogRaceId`.

**Mapping** (Domain `RaceRunMapper`, pure; produces `RunValues` that pass `RunRules`):

| Run field | Value |
|---|---|
| `Status` | `Planned` |
| `Date` | `race.Date` |
| `StartTime` | request `startTime` ?? `race.StartTime` ?? null |
| `TimeZoneId` | `race.TimeZoneId` ?? the caller's profile zone. This is the race's own zone, so a race abroad keeps local time (ADR 0008 travel rule). |
| `RunTypeId` | request `runTypeId` (must be Race category) ?? by kind: `Road`/`Other` → 1 `roadRace`; `Trail`/`Mountain`/`Ocr` → 2 `trailRace`; `CrossCountry` → 5 `crossCountry`; `Ultra`, or chosen distance > 42,195 m → 3 `ultra` |
| `DistanceMeters` | the chosen distance (required in the request when the race lists > 1; must equal one of them; optional when exactly one; null when none) |
| `Race.Name` | `race.Name`, cut to 150 characters |
| `Race.DistanceCategory` | within ±1 % of 5,000 → `FiveK`; 10,000 → `TenK`; 21,097.5 → `HalfMarathon`; 42,195 → `Marathon`; else `Other` |
| `Race.OfficialDistanceMeters` | the chosen distance |
| `Race.TargetTimeSeconds` | request (optional) |
| `Surface` | `Road` → `Road`; `Trail`/`Mountain`/`CrossCountry`/`Ocr` → `Trail`; `Ultra` → `race.Surface` ?? null |
| `ElevationGainMeters` | `race.ElevationGainMeters` (planned target) |
| `Location` | `race.Point` + `LocationName` (when a point exists) |
| `Title`, `Notes`, other fields | null; the user edits them later in the normal form |

**Rejected with 400:** a race dated before the caller's today (`errors.raceId`), and a distance not offered (`errors.distanceMeters`).
**Other checks:** removed or duplicate races → 404; run caps → 409 `run-limit`, as for `createRun`.
**After signup:** the response is the new `RunDto` (201, `Location: /api/runs/{id}`). The proposal's state row (if any) is left as is; `signedUp` wins in the DTO.

### 6. Guests

The catalogue is shared and read-only for everyone. Interest profiles and proposal states are user rows with FK → `Users` **ON DELETE CASCADE**, and the linked runs cascade with the user as before. The guest cleanup's `ExecuteDelete` on `Users` therefore **removes them without code changes**. The **only** cleanup addition is a test asserting that no `RaceInterestProfiles` or `RaceProposalStates` rows outlive an expired guest. `DemoDataSeeder` writes a demo interest profile for every new guest (plan §3.2).

## Consequences

- One additive migration (`RaceCatalogue`: three new tables plus one nullable column and a filtered index on `Runs`). The previous API release ignores all of it, so it follows the ADR 0032 expand rule.
- Signed-up races join every existing feature (calendar, insights, AI summary, GPX course upload) with zero changes there.
- A user who edits the race name or date of a linked run keeps the link. `dateChanged` then reflects their own edit; this is accepted and documented in the UI copy ("differs from the race listing").
- The catalogue has no per-user rows, so a large catalogue costs one shared copy. Proposals are computed per request (ADR [0037](0037-race-proposals-filtering-and-state.md)).

## Alternatives considered

- **`RaceProposalStates.RunId` (link on the state row):** needs either a second cascade path (rejected by SQL Server) or application code that clears it on every run delete. It also duplicates the truth. Rejected.
- **Copy every catalogue race into each user's runs as "proposed" runs:** pollutes runs, insights and caps, multiplies rows by users and needs a sync. Rejected.
- **Distances as an EF 10 JSON complex collection:** fewer tables, but it couples to the `json` column type and to JSON querying for the distance filter. The child table is simpler to index and test.
- **Hard delete when a race disappears:** loses the race under users' interest flags because of one bad scan. Soft removal with a two-scan threshold is safer.
- **Automatically moving linked runs on a date change:** silently changes user data and can break their week plan or totals. Rejected in favour of an explicit button.

## Amendment (2026-11-13, implementation)

The decision stands. As built (commits `07f9edf`, `6b5cffe`, `4a07bf1`, `c4dbc61`; migration `20261113165157_RaceCatalogue`; plan §12 D7–D12):
1. **Distances (§1).** `Race.Distances` is an EF child collection of `RaceDistance(RaceId, DistanceMeters)` (the domain still exposes `DistancesMeters` as an ascending list). `Race.ApplyFacts` **diffs** the rows (keeps unchanged ones, removes missing ones, adds new ones) instead of replacing the set, so an unchanged distance issues no delete/insert.
2. **Cascade check resolved (§5, references ⚠).** SQL Server accepts `FK_Runs_Races_CatalogRaceId` **ON DELETE SET NULL** next to the existing cascades. An Api.Tests case running as a principal with only the `Races`/`RaceDistances` grants imports and purges, and the engine performs the `RaceProposalStates` cascade and the `Runs` SET NULL. **The NO ACTION fallback is not needed.**
3. **Extra constraints and index.** Beyond the plan: `CK_RaceInterestProfiles_Center` (centre and radius together; no name without a centre), `_Distance` (1,000–250,000, min ≤ max), `_Horizon` (7–365), `_Kinds` (1–127), `_Radius` (5,000–300,000); `CK_RaceProposalStates_State` (`State IN (1, 2)`); and the non-unique FK index `IX_Runs_CatalogRaceId` (serves the SET NULL on purge).
4. **Purge (§2 retention).** Because `DuplicateOfRaceId` is NO ACTION, the purge **skips** a doomed canonical race that a surviving row still references (repeated until stable) and **clears** `DuplicateOfRaceId` among the rows it deletes before deleting them in chunks of 500.
5. **New races without a detail page.** A new race whose detail page was not read in a run (detail budget spent, fetch failed) is **deferred**: it is not inserted, and a later run picks it up. With `MaxDetailPages` 150 and ≈ 600 listed races, the first backfill completes in **≈ 4 weekly runs**. Known races keep their stored facts with the listing facts refreshed.
6. **Mapper (§5).** `RaceRunMapper.ToPlannedRun(race, choice, profileTimeZoneId, today)` takes `today` (the past-race rule lives in Domain), and `TryToPlannedRun(…, out RunValues, out errors)` returns the 400 errors without exceptions; the signup service uses the latter.

## References (checked 2026-11-13)

- EF Core 10 what's new (JSON complex types and the `json` type default with `UseAzureSql`, `ExecuteUpdateAsync` with a regular lambda, named query filters): https://learn.microsoft.com/ef/core/what-is-new/ef-core-10.0/whatsnew
- EF Core `ExecuteUpdate`/`ExecuteDelete` (no change tracking, no implicit transaction, rows-affected for concurrency; updated 2026-06-24): https://learn.microsoft.com/ef/core/saving/execute-insert-update-delete
- EF Core SQL Server spatial mappings (`Distance` → `STDistance`, `IsWithinDistance`; geography in metres): https://learn.microsoft.com/ef/core/providers/sql-server/spatial
- ISO 3166-2:PL subdivision codes (PL-02 … PL-32): https://www.iso.org/obp/ui/#iso:code:3166:PL (⚠ not re-fetched; the list is fixed in a Domain table with a unit test)
- ⚠ To verify in the migration: SQL Server accepts the `Runs → Races` `ON DELETE SET NULL` FK next to the existing cascades (no multiple-cascade-path error), and a delete by the job's identity performs the `SET NULL` on `Runs` without an `UPDATE` grant on `Runs`. Fallback: NO ACTION, with the retention purge skipping races that still have linked runs (the job then needs `SELECT` on `Runs(CatalogRaceId)` only).
