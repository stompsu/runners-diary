# 0038 – The race import job on Container Apps (schedule, identity, SQL access, kill switch, observability, cost)

**Status:** Proposed (M8 plan, 2026-11-13; awaiting the user's approval)
**Date:** 2026-11-13
**Related:** [0031](0031-identities-secrets-and-data-access.md) (identities, Entra-only SQL), [0032](0032-migrations-and-jobs-in-azure.md) (job mode, SQL wake budget), [0034](0034-cost-guardrails-and-observability.md) (alerts, cost), [0035](0035-race-catalogue-sources-and-crawling-policy.md) (crawling rules), [0036](0036-race-catalogue-model-and-race-runs.md) (upsert rules)

## Context

- The catalogue is filled by crawling a third-party site on a schedule. This is untrusted input, egress to the internet, and a weekly SQL write.
- The API scales to zero and must not crawl in a request path or a hosted timer: that would wake SQL and run per replica (ADR 0032 §2).
- The SQL free offer costs **≈ 450 vCore-s per wake** (15-minute fixed auto-pause), and about 220 wakes a month exhaust it. `job-guest-cleanup` already wakes SQL every **Monday 03:15 UTC**.
- Container Apps jobs (verified 2026-11-13):
  - `Schedule` trigger with 5-field cron **in UTC**;
  - `replicaTimeout` in seconds and `replicaRetryLimit` (0 = no retry);
  - `az containerapp job start` for manual runs; history of the last 100 executions;
  - the platform metric **`Executions`** with a `state` dimension;
  - Microsoft recommends retry ≥ 1 because platform maintenance can interrupt replicas.

## Decision

### 1. Same API image, new entry mode `--job race-import`

`JobMode` gains a second job name (`race-import`). It builds the same ServiceDefaults + generic host as `guest-cleanup`. Infrastructure is registered with a **race-import profile**: persistence + `RaceSources` + the HTTP client only. There is no Blob, AI, Maps or guest services, and no hosted services. It then:
1. **Checks** `RaceImport:Enabled`; when false, it logs `Race import disabled` and **exits 0 before any SQL connection** (a disabled job never wakes SQL).
2. **Takes** a session app lock `sp_getapplock 'race-import'` (exclusive, timeout 0). If another run holds it, it exits 0 (a manual start overlapping the schedule is harmless).
3. **Reads the index** (projection `(Source, ExternalId, FactsHash, DetailFetchedUtc, DetailETag, DetailLastModified, Date)`) once, then **closes** the connection.
4. **Crawls** each enabled source under [0035](0035-race-catalogue-sources-and-crawling-policy.md)'s rules, with no SQL connection open.
5. **Writes** per source in one transaction:
   - the `RaceCatalogueMerger` result;
   - then the duplicate matcher;
   - finally the retention purge.
6. **Logs** one summary per source and **exits**:
   - 0 when every enabled source completed or was skipped by a stop rule that is not an error (403/429 are logged as warnings);
   - **1** on a layout change (zero events from a 200 page), a robots failure, an unhandled exception or a SQL failure.

**Why the API image:**
- It reuses the EF model, migrations, Domain and `JobMode` (no new image to build, scan, push and prune).
- It follows the API release automatically, exactly as `job-guest-cleanup` does.

**Why not a separate image:**
- An image without Kestrel would shave about 10 MB, but it costs another Dockerfile, CI build and digest pin for a weekly job.
- The isolation that matters (identity and data rights, §2) does not depend on the image.

### 2. Its own identity with least privilege: `id-rd-race-import`

The job parses untrusted HTML. A bug there must not be able to read users' data, blobs, the guest signing key or the AI deployment, so the job **does not use `id-rd-api`**.
- **The new UAMI has only:**
  - `AcrPull` on the registry;
  - a contained SQL user with **table-level grants**:
    ```sql
    -- infra/sql/grant-app-users.sql (idempotent, run by job-migrate as the SQL Entra admin)
    CREATE USER [id-rd-race-import] WITH SID = <client-id as varbinary>, TYPE = E;   -- if missing
    GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.Races TO [id-rd-race-import];
    GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.RaceDistances TO [id-rd-race-import];
    -- nothing on Users, Runs, GpxTracks, Ai*, GeocodeUsage, RaceInterestProfiles, RaceProposalStates
    ```
- **No role on:** Storage, Key Vault, Foundry or Maps.
- **SQL access** follows ADR 0031: Entra-only server, connection string with `Authentication=Active Directory Managed Identity;User Id=<client id>` (no password, no secret), firewall `AllowAzureServices` as for the API.
- `migrate.sh` receives `RACE_IMPORT_IDENTITY_NAME` and `RACE_IMPORT_IDENTITY_CLIENT_ID` alongside the API's.
- **Cascade check (⚠, ADR 0036 references):** the retention purge deletes from `Races`, and SQL Server performs the `Runs.CatalogRaceId` `SET NULL` and `RaceProposalStates` cascade actions. Verify in an Api.Tests case that runs as a principal with only these grants that the cascades need no grant on `Runs` or `RaceProposalStates`. The fallback is in ADR 0036: purge only races without linked runs, and delete their states from the API side.

### 3. Bicep (`infra/modules/jobs.bicep`, `identities.bicep`, `main.bicep`)

| Setting | Value |
|---|---|
| Name | `job-race-import` |
| Trigger | `Schedule`, cron param `raceImportCron`, default **`25 3 * * 1`** (Monday 03:25 UTC, 10 minutes after the guest cleanup, so both share **one SQL wake**) |
| Image | the API image (`cleanupImage` pattern); args `['--job', 'race-import']` |
| Resources | 0.25 vCPU / 0.5 GiB |
| `replicaTimeout` | 1,800 s (a first backfill at 1.5 s per request with 40 + 150 fetches ≈ 5 min; headroom for a resuming SQL) |
| `replicaRetryLimit` | 1 (platform interruptions; the run is idempotent and re-crawls at most ≈ 200 pages) |
| `parallelism` / `replicaCompletionCount` | 1 / 1 |
| Identity / registry | `id-rd-race-import` (user-assigned), `AcrPull` |
| Env | `ASPNETCORE_ENVIRONMENT=Production`, the connection string with this identity's client id, `AZURE_CLIENT_ID`, `APPLICATIONINSIGHTS_CONNECTION_STRING`, `OTEL_SERVICE_NAME=job-race-import`, **`RaceImport__Enabled`** (param `raceImportEnabled`, default **`false`**), **`RaceImport__Sources__RunningLife__Enabled`** (param `raceImportRunningLife`, default **`false`**) |

- **No outbound network restriction:** the environment has no VNet (ADR 0030). The allow-list lives in code (0035 §4).
- The API app additionally gets **`Races__Enabled`** (param `racesUiEnabled`, default `false`), which feeds `client-config.races.enabled`.
- **No new resource type and no new paid SKU.**
- **`azd provision` changes exactly:**
  - one UAMI;
  - one `AcrPull` role assignment;
  - one Container Apps job;
  - one metric alert (§5);
  - two new env vars on `api` (`Races__Enabled`) and on `job-migrate` (the identity name and client id).

### 4. Deploy workflow (`.github/workflows/deploy.yml`)

- **Images:** none new. The job runs `rd-api`.
- **The step "Update the apps and the cleanup job"** also runs `az containerapp job update --name job-race-import --image "$API_IMAGE"`. The step "previous" records its image, and the rollback step restores it, exactly as for `job-guest-cleanup`.
- **The migration job runs before the apps** (existing order), so the `RaceCatalogue` migration and the new grants exist before any job revision uses them.
- **`smoke.sh` does not start the import job:** it would crawl a third party on every deploy. A manual first run is a gate (G-R3).

### 5. Observability and alerts (ADR 0034 style, free)

- **Logs** (`RunnersDiary.*` at Information; no page bodies, no URLs with query strings, no personal data):
  - one `RaceImportSourceCompleted {Source} {ListingPages} {DetailPages} {NotModified} {New} {Updated} {DateChanged} {Removed} {Restored} {Duplicates} {Purged} {DurationMs} {Outcome}`;
  - plus warnings for stop rules.
- **Meter `RunnersDiary.RaceImport`:** counters `races.import.requests` (by source and status class) and `races.import.upserts`.
- **Metric alert `alert-rd-race-import-failed`:**
  - `Microsoft.App/jobs` metric **`Executions`**, Total, 24 h window, dimension `state` = failed (⚠ the exact dimension value, e.g. `Failed`, is checked at implementation), on `job-race-import`, > 0;
  - same action group (e-mail) as the other alerts.
- **Saved KQL** in the runbook: the last 10 import summaries, and requests by status per source.
- **Freshness in the product:** `RaceProposalListResponse.catalogueUpdatedUtc` = max `LastSeenUtc`. The UI shows "Race list updated {date}" and, when older than 21 days, "Race list may be out of date".

### 6. Cost (retail, 2026-11-13; Consumption plan)

| Item | Estimate |
|---|---|
| Job compute | Weekly ≈ 2 min (30–40 listing pages + tens of detail pages at 1.5 s); first backfill ≈ 5–6 min per run until done. ≈ 0.25 vCPU × 150 s × 4.3 ≈ **160 vCPU-s/month**, inside the Consumption monthly free grant (⚠ re-check the grant figures at G-R2). **€0.** |
| SQL vCore | Runs inside the cleanup's Monday wake. The extra online time ≈ the job duration: ≈ 0.5 vCore × 150–360 s ≈ **75–180 vCore-s per run**, ≈ 0.3–0.8 % of 100,000 a month. Standalone (if cleanup moves) ≈ 450–600 vCore-s per run ≈ 2–2.6 % a month. Daily would be ≈ 14–18 %: rejected. |
| SQL storage | ≈ 1k `Races` + 3k `RaceDistances` rows < 2 MB (32 GB free). |
| Geocoding | **0** in M8 (the source has coordinates). Phase 2 needs its own budget so that API + job stay under the free 5,000 a month (e.g. API 75/day + job 60/day ≈ 4,185). |
| Log Analytics | ≈ 20 lines per run; negligible against the 0.15 GB/day cap. |
| Egress | ≈ 200 requests × ≤ 2 MB worst case; in practice ≈ 30–60 MB a week of ingress (free). Egress is tiny. |
| **Total** | **≈ €0 a month**, with the SQL free allowance impact ≤ 1 % when it shares the cleanup wake. |

## Consequences

- The import has no effect on the API's availability or its identity's blast radius. A compromised parser can at most corrupt the race catalogue, which the next clean run repairs (`FactsHash` differs) or a purge removes.
- Two jobs now depend on the Monday wake. Moving the cleanup cron should also move the import (documented next to both Bicep params).
- One more identity to grant in `grant-app-users.sql`. The migration job must run once after provisioning before the import job can write: deploy order G-R2 → G4-style deploy → G-R3.
- The image ships AngleSharp to the API process, which never calls it (registered only in the race-import profile).

## Alternatives considered

- **Hosted service inside the API:** runs per replica, only while the API is awake, and crawls with the API identity. Rejected.
- **Separate importer image:** cleaner isolation of code, but the identity already isolates the data rights. One more artifact to build, pin and roll back. Rejected for now; easy to split later because the entry point is already separate.
- **Reuse `id-rd-api`:** simplest, but the API identity reads Key Vault, Blob and Foundry. Rejected for an untrusted-input workload.
- **Azure Functions timer trigger:** a second hosting model, plan and deployment path. Rejected.
- **GitHub Actions scheduled workflow writing to SQL:** needs a firewall hole and SQL rights for CI (rejected in ADR 0032 for the same reason).
- **Daily schedule:** fresher, but ≈ 7× the SQL wakes unless it shares a daily wake that does not exist. Rejected; `raceImportCron` allows it.

## References (checked 2026-11-13)

- Container Apps jobs (`Schedule` cron in UTC, `replicaTimeout`, `replicaRetryLimit` with "set at least 1 for long-running jobs", `az containerapp job start`, 100-execution history; updated 2026-09-16): https://learn.microsoft.com/azure/container-apps/jobs
- Supported metrics for `Microsoft.App/jobs` (`Executions` with dimensions `state`, `jobName`, `executionName`): https://learn.microsoft.com/azure/azure-monitor/reference/supported-metrics/microsoft-app-jobs-metrics
- Azure SQL free offer (15-minute fixed auto-pause, 100,000 vCore-s/month): https://learn.microsoft.com/azure/azure-sql/database/free-tier (as checked for ADR 0032; not re-fetched)
- Azure Maps transactions (Search: 1 request = 1 transaction, batch = per item; 5xx/401/403/408/429 not billed; updated 2026-09-15): https://learn.microsoft.com/azure/azure-maps/understanding-azure-maps-transactions
- `sp_getapplock`: https://learn.microsoft.com/sql/relational-databases/system-stored-procedures/sp-getapplock-transact-sql (not re-fetched; already used for track caps, ADR 0023)
