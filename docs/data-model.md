# Runners Diary – Data model

Living document, owned by the `architect` agent. Status: **M2 implemented (migration `InitialSchema`; awaiting approval)**.
Decisions: [ADR 0008](adr/0008-run-date-and-time-storage.md) (date/time), [0010](adr/0010-current-user-and-data-scoping.md) (scoping), [0011](adr/0011-guest-accounts-lifecycle.md) (guests), [0013](adr/0013-persistence-model-and-migrations.md) (mapping, migrations).

Conventions: SI units (metres, seconds, metres of elevation); enums stored as `tinyint` and exposed as strings in the API; audit timestamps `datetime2(0)` UTC; GUID keys are `Guid.CreateVersion7()` assigned in the domain factories (time-ordered, known before `SaveChanges`; EF `ValueGeneratedNever`); every user-owned row carries `UserId` and is filtered by the `Owner` query filter.

## Entity overview

```
Users 1 ──< Runs >── 1 RunTypes (catalog, seeded)
              │  └─ (complex, same row) Location?, Race?, Rating?
              └──< GpxTracks  (0..1 per Kind; blob = original GPX)
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

## GpxTracks (M2: metadata + blob reference only)

| Column | SQL type | Null | Notes |
|---|---|---|---|
| `Id` | `uniqueidentifier` | no | PK. |
| `RunId` | `uniqueidentifier` | no | Part of the composite FK `(RunId, UserId)` → `Runs(Id, UserId)` ON DELETE CASCADE. |
| `UserId` | `uniqueidentifier` | no | Denormalized owner (filter + blob path). Part of the composite FK to `Runs`; **no FK to `Users`** (avoids multiple cascade paths). |
| `Kind` | `tinyint` | no | `PlannedCourse = 0`, `RecordedTrack = 1`. |
| `OriginalFileName` | `nvarchar(255)` | no | Sanitized; display only. |
| `SizeBytes` | `bigint` | no | ≤ 20 MB (enforced by M4 upload). |
| `Sha256` | `binary(32)` | no | Dedup / integrity. |
| `RawBlobName` | `varchar(400)` | no | `users/{userId:N}/tracks/{trackId:N}/original.gpx` in container `gpx`. |
| `Status` | `tinyint` | no | `Pending = 0`, `Processed = 1`, `Failed = 2`. M2 seeds `Pending`. |
| `CreatedAt` | `datetime2(0)` | no | |

Keys/indexes: `FK_GpxTracks_Runs_RunId_UserId (RunId, UserId)` → `AK_Runs_Id_UserId` ON DELETE CASCADE (replaces a simple `RunId` FK) · `UX_GpxTracks_Run_Kind (RunId, Kind)` unique · `IX_GpxTracks_RunId_UserId` (FK index) · `IX_GpxTracks_UserId`.
**Ownership rationale:** a track's denormalized `UserId` drives the `Owner` filter and the blob path, so it must never disagree with its run's owner. The composite FK makes the database enforce "a track belongs to the same user as its run" (a track pointing at another user's run cannot be inserted), on top of the application-level ownership guard (ADR 0010). The cascade path stays single: `Users → Runs → GpxTracks`.
**Deferred to M4 (maps-gpx):** processed GeoJSON blob name, track metrics (distance, moving/elapsed time, gain/loss, start/end UTC, start point, bbox), point count, processing error, schema of the GeoJSON.

## Blob storage

Container `gpx` (private). The API creates it only when `Storage:CreateContainerOnStartup=true` (AppHost, tests); in Azure it is provisioned by Bicep (M7). Rule: **every blob a user owns is under `users/{userId:N}/`** — prefix delete removes all of a user's files (guest cleanup, future account deletion). M4 adds `…/tracks/{trackId:N}/track.geojson`.

## Deferred (by milestone)

| Item | Milestone |
|---|---|
| GPX metrics columns, GeoJSON blob, upload validation | M4 |
| Saved places / geocoding cache (if needed) | M4 |
| Aggregate views/indexes for insights (only if query plans need them) | M5 |
| `AiSummaries` (cache per user + period + data hash), guest AI counter | M6 |
| `ExternalSource`/`ExternalId` on `Runs` (Strava/Garmin/FIT) | post-MVP |
| Account deletion / guest → user conversion | post-MVP |
