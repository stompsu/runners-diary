# Runners Diary – Data model

Living document, owned by the `architect` agent. Status: **M2 implemented (migration `InitialSchema`); M4 added migration `GpxTrackProcessing`; M5 (Insights) had no schema change (all merged); M6 added migration `20261104070212_AiSummaries` (tables `AiSummaries`, `AiGenerations`; merged); M7 Part A adds migration `20261104153828_GeocodeUsage` (table `GeocodeUsage`; implemented, awaiting approval)**. **M8 (race proposals) adds migration `20261113165157_RaceCatalogue` (tables `Races`, `RaceDistances`, `RaceInterestProfiles`, `RaceProposalStates`, column `Runs.CatalogRaceId`; part A implemented on `m8-race-proposals`, UI done; awaiting approval/merge; not deployed): see [Added in M8](#added-in-m8-race-proposals).**
Decisions: [ADR 0008](adr/0008-run-date-and-time-storage.md) (date/time), [0010](adr/0010-current-user-and-data-scoping.md) (scoping), [0011](adr/0011-guest-accounts-lifecycle.md) (guests), [0013](adr/0013-persistence-model-and-migrations.md) (mapping, migrations).

Conventions: SI units (metres, seconds, metres of elevation); enums stored as `tinyint` and exposed as strings in the API; audit timestamps `datetime2(0)` UTC; GUID keys are `Guid.CreateVersion7()` assigned in the domain factories (time-ordered, known before `SaveChanges`; EF `ValueGeneratedNever`); every user-owned row carries `UserId` and is filtered by the `Owner` query filter.

## Entity overview

```
Users 1 ──< Runs >── 1 RunTypes (catalog, seeded)
  │           │  └─ (complex, same row) Location?, Race?, Rating?
  │           └──< GpxTracks  (0..1 per Kind; blobs = original GPX + processed GeoJSON)
  └──< AiSummaries  (M6; one per period + compare + language)

AiGenerations  (M6; system usage ledger: UserId without FK, no Owner filter)
GeocodeUsage   (M7; system counter per UTC day: no user id, no Owner filter)
```

## Users

| Column | SQL type | Null | Notes |
|---|---|---|---|
| `Id` | `uniqueidentifier` | no | PK (clustered). Internal user id; the only owner key. |
| `Kind` | `tinyint` | no | `User = 0`, `Guest = 1`. |
| `ExternalTenantId` | `uniqueidentifier` | yes | Entra `tid`. NULL for guests. |
| `ExternalObjectId` | `uniqueidentifier` | yes | Entra `oid`. NULL for guests. |
| `DisplayName` | `nvarchar(100)` | yes | From `name` claim if present; editable. No e-mail stored. |
| `TimeZoneId` | `varchar(64)` | no | IANA id. Entra users: `Etc/UTC` until the SPA sets the browser zone (M3); guests: `Europe/Warsaw`. |
| `TimeZoneIsDefault` | `bit` | no | `1` until the user/SPA sets a zone explicitly. |
| `UnitSystem` | `tinyint` | no | `Metric = 0` (only option in the UI for now), `Imperial = 1` reserved. |
| `CreatedAt` | `datetime2(0)` | no | UTC. |
| `ExpiresAt` | `datetime2(0)` | yes | Guests only: `CreatedAt + 24 h`; equals the guest JWT `exp`. |

Indexes: `UX_Users_External (ExternalTenantId, ExternalObjectId) WHERE ExternalObjectId IS NOT NULL` (unique, filtered) · `IX_Users_Guest_ExpiresAt (ExpiresAt) WHERE Kind = 1` (cleanup + active-guest count).
Check `CK_Users_Kind`: `Kind = 1` ⇔ `ExpiresAt IS NOT NULL` ⇔ external ids NULL (and `Kind = 0` ⇔ both external ids set).

## RunTypes (reference data, `HasData` from `RunTypeCatalog`)

| Column | SQL type | Notes |
|---|---|---|
| `Id` | `smallint` | PK, stable, never reused. |
| `Code` | `varchar(40)` | Unique; i18n key on the frontend (`runTypes.<code>`). |
| `Category` | `tinyint` | `Race = 0`, `Training = 1`, `Other = 2`. |
| `CountsTowardRunningDistance` | `bit` | False for cross-training and rest. |
| `CountsTowardLoad` | `bit` | True for everything except rest. |
| `IsRestDay` | `bit` | Rest/injury: note only, no metrics allowed. |
| `SortOrder` | `smallint` | UI order. |

Seed (ids are contracts):

| Id | Code | Category | Running distance | Load |
|---|---|---|---|---|
| 1 | `roadRace` | Race | ✓ | ✓ |
| 2 | `trailRace` | Race | ✓ | ✓ |
| 3 | `ultra` | Race | ✓ | ✓ |
| 4 | `trackRace` | Race | ✓ | ✓ |
| 5 | `crossCountry` | Race | ✓ | ✓ |
| 6 | `virtualRace` | Race | ✓ | ✓ |
| 20 | `easy` | Training | ✓ | ✓ |
| 21 | `recovery` | Training | ✓ | ✓ |
| 22 | `long` | Training | ✓ | ✓ |
| 23 | `tempo` | Training | ✓ | ✓ |
| 24 | `intervals` | Training | ✓ | ✓ |
| 25 | `fartlek` | Training | ✓ | ✓ |
| 26 | `hillRepeats` | Training | ✓ | ✓ |
| 27 | `progression` | Training | ✓ | ✓ |
| 28 | `racePace` | Training | ✓ | ✓ |
| 29 | `stridesDrills` | Training | ✓ | ✓ |
| 30 | `trailTraining` | Training | ✓ | ✓ |
| 31 | `treadmill` | Training | ✓ | ✓ |
| 40 | `crossTraining` | Other | — | ✓ |
| 41 | `rest` | Other | — | — (rest day) |

The brief's road-race sub-types (5K, 10K, half, marathon, other) are `Race.DistanceCategory`, not separate run types. Ultra road vs trail is `Surface`.

## Runs

| Column | SQL type | Null | Notes |
|---|---|---|---|
| `Id` | `uniqueidentifier` | no | PK, **non-clustered**. |
| `UserId` | `uniqueidentifier` | no | FK → `Users.Id` **ON DELETE CASCADE**. Owner filter. |
| `Date` | `date` | no | ADR 0008: source of truth for calendar, weeks, metrics. |
| `StartTime` | `time(0)` | yes | Local wall-clock time. |
| `TimeZoneId` | `varchar(64)` | no | IANA. |
| `StartUtc` | `datetime2(0)` | yes | Derived in Domain (DST rules); NULL when `StartTime` is NULL. Ordering/GPX only. |
| `Status` | `tinyint` | no | `Planned = 0`, `Completed = 1`. |
| `RunTypeId` | `smallint` | no | FK → `RunTypes.Id` (NO ACTION). |
| `Title` | `nvarchar(120)` | yes | |
| `Notes` | `nvarchar(4000)` | yes | |
| `DistanceMeters` | `float` | yes | > 0, ≤ 1 000 000. Planned = target. |
| `DurationSeconds` | `int` | yes | 1 … 604 800. Planned = target. Pace is derived (UI), never stored. |
| `ElevationGainMeters` | `float` | yes | 0 … 100 000. |
| `ElevationLossMeters` | `float` | yes | 0 … 100 000. |
| `AvgHeartRate` | `smallint` | yes | 30 … 250 bpm. |
| `MaxHeartRate` | `smallint` | yes | 30 … 250, ≥ avg. |
| `Rpe` | `tinyint` | yes | 1 … 10. |
| `Gear` | `nvarchar(100)` | yes | Free text (a `Gear` entity can come later). |
| `Weather` | `nvarchar(200)` | yes | Free text. |
| `Surface` | `tinyint` | yes | `Road`, `Trail`, `Track`, `Treadmill`, `Mixed`. |
| `Location_Point` | `geography` | yes | Complex `Location?` (required member). SRID 4326. |
| `Location_Name` | `nvarchar(200)` | yes | Display name from picker/geocoder. |
| `Race_Name` | `nvarchar(150)` | yes | Complex `Race?` (required member). Only for Race-category types. |
| `Race_DistanceCategory` | `tinyint` | yes | `FiveK`, `TenK`, `HalfMarathon`, `Marathon`, `Other`. |
| `Race_OfficialDistanceMeters` | `float` | yes | For `Other` / ultras. |
| `Race_BibNumber` | `nvarchar(20)` | yes | |
| `Race_TargetTimeSeconds` | `int` | yes | |
| `Race_ResultTimeSeconds` | `int` | yes | Completed only. |
| `Race_OverallPlace` | `int` | yes | Completed only. |
| `Race_CategoryPlace` | `int` | yes | Completed only. |
| `Race_CategoryName` | `nvarchar(50)` | yes | e.g. `M35`. |
| `Race_ResultsUrl` | `varchar(2048)` | yes | Absolute `http(s)` only. |
| `Rating_Stars` | `tinyint` | yes | Complex `Rating?` (required member). 1 … 5; **Completed only**. |
| `Rating_Comment` | `nvarchar(500)` | yes | |
| `CreatedAt` / `UpdatedAt` | `datetime2(0)` | no | UTC. |
| `RowVersion` | `rowversion` | no | Concurrency token → API `version`. |

Indexes/keys: **`CX_Runs_User_Date (UserId, Date)` clustered, non-unique** (calendar, lists, weekly/rolling aggregates) · `PK_Runs (Id)` non-clustered · **`AK_Runs_Id_UserId (Id, UserId)`** unique constraint (principal key of the `GpxTracks` ownership FK, see below) · `IX_Runs_RunTypeId`.
Location: the domain `GeoPoint` is mapped to an NTS `Point` (X = longitude, Y = latitude, SRID 4326) by a value converter inside the optional complex type — verified in M2, no fallback mapping.
Volume caps (application-enforced, `RunLimits`): ≤ 500 runs per guest, ≤ 20 000 per user, ≤ 20 per date.
Domain rules (in `RunRules`, see api-contract.md): rating and race result fields only when `Completed`; `Race` only for Race-category types; rest days carry no metrics; `MaxHeartRate ≥ AvgHeartRate`; `TimeZoneId` must be a valid IANA id (Windows ids rejected).

## GpxTracks

M2 created the table (metadata + raw blob reference); M4 migration `GpxTrackProcessing` added the processed summary ([ADR 0021 §7](adr/0021-track-metrics-simplification-and-geojson.md)).

| Column | SQL type | Null | Notes |
|---|---|---|---|
| `Id` | `uniqueidentifier` | no | PK. |
| `RunId` | `uniqueidentifier` | no | Part of the composite FK `(RunId, UserId)` → `Runs(Id, UserId)` ON DELETE CASCADE. |
| `UserId` | `uniqueidentifier` | no | Denormalized owner (filter + blob path). Part of the composite FK to `Runs`; **no FK to `Users`** (avoids multiple cascade paths). |
| `Kind` | `tinyint` | no | `PlannedCourse = 0`, `RecordedTrack = 1`. |
| `OriginalFileName` | `nvarchar(255)` | no | Sanitized; display only. |
| `SizeBytes` | `bigint` | no | 1 B … 20 MB (Domain constant `GpxTrack.MaxSizeBytes`; upload limits users 20 MB, guests 5 MB). |
| `Sha256` | `binary(32)` | no | Integrity; its first 8 bytes form the GeoJSON `ETag`. |
| `RawBlobName` | `varchar(400)` | no | `users/{userId:N}/tracks/{trackId:N}/original.gpx` in container `gpx`. |
| `Status` | `tinyint` | no | `Pending = 0`, `Processed = 1`, `Failed = 2`. Uploads and demo seeding (M4) create `Processed` rows only; `Pending` remains for pre-M4 rows and future async importers; `Failed` is unused. |
| `CreatedAt` | `datetime2(0)` | no | UTC, truncated to whole seconds. |
| `ProcessedBlobName` | `varchar(400)` | yes | M4. `users/{userId:N}/tracks/{trackId:N}/track.json`. |
| `ProcessorVersion` | `tinyint` | yes | M4. `1`. Bumped when metric/simplification rules change. |
| `ProcessedAt` | `datetime2(0)` | yes | M4. UTC (= `CreatedAt` for synchronous uploads). |
| `PointCount` | `int` | yes | M4. Raw points used (from `trk`, else `rte`). |
| `DistanceMeters` | `float` | yes | M4. Haversine sum within segments, 0.1 m. |
| `ElevationGainMeters` / `ElevationLossMeters` | `float` | yes | M4. 3 m hysteresis, 0.1 m; NULL when the file has no elevation. |
| `MinElevationMeters` / `MaxElevationMeters` | `float` | yes | M4. NULL without elevation. |
| `ElapsedSeconds` / `MovingSeconds` | `int` | yes | M4. NULL when untimed. |
| `StartUtc` / `EndUtc` | `datetime2(0)` | yes | M4. First/last point time, **rounded to whole seconds** (half up) in the Domain so the upload response equals later reads; NULL when untimed. |
| `StartPoint` / `FinishPoint` | `geography` | yes | M4. SRID 4326 via the existing `GeoPointConverter`. |
| `WarningFlags` | `tinyint` | **no** | M4. Bit flags `TimestampsIgnored`, `NoElevation`, `MultipleSegments`, `RouteConverted`; `0` = none (also for `Pending` rows). |

Keys/indexes: `FK_GpxTracks_Runs_RunId_UserId (RunId, UserId)` → `AK_Runs_Id_UserId` ON DELETE CASCADE (replaces a simple `RunId` FK) · `UX_GpxTracks_Run_Kind (RunId, Kind)` unique · `IX_GpxTracks_RunId_UserId` (FK index) · `IX_GpxTracks_UserId`. M4 adds no index (the cap count uses `IX_GpxTracks_UserId` through the owner filter; M5 queries go through `Runs`).
Check **`CK_GpxTracks_Processed`** (M4): `[Status] <> 1 OR ([ProcessedBlobName] IS NOT NULL AND [DistanceMeters] IS NOT NULL AND [PointCount] IS NOT NULL AND [StartPoint] IS NOT NULL)`. `FinishPoint` and the other summary columns are always set by the processor but not constrained.
Track cap (application-enforced, `GpxLimits`): ≤ 16 per guest, ≤ 2 000 per user; re-counted inside the insert transaction under a per-user application lock (`sp_getapplock` `user-tracks:{userId:N}`, also taken by run delete; [ADR 0023 amendment](adr/0023-run-tracks-prefill-and-deletion.md)).
**Ownership rationale:** a track's denormalized `UserId` drives the `Owner` filter and the blob path, so it must never disagree with its run's owner. The composite FK makes the database enforce "a track belongs to the same user as its run" (a track pointing at another user's run cannot be inserted), on top of the application-level ownership guard (ADR 0010). The cascade path stays single: `Users → Runs → GpxTracks`.
Not stored in SQL: bbox, the simplified line, the elevation profile and splits — they live only in the `track.json` blob ([ADR 0021 §5](adr/0021-track-metrics-simplification-and-geojson.md)). No processing-error column: rejected files are never stored.

## Blob storage

Container `gpx` (private). The API creates it only when `Storage:CreateContainerOnStartup=true` (AppHost, tests); in Azure it is provisioned by Bicep (M7). Rule: **every blob a user owns is under `users/{userId:N}/`** — prefix delete removes all of a user's files (guest cleanup, future account deletion).

```
gpx/users/{userId:N}/tracks/{trackId:N}/original.gpx   Content-Type application/gpx+xml   (exact uploaded bytes)
gpx/users/{userId:N}/tracks/{trackId:N}/track.json     Content-Type application/geo+json  (processed FeatureCollection, ADR 0021 §5)
```

Each track has its own prefix, so deleting a track or a run is a prefix delete per track (blobs first, then rows; ADR 0023). A replace writes the new track id's prefix first and deletes the old prefix after commit (best effort; failures logged `TrackBlobOrphaned`). Upload failures and cancellations roll back the new prefix. A fresh guest gets 6 demo tracks = 12 blobs. The API never serves blob URLs; it streams the blobs itself.

## Insights reads (M5)

**No schema change in M5**: no migration, table, column, view or index ([ADR 0025](adr/0025-insights-report-computation-and-api.md)). Metrics are computed on demand in `RunnersDiary.Domain/Insights` ([ADR 0024](adr/0024-training-metrics-definitions-v1.md)); nothing is stored.

`InsightsReportService` reads through the `Owner`-filtered context with `AsNoTracking` **projections** only: the user's `TimeZoneId` (`Users` PK); then, each a seek on `CX_Runs_User_Date (UserId, Date)`, run facts (`Id, Date, Status, RunTypeId, DistanceMeters, DurationSeconds, ElevationGainMeters, Rpe, Surface, AvgHeartRate`) for the bucket/load window; the comparison window's part outside it (when needed); `MIN(Date)` of completed load-counting runs; race facts (the `Race_*` scalars and `Rating_Stars`, `Date ≤ to`). `Notes`, `Location_*`, `Title` and `GpxTracks` are never read. 1–5 queries per report.

## AiSummaries (M6)

Migration `20261104070212_AiSummaries`; [ADR 0028](adr/0028-summary-cache-quotas-and-demo-summaries.md) §1–2. Cache of **model-written** summaries only. Demo and template summaries are computed on read and never stored. User-owned: `Owner` query filter; the owner of a new row is always the current user.

| Column | SQL type | Null | Notes |
|---|---|---|---|
| `Id` | `uniqueidentifier` | no | PK **non-clustered**, v7. |
| `UserId` | `uniqueidentifier` | no | FK → `Users.Id` **ON DELETE CASCADE** (guest cleanup removes the rows). Owner filter. |
| `PeriodFrom` / `PeriodTo` | `date` | no | The requested period. |
| `Compare` | `tinyint` | no | `None = 0`, `Previous = 1`, `PreviousYear = 2`. |
| `Language` | `varchar(8)` | no | `en`. |
| `MetricsVersion` / `FactSheetVersion` / `PromptVersion` | `smallint` | no | Part of the validity tuple. |
| `ModelDeployment` | `varchar(64)` | no | The configured deployment when written (validity tuple). |
| `ModelId` | `varchar(100)` | yes | Provider-reported model id (audit, shown in the UI); truncated to 100 characters. |
| `Fingerprint` | `binary(32)` | no | SHA-256 of the canonical fact sheet (validity tuple). |
| `ContentJson` | `nvarchar(max)` | no | The validated draft + resolved `values`. ≤ 16 KB, enforced in Domain (`SummaryValues.MaxContentBytes`). Holds no user free text. |
| `InputTokens` / `OutputTokens` | `int` | no | Provider-reported usage. |
| `CreatedAt` | `datetime2(0)` | no | UTC, truncated to whole seconds. |

- **Keys:** `UX_AiSummaries_Key (UserId, PeriodFrom, PeriodTo, Compare, Language)`, unique **clustered** · `PK_AiSummaries (Id)` non-clustered.
- **Upsert:** one row per key; a concurrent insert of the same key is retried once as an update.
- **Cap (application-enforced):** users 1 000 rows, guests 20. On insert, the oldest rows by `CreatedAt` are deleted.
- **Fresh vs stale:** a row is fresh when the whole tuple (`Fingerprint`, the three versions, `ModelDeployment`) equals the current one. The deployment is ignored when `Ai:Provider = None`. Nothing is invalidated on run writes.

## AiGenerations (M6, usage ledger)

[ADR 0028](adr/0028-summary-cache-quotas-and-demo-summaries.md) §4 + amendment. One row per generation request that reached the model. **Content-free:** ids, counts and timings only, never prompt or answer text.

**Documented exception to the `Owner` filter** (the second after `GuestCleanup`, ADR 0010 §6):
- not `IUserOwned`, no `Owner` filter, and **no FK to `Users`**, so the global budgets survive guest cleanup;
- only `AiUsageLedger` (through `EfAiGenerationStore`) reads it. It filters per-user reads by the caller's id from claims, or aggregates globally.

| Column | SQL type | Null | Notes |
|---|---|---|---|
| `Id` | `uniqueidentifier` | no | PK **non-clustered**, v7. |
| `UserId` | `uniqueidentifier` | no | The caller; no FK. |
| `IsGuest` | `bit` | no | For the guest-global budget. |
| `CreatedAt` | `datetime2(0)` | no | UTC, truncated to whole seconds. |
| `Outcome` | `tinyint` | no | `Succeeded = 0`, `InvalidOutput = 1`, `ProviderError = 2`, `Timeout = 3`, `Cancelled = 4`, `Throttled = 5`. A `Throttled` row is written only when an earlier attempt of the same generation was billed; it counts toward the global budgets but never toward the caller's quota. |
| `Attempts` | `tinyint` | no | Model calls that may have been billed (≤ 2). Summed for the global budgets. |
| `InputTokens` / `OutputTokens` / `DurationMs` | `int` | no | Provider-reported usage (a lower bound when an attempt timed out). |
| `PromptVersion` | `smallint` | no | |
| `ModelDeployment` | `varchar(64)` | no | |

- **Indexes:**
  - `CX_AiGenerations_User_Created (UserId, CreatedAt)` **clustered**: the per-user quota (rows in the last 24 h, or all rows for a guest);
  - `IX_AiGenerations_Created (CreatedAt) INCLUDE (IsGuest)`: the global and guest-global sums, and the purge.
- **Retention:** 30 days (`AiUsageLedger.Retention`). The guest cleanup run purges older rows with one set-based delete.
- Account deletion (post-MVP) must purge this table explicitly, because there is no FK.

## GeocodeUsage (M7, system counter)

[ADR 0033 amendment](adr/0033-production-maps-and-azure-maps.md) §1. The global daily budget for Azure Maps geocoding calls, shared by all replicas and durable across restarts (default 75 per UTC day, `Maps:Geocoding:GlobalDailyLimit`).

**Documented exception to the `Owner` filter** (the third, after `GuestCleanup` and `AiGenerations`, ADR 0010 §6): not `IUserOwned`, no `Owner` filter. It holds **no user id and no query text**, only a count per day. Only `GeocodeBudget` writes it (through a context created with the anonymous current user), and only the guest cleanup deletes from it.

| Column | SQL type | Null | Notes |
|---|---|---|---|
| `Day` | `date` | no | PK (clustered). The UTC day. |
| `Count` | `int` | no | Provider calls granted that day; never exceeds the limit. |

- **Write path (one round trip per search):** `INSERT … SELECT @day, 0 WHERE NOT EXISTS (… WITH (UPDLOCK, HOLDLOCK) …)`, then `UPDATE … SET Count = Count + 1 OUTPUT inserted.Count WHERE Day = @day AND Count < @limit`. No row returned = budget spent → 429 `code: geocode-budget`.
- **Indexes:** the PK only (point seek by day; the purge is a range on the same key).
- **Retention:** 35 days (`GeocodeBudget.Retention`); each guest cleanup pass deletes older rows.
- Migration `20261104153828_GeocodeUsage` only creates the table, so it is compatible with the previous release (ADR 0032 expand rule).

<a id="planned-in-m8-race-proposals"></a>

## Added in M8 (race proposals)

**Implemented 2026-11-13 on `m8-race-proposals` (schema, demo catalogue, endpoints, import job); UI done and reviewed 2026-11-14 (no schema change from the review); awaiting approval/merge; nothing deployed.**
- [M8 plan](plans/M8-race-proposals.md) (deviations §12); ADRs [0036](adr/0036-race-catalogue-model-and-race-runs.md) (model, upsert, dedup, race → run; amended) and [0037](adr/0037-race-proposals-filtering-and-state.md) (interests, state; amended).
- One additive migration **`20261113165157_RaceCatalogue`**. It is compatible with the previous release (ADR 0032 expand rule): new tables, plus one nullable column and two indexes on `Runs`.

```
Races (catalogue, shared) 1 ──< RaceDistances
  │  └── 0..1 DuplicateOfRaceId → Races (self, NO ACTION)
  ├──< RaceProposalStates >── 1 Users          (per user; Owner filter)
  └──< Runs.CatalogRaceId (0..1 per user per race; ON DELETE SET NULL)
Users 1 ── 0..1 RaceInterestProfiles           (per user; Owner filter)
```

### Races (catalogue)

**Documented exception to the `Owner` filter** (the fourth, after `GuestCleanup`, `AiGenerations` and `GeocodeUsage`; ADR 0010 §6). The table is shared reference data: no `UserId`, not `IUserOwned`, no `Owner` filter. Personal data is never stored (ADR 0035 §2).
- **Writer:** only the import job (`id-rd-race-import`, table-level grants, ADR 0038), plus the Development/Testing demo seeder.
- **The API only reads it.**

| Column | SQL type | Null | Notes |
|---|---|---|---|
| `Id` | `uniqueidentifier` | no | PK **non-clustered**, v7. |
| `Source` | `tinyint` | no | `RunningLife = 1`, `Demo = 100` (more later). |
| `ExternalId` | `varchar(100)` | no | The source's stable id (slug or numeric id). |
| `Name` | `nvarchar(200)` | no | Source language; trimmed, control characters removed. |
| `Date` | `date` | no | Local race date (ADR 0008 semantics). |
| `StartTime` | `time(0)` | yes | Local wall-clock time when the source gives one. |
| `TimeZoneId` | `varchar(64)` | yes | IANA, from the country (`PL` → `Europe/Warsaw`); NULL when unknown. |
| `LocationName` | `nvarchar(200)` | yes | Locality as the source names it. |
| `Point` | `geography` | yes | SRID 4326 via the existing `GeoPointConverter` (X = lon, Y = lat), like `Runs.Location_Point`. |
| `CountryCode` | `char(2)` | no | ISO 3166-1 alpha-2 (`PL`). |
| `RegionCode` | `varchar(6)` | yes | ISO 3166-2 (`PL-24` = śląskie). |
| `Kind` | `tinyint` | no | `Road 0, Trail 1, Mountain 2, Ultra 3, CrossCountry 4, Ocr 5, Other 6`. |
| `Surface` | `tinyint` | yes | Existing `Surface` enum. |
| `ElevationGainMeters` | `float` | yes | 0 … 100,000. |
| `SignupUrl` / `EventUrl` | `varchar(2048)` | yes | Absolute `http(s)` only; never fetched by the server. |
| `SourceUrl` | `varchar(2048)` | yes | Required for real sources (attribution link); NULL for `Demo`. |
| `PriceAmount` | `decimal(9,2)` | yes | With `PriceCurrency`; both or neither (check constraint). |
| `PriceCurrency` | `char(3)` | yes | ISO 4217. |
| `SignupDeadline` | `date` | yes | |
| `OrganizerName` | `nvarchar(150)` | yes | Organisation name only; e-mail/phone-like values become NULL (Domain). |
| `FactsHash` | `binary(32)` | no | SHA-256 of the canonical normalised facts. |
| `ImportedUtc` / `LastSeenUtc` / `UpdatedUtc` | `datetime2(0)` | no | First seen / last seen in a scan / last facts change. |
| `PreviousDate` | `date` | yes | The date before the last date change. |
| `DateChangedUtc` | `datetime2(0)` | yes | |
| `MissedScans` | `tinyint` | no | Complete scans in which a future race was absent; reset when seen. |
| `RemovedUtc` | `datetime2(0)` | yes | Soft removal at `MissedScans ≥ 2`; cleared when the race reappears. |
| `DuplicateOfRaceId` | `uniqueidentifier` | yes | FK → `Races.Id` **NO ACTION**; set by the duplicate matcher. |
| `DetailETag` | `varchar(200)` | yes | For conditional requests. |
| `DetailLastModified` | `varchar(64)` | yes | Raw header value. |
| `DetailFetchedUtc` | `datetime2(0)` | yes | |

- **Keys and indexes:**
  - **`CX_Races_Date (Date)` clustered**, non-unique: every proposal query is a date window;
  - `PK_Races (Id)` non-clustered;
  - **`UX_Races_Source_External (Source, ExternalId)` unique**: the upsert's natural key;
  - `IX_Races_DuplicateOf (DuplicateOfRaceId) WHERE DuplicateOfRaceId IS NOT NULL`.
- **Checks:** `CK_Races_Price` (both or neither); `CK_Races_Duplicate` (`DuplicateOfRaceId <> Id`).
- **Retention:** the import job deletes races with `Date < today − 180 days` (`RaceImport:RetainPastDays`, Q9). Distances and states cascade; linked runs get `CatalogRaceId = NULL` and keep their own copy of every fact. A doomed canonical race still referenced by a surviving duplicate is skipped; `DuplicateOfRaceId` among the deleted rows is cleared first (NO ACTION self-FK).
- **Volume:** ≈ 600–1,000 rows (one source); ≈ 3k with a second source.

### RaceDistances

| Column | SQL type | Null | Notes |
|---|---|---|---|
| `RaceId` | `uniqueidentifier` | no | FK → `Races.Id` **ON DELETE CASCADE**. |
| `DistanceMeters` | `float` | no | 1,000 … 250,000 (shorter kids' runs are dropped by the normaliser). |

PK `(RaceId, DistanceMeters)` clustered. ≤ 12 per race (Domain). A child table, not JSON (ADR 0036 §1).
- **Mapping:** EF one-to-many `Race.Distances` of the domain type `RaceDistance(RaceId, DistanceMeters)`; consumers read `Race.DistancesMeters` (ascending). `Race.ApplyFacts` diffs the rows when `FactsHash` changes (unchanged rows stay, missing ones are deleted, new ones inserted).

### RaceInterestProfiles (per user)

User-owned: `IUserOwned`, **`Owner` filter**, and the owner of a new row is always the current user. One row per user; no row means proposals are off for that user.

| Column | SQL type | Null | Notes |
|---|---|---|---|
| `UserId` | `uniqueidentifier` | no | **PK (clustered)**; FK → `Users.Id` **ON DELETE CASCADE**. |
| `Kinds` | `tinyint` | no | Bit set of `RaceKind` (bit = 1 << value); ≥ 1 bit. |
| `RegionCodes` | `varchar(200)` | no | EF primitive collection (JSON array text, explicit column type), ≤ 16 known ISO 3166-2 codes; `[]` = none. |
| `Center` | `geography` | yes | With `CenterName nvarchar(200) NULL`; both centre and radius or neither (check). |
| `RadiusMeters` | `int` | yes | 5,000 … 300,000. |
| `MinDistanceMeters` / `MaxDistanceMeters` | `float` | yes | 1,000 … 250,000; min ≤ max (check). |
| `HorizonDays` | `smallint` | no | 7 … 365. |
| `Keywords` | `nvarchar(100)` | yes | ≤ 5 terms. |
| `ShowInCalendar` | `bit` | no | |
| `ProposalsSeenUtc` | `datetime2(0)` | yes | "New since last visit" (ADR 0037 §3). |
| `UpdatedAt` | `datetime2(0)` | no | |

- **Checks** (mirror the Domain rules): `CK_RaceInterestProfiles_Center` (centre and radius both or neither; no `CenterName` without a centre), `_Radius` (5,000–300,000), `_Distance` (1,000–250,000, min ≤ max), `_Horizon` (7–365), `_Kinds` (1–127).

### RaceProposalStates (per user)

User-owned: **`Owner` filter**. One row per user and race that the user marked. `SignedUp` is **not stored**: it is derived from `Runs.CatalogRaceId`.

| Column | SQL type | Null | Notes |
|---|---|---|---|
| `UserId` | `uniqueidentifier` | no | FK → `Users.Id` **ON DELETE CASCADE**. |
| `RaceId` | `uniqueidentifier` | no | FK → `Races.Id` **ON DELETE CASCADE**. |
| `State` | `tinyint` | no | `Interested = 1`, `Dismissed = 2` (setting `None` deletes the row). |
| `RaceDateWhenMarked` | `date` | no | For `dateChanged` on Interested races. |
| `UpdatedAt` | `datetime2(0)` | no | |

- **Keys:** PK **`(UserId, RaceId)` clustered** (per-user reads are a seek on `UserId` + an id list); `IX_RaceProposalStates_RaceId` (FK, purge cascade).
- **Check:** `CK_RaceProposalStates_State` (`State IN (1, 2)`).
- **Cap (application-enforced):** users 2,000 rows, guests 200 (`Races:Proposals:MaxStatesPerUser` / `MaxStatesPerGuest`) → 409 `proposal-state-limit`.

### Runs (change)

| Column | SQL type | Null | Notes |
|---|---|---|---|
| `CatalogRaceId` | `uniqueidentifier` | yes | FK → `Races.Id` **ON DELETE SET NULL**. Set only by `POST /api/races/{id}/signup`; preserved by `PUT /api/runs/{id}`. |

New indexes:
- **`UX_Runs_User_CatalogRace (UserId, CatalogRaceId) WHERE CatalogRaceId IS NOT NULL`**, unique (one linked run per race per user → 409 `race-already-linked`). It also serves the "signed up?" lookup for a proposal window.
- `IX_Runs_CatalogRaceId (CatalogRaceId)`, non-unique: the FK index that serves the `SET NULL` when the job purges a race.

Verified (ADR 0036 amendment §2): SQL Server accepts the `SET NULL` FK next to the existing cascades, and a principal with only the `Races`/`RaceDistances` grants can purge; the engine performs the `Runs` SET NULL and the `RaceProposalStates` cascade.

### Guest deletion (M8)

- Every new per-user row hangs off `Users` with `ON DELETE CASCADE`: `RaceInterestProfiles`, `RaceProposalStates`, and runs as before. The existing guest cleanup (`ExecuteDelete` on `Users`) therefore **removes them with no code change** (proved by `Races/RaceGuestLifecycleTests`).
- Guests never write catalogue rows; the catalogue is untouched by guest cleanup.
- Signed-up guest runs are ordinary runs (blob prefix rule unchanged).

## Deferred (by milestone)

| Item | Milestone |
|---|---|
| ~~GPX metrics columns, GeoJSON blob, upload validation~~ | done in M4 |
| Saved places / geocoding cache (if needed) | not built in M7: the durable daily budget (`GeocodeUsage`) bounds cost without caching personal queries; revisit only if the 75/day cap is hit in practice |
| Orphan-blob reconciliation for users | post-MVP (ADR 0023 §5) |
| ~~Aggregate views/indexes for insights~~ | not needed in M5 (see *Insights reads*); revisit only if App Insights shows slow reports (M7) |
| ~~`AiSummaries` (cache per user + period + data hash), guest AI counter~~ | done in M6 (`AiSummaries`, `AiGenerations` ledger) |
| `ExternalSource`/`ExternalId` on `Runs` (Strava/Garmin/FIT) | post-MVP |
| Account deletion / guest → user conversion | post-MVP |
