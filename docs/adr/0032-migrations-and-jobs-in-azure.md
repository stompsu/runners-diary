# 0032 – Schema migrations and background jobs in Azure

**Status:** Accepted (M7 plan approved 2026-11-04; amended 2026-11-04 after Part A implementation, see the end)
**Date:** 2026-11-04
**Completes:** ADR 0013 §7 ("Azure: decided in M7") and ADR 0011 §5 ("M7 decides whether a Container Apps job is also needed").

## Context

- Migrations must never run at API startup in Azure. The pipeline produces an idempotent script or bundle, and applying it is a separate step (0013 §7).
- Guest cleanup is a hosted service that runs only while an API replica lives (0011 §5). It starts **1 minute after every replica start**.
- The SQL free offer (verified 2026-11-04, [Learn](https://learn.microsoft.com/azure/azure-sql/database/free-tier)) gives 100,000 vCore-seconds per month:
  - the **auto-pause delay is fixed at 15 minutes**;
  - at exhaustion the database pauses **until the next month** (`AutoPause`), or bills overage (`BillOverUsage`, irreversible).
- Every wake therefore costs at least about 0.5 vCore × 15 min ≈ **450 vCore-s**, so about 220 wakes per month exhaust the allowance. Anything that wakes SQL on a timer, or because of anonymous traffic, is an **availability risk**, not just a cost.
- Container Apps jobs (verified): manual and scheduled (cron in UTC) triggers, `az containerapp job start`, billed at the active rate only while running ([jobs](https://learn.microsoft.com/azure/container-apps/jobs)).

## Decision

### 1. Migrations: an EF Core bundle run by a manual Container Apps job

- **Build:** CI runs `dotnet ef migrations bundle --self-contained -r linux-x64` (startup project `RunnersDiary.Api`, the existing design-time factory). The bundle goes into image `rd-migrate` (`infra/docker/migrate.Dockerfile`), on `runtime-deps:10.0-noble` plus **go-sqlcmd**. go-sqlcmd is Microsoft's single-binary sqlcmd with managed-identity auth; it is used for the grant script.
- **Job `job-migrate`:** manual trigger, identity `id-rd-migrator` (the SQL Entra admin, ADR 0031), 0.5 vCPU / 1 GiB, `replicaTimeout` 900 s, `replicaRetryLimit` 1. The entrypoint `migrate.sh`:
  1. `efbundle --connection "<keyless MI connection string>"`. This is idempotent: it applies pending migrations under EF's migration lock.
  2. `sqlcmd … -i grant-app-users.sql` creates the API's DB user if missing, then adds `db_datareader` and `db_datawriter`. It is idempotent and has no DDL rights for the API:
     ```sql
     CREATE USER [<api-identity-name>] WITH SID = <client-id as varbinary>, TYPE = E;
     ```
- **Order in the deploy workflow:**
  1. push images;
  2. update `job-migrate` to the new tag;
  3. start it and wait for `Succeeded`;
  4. only then update `api` / `web`.

  On failure the apps are not touched and the run fails.
- **Compatibility rule (new):** every migration must work with the **previous** API release, because the old revision serves traffic while the job runs and is the rollback target.
  - Destructive changes take two releases (expand → contract).
  - The reviewer checks this per migration.
  - There are no down-migrations in Azure.
- **First deployment:** the same job creates the whole schema and the run-type seed (`HasData`).

### 2. Guest cleanup: hosted service with idle-skip, plus a weekly scheduled job

- **Hosted service change** (backend-dotnet): the periodic timer **skips a tick unless the process has opened a SQL connection since the previous tick**.
  - A connection counter comes from a `DbConnectionInterceptor`.
  - The opportunistic run on guest creation is unchanged.
  - Effect: a replica woken by anonymous `/api/client-config` or by a crawler never wakes SQL by itself 1 minute later.
- **Job `job-guest-cleanup`** (scheduled):
  - Runs the same API image with args `--job guest-cleanup`. The new entry mode builds a host with Infrastructure only (no Kestrel, no auth):
    1. it runs `GuestCleanup.RunOnceAsync` until a batch removes nothing, including the AI ledger purge (0028 amendment);
    2. it exits `0`, or `1` on failure.
  - Identity `id-rd-api`; 0.25 vCPU / 0.5 GiB; `replicaTimeout` 600 s.
  - Cron **`15 3 * * 1`** (Monday 03:15 UTC, **weekly**, plan Q5). Each run wakes SQL for at least 450–900 vCore-s, so weekly costs about 2–4% of the free allowance and daily about 14–27%.
- **The 24 h guarantee stays in the token** (`exp` = `ExpiresAt`) and in the guest check (0009). The job only bounds how long expired rows and blobs linger when nobody uses the app.

### 3. Other startup work in Azure

`Database:MigrateOnStartup` and `Storage:CreateContainerOnStartup` are **not set** in Azure. Bicep creates `gpx`, and the migration job creates the schema. The API refuses neither. Their absence is the default.

## Consequences

- Schema changes are applied by a reviewed, idempotent artifact, under an identity the API never holds. The API runs with read/write rights only.
- One extra image to build (≈ 80–100 MB). The bundle is self-contained, so it does not depend on the API image's runtime.
- Deploys take 1–2 minutes longer, because the job wakes SQL (a resume can take up to about 1 minute).
- Expired guests may linger up to about a week when the app is idle. That is acceptable: the tokens are dead, the data is the synthetic demo plus whatever the guest typed, and the job and any user activity remove it.
- Rollback never runs migrations (`skip_migrations`). A bad migration is fixed forward.

## Alternatives considered

- **`Database.MigrateAsync()` at API startup with a privileged identity:** contradicts 0013 §7, and gives the API DDL rights at runtime. Rejected.
- **Idempotent SQL script applied from the GitHub runner:** needs a firewall hole for runner IPs and SQL rights on the CI identity. Rejected.
- **API image with a `--job migrate` mode:** one image fewer, but the API image would need the EF design-time stack and the API identity would need admin. Rejected.
- **Daily cleanup job (the brief's literal wording):** about 14–27% of the free vCore budget for no user-visible benefit. Offered as plan Q5.
- **No cleanup job (hosted service only):** cheapest, but expired data can linger indefinitely when the app is idle. Rejected.

## Amendment (2026-11-04, M7 Part A implemented)

1. **Migrate image (§1).** `infra/docker/migrate.Dockerfile`, three stages, all digest-pinned:
   - the SDK stage builds the bundle **inside the Dockerfile** (`dotnet ef migrations bundle --self-contained -r linux-x64|linux-arm64`, `OpenApiGenerateDocuments=false`), so CI needs no separate bundle step;
   - **go-sqlcmd `v1.10.0`**, downloaded from the GitHub release and **checked against pinned SHA-256 values** per architecture;
   - final stage `runtime-deps:10.0-noble`, non-root (uid 1654), ≈ **116 MB** compressed (the §Consequences estimate of 80–100 MB was low).
   - `migrate.sh` retries each step 5 × 20 s (a paused free-offer database can take about a minute to resume; both steps are idempotent) with `Connect Timeout=120`. go-sqlcmd uses `--authentication-method ActiveDirectoryManagedIdentity -U <migrator client id>`.
2. **Job mode (§2), as built.** `RunnersDiary.Api/Jobs/JobMode.cs`: `--job guest-cleanup` (or `--job=guest-cleanup`) builds a generic host with ServiceDefaults + Infrastructure only (no Kestrel, auth, endpoints or hosted services; periodic cleanup, startup migration and container creation forced off), then runs `GuestCleanup` passes **until a pass removes nothing, capped at 1,000 passes** so a stuck guest cannot loop forever. Exit 0 on success, 1 on any failure, including an unknown job name or a startup/config error. Disposing the host flushes telemetry before exit.
3. **Idle-skip (§2), as built.** `SqlActivityInterceptor` (a `DbConnectionInterceptor`) counts opened connections; the hosted cleanup skips a tick when the count has not moved since the previous tick.
4. **More rows for the cleanup to purge.** Each pass also deletes `GeocodeUsage` rows older than 35 days ([ADR 0033 amendment](0033-production-maps-and-azure-maps.md), [data model](../data-model.md#geocodeusage-m7-system-counter)), alongside the AI ledger purge.
5. **Migration `20261104153828_GeocodeUsage`** (create table only) is additive and compatible with the previous release, so it follows the expand rule of §1.
6. **Production startup failure handling (API).** A top-level catch in `Program.cs` logs `Startup failed (<ExceptionType>)` (message only in Development) and exits 1. QA found that without it a failed startup inside the container hung at about 99 % CPU instead of exiting: a busy, billed replica instead of a fast, visible failure.

**Still unverified (⚠, first run at G4):** `CREATE USER … WITH SID … TYPE = E` with the client id and the UAMI admin `sid`; go-sqlcmd's managed-identity flags against Azure SQL; the resume behaviour of the free-offer DB within the retry window.

## References (checked 2026-11-04)

- Azure SQL free offer (15-minute fixed auto-pause, `AutoPause` vs `BillOverUsage`, free amount metric/alert; updated 2026-11-02): https://learn.microsoft.com/azure/azure-sql/database/free-tier
- Container Apps jobs: https://learn.microsoft.com/azure/container-apps/jobs
- EF Core migration bundles (`dotnet ef migrations bundle`): https://learn.microsoft.com/ef/core/managing-schemas/migrations/applying#bundles (not re-fetched; stable since EF 6)
- go-sqlcmd (managed-identity auth): https://learn.microsoft.com/sql/tools/sqlcmd/go-sqlcmd-utility (not re-fetched ⚠)
