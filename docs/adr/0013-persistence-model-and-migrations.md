# 0013 – Persistence model (EF Core 10) and migrations policy

**Status:** Accepted (M2 plan approved by the user 2026-11-03; implemented in M2 — the §3 spike succeeded, no fallback used)
**Date:** 2026-11-03

## Context

M2 creates the first schema: users, runs (planned → completed), run types, race details, ratings, GPX track metadata, location as a SQL Server `geography` point (brief §2), with ADR 0008's date/time columns. Constraints: `RunnersDiary.Domain` has **no package references** (ADR 0001) — so no NetTopologySuite types in entities; aggregate/date-range queries per user (calendar, M5 insights); smallest schema that keeps Strava/FIT/plan-generator open; never auto-deploy schema changes to Azure.

## Decision

1. **Domain entities are mapped directly** with Fluent configuration in `RunnersDiary.Infrastructure/Persistence/Configurations/` (no separate persistence models).
2. **Race details, rating and location are value objects inside `Run`**, mapped as EF Core 10 **optional complex types** (table splitting into `Runs` columns): `Race` (`RaceDetails?`, required `Name`), `Rating` (`RunRating?`, required `Stars`), `Location` (`RunLocation?`, required `Point`). One row per run, no joins, cascade is trivial, the `Owner` query filter covers them (ADR 0010).
3. **Location** — domain type `GeoPoint(double Latitude, double Longitude)` (validated ranges); Infrastructure maps it with a value converter to NetTopologySuite `Point` (X = longitude, Y = latitude, SRID 4326) on a `geography` column (`Microsoft.EntityFrameworkCore.SqlServer.NetTopologySuite`, `UseNetTopologySuite()`). **Spike first** (implementation step 2): if EF rejects a converter to a spatial provider type, or spatial inside a complex type, fall back to a shadow property `LocationPoint` (`Point`) on `Run` synchronized from `GeoPoint` by a `SaveChangesInterceptor` and `IMaterializationInterceptor`. Either way the column is `geography` and Domain stays pure. **Outcome (M2):** the converter inside the optional complex type works with EF Core 10.0.12 + SQL Server 2022; the fallback was not needed.
4. **Run types** are a fixed catalog in code (`RunnersDiary.Domain/RunTypes/RunTypeCatalog.cs`, stable `smallint` ids + codes + category + flags) and **seeded with `HasData` from that catalog** into a `RunTypes` table (FK target, joinable in SQL for M5). Display names come from frontend i18n by `code`. Changing the catalog = a migration, which is the intent. `GET /api/run-types` serves the catalog from memory (no SQL hit — helps auto-pause).
5. **Keys & indexes**: GUID keys generated client-side — as built, `Guid.CreateVersion7()` in the domain factories (`ValueGeneratedNever`), time-ordered and known before `SaveChanges`. `Runs`: **clustered index `(UserId, Date)`**, primary key `Id` **non-clustered** — every hot query is "this user's runs in a date range". `rowversion` concurrency token on `Runs`. Details in `docs/data-model.md`.
6. **Deletion**: `Users → Runs` and `Runs → GpxTracks` cascade in the database. `GpxTracks.UserId` is denormalized for scoping and blob paths, indexed, **without** an FK to `Users` (avoids SQL Server multiple-cascade-path errors); instead the run FK is composite, `GpxTracks(RunId, UserId)` → `Runs(Id, UserId)` via alternate key `AK_Runs_Id_UserId`, so the database guarantees a track's owner equals its run's owner (single cascade path kept). Blobs are deleted by application code (guest cleanup / run delete) — the database never knows about them.
7. **Migrations**: in `RunnersDiary.Infrastructure/Persistence/Migrations`, created with the local `dotnet-ef` tool (`.config/dotnet-tools.json`) via an `IDesignTimeDbContextFactory` (dummy connection string; no Aspire host needed):
   `dotnet ef migrations add <Name> --project src/RunnersDiary.Infrastructure --startup-project src/RunnersDiary.Api`. `.editorconfig` marks `Persistence/Migrations/*.cs` as generated code (analyzers off). EF packages and `dotnet-ef` stay on the EF Core patch Aspire resolves (10.0.12 with Aspire 13.6.4). The DbContext is registered with non-pooled `AddDbContext` + `EnrichSqlServerDbContext` (ADR 0010).
   Applied:
   - **Local (AppHost)**: the API runs `Database.MigrateAsync()` at startup **only when** `Database:MigrateOnStartup=true`, which only the AppHost sets (`Database__MigrateOnStartup`). EF 9+ takes a migration lock, so concurrent starts are safe.
   - **Tests**: the container fixture applies migrations once per database.
   - **Azure (M7)**: never at app startup. The deploy pipeline produces an idempotent script / EF bundle; applying it is a separate, user-confirmed step (decided in M7).
8. Column conventions: SI units (`float` metres, `int` seconds, `float` metres of elevation), enums as `tinyint`, `datetime2(0)` UTC audit columns, explicit `nvarchar`/`varchar` lengths, no `nvarchar(max)`.

## Consequences

- Wide `Runs` table (~40 columns) — fine at this scale and ideal for aggregate scans.
- Optional complex types require ≥ 1 required property each (satisfied above); they are new in EF 10 — the spike also covers them (fallback: owned 1:1 tables `RaceDetails`/`RunRatings` keyed by `RunId`).
- Domain types carry no EF attributes; configuration is centralized.
- Future features: Strava/FIT import adds `Runs.ExternalSource/ExternalId` (unique per user) and more `GpxTracks.Kind`/format values; a plan generator adds a `TrainingPlans` table referencing planned runs — none require reshaping M2 tables.

## Alternatives considered

- **Separate `RaceDetails` / `RunRatings` / `Locations` tables** — closer to the brief's entity list, but more joins, filters and cascades for strictly 1:0..1 data. Kept as the documented fallback.
- **Saved-places `Locations` table** (reusable named places) — not required by M2–M4; can be added later without changing `Runs`.
- **Lat/lon `float` columns instead of `geography`** — simpler, but the brief mandates `geography`.
- **Run types as user-editable data** — not required; catalog-in-code keeps validation pure.
- **Migrate on startup everywhere** — risky with multiple replicas and no review step for production schema changes.

## References (checked 2026-11-03)

- EF Core 10 what's new (optional complex types, named query filters): https://learn.microsoft.com/ef/core/what-is-new/ef-core-10.0/whatsnew (updated 2026-08-05)
- Not re-fetched for this ADR (verify during the step-2 spike): complex types https://learn.microsoft.com/ef/core/modeling/complex-types · SQL Server spatial (NetTopologySuite, SRID 4326, X = longitude) https://learn.microsoft.com/ef/core/providers/sql-server/spatial · data seeding (`HasData` for static reference data) https://learn.microsoft.com/ef/core/modeling/data-seeding
- Aspire EF Core SQL Server enrichment: https://aspire.dev/integrations/databases/efcore/sql-server/sql-server-connect/
