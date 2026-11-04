# 0025 – Insights report: on-demand computation, one period endpoint, M6 read-only contract

**Status:** Accepted (M5 plan approved 2026-11-04) · Amended 2026-11-04 (implementation, see the end)
**Date:** 2026-11-04

## Context

M5 adds the Insights dashboard. M6 adds an AI agent whose tools must read **pre-computed** metrics only, with no raw DB access (brief §2 "AI"). ADR 0010 requires every query to be scoped by the `Owner` filter. Scale is small (a few hundred runs per user per year, a 20 000-run cap per user, ≤ 500 runs per guest), and serverless SQL should not be woken without need. The clustered index `CX_Runs_User_Date (UserId, Date)` already serves date-range scans. Metric definitions are in ADR 0024.

## Decision

1. **Computation on demand, no stored aggregates.**
   - The pure `InsightsReportBuilder` (Domain) takes `(facts, raceFacts, historyStart, period, compare, today)` and returns an `InsightsReport` record.
   - `InsightsReportService` (Infrastructure, scoped, `IInsightsReportService`) loads the inputs through the filtered `RunnersDiaryDbContext` with `AsNoTracking` **projections** (no `Notes`, location or tracks) in at most **5 queries**:
     1. the user's zone (for `today`);
     2. completed + planned facts for `[min(Monday(from), 1st of month(from)) − 34 d, max(Sunday(to), last of month(to))]`;
     3. the comparison window's facts, when it does not overlap query 2;
     4. `historyStart` = `MIN(Date)` of completed load-bearing runs;
     5. completed race facts with `Date ≤ to` (for "PB at the time").
   - Every query is an index seek on `(UserId, Date)`. **No new index, view or migration.**
2. **One endpoint:** `GET /api/insights/report?from&to&compare` (operationId `getInsightsReport`, policy **Auth**, general rate limiter).
   - Validation (400, keys `from`/`to`/`compare`):
     - both dates are required and within 1950-01-01 … 2100-12-31;
     - `to ≥ from`;
     - the span is ≤ **366 days** inclusive;
     - `compare ∈ none | previous | previousYear` (default `previous`).
   - Response: `200 InsightsReport` (see `api-contract.md` once implemented, M5 plan §3.2), `Cache-Control: private, no-store`. An empty period still answers 200 (zeros and empty arrays). There is no route parameter that names a user or a resource, so there is nothing to 404.
   - Presets (this week, this month, last 12 weeks, year to date) are **client-side**. The client computes `from`/`to` from `me.today` (ADR 0017), so the API stays one generic range endpoint.
3. **No server cache in M5.** One report costs a few small index seeks plus in-memory work in the order of ms. TanStack Query caches it on the client, and run create/update/delete invalidate `getInsightsReport`. M6 adds its own summary cache keyed on (user, period, compare, `metricsVersion`, hash of the canonical report JSON). The report is deterministic, so its hash serves as the data fingerprint and no extra field is needed.
4. **M6 read-only contract.**
   - The M6 agent tools call `IInsightsReportService` **in-process**, in the request's DI scope, and therefore inherit the `Owner` filter. There is no HTTP self-call and no `IgnoreQueryFilters`.
   - The tools may project the report into smaller tool payloads, but they never compute new numbers.
   - The Domain `InsightsReport` is the source. The API DTO and the M6 tool payloads are both mappings of it, and both carry `metricsVersion`.
   - Breaking or definitional changes bump `metricsVersion` (ADR 0024 §9). Additive fields do not.
5. **Guests:** same endpoint and limits. A report reads only the guest's own copy of the demo data. No guest-specific cap is needed, because the cost is a handful of index seeks, bounded by the general token bucket.
6. **Stability tests:**
   - a Domain golden test: the demo dataset at a fixed "today" → a committed JSON snapshot of the report;
   - the OpenAPI drift check covers the DTO;
   - an Api test asserts that the endpoint is in the inventory as Auth.

## Consequences

- Always consistent with the data, because nothing is stale or needs reconciling. There is no write-path coupling: run CRUD code does not change.
- A 366-day report with a previous-year comparison reads at most ≈ 2 × 400 days of projected rows. That is fine at this scale, and the 366-day span limit bounds the work per request.
- Every dashboard view costs one API call. Revisiting it wakes SQL only if the client cache has expired, which is acceptable.
- M6 can be built and evaluated against fixed reports (demo dataset + golden snapshots) without a database.

## Alternatives considered

- **Materialized daily/weekly aggregates** (a table maintained on write, or an indexed view): faster at large scale, but it couples every run write and the guest seeding to aggregate maintenance, and it needs a backfill. Not needed at this scale.
- **SQL window functions for ACWR and monotony**: they would push the metric definitions into SQL, out of the pure Domain, where they are hard to unit-test and to version. Rejected (CLAUDE.md: metrics stay in Domain).
- **Client-side computation from `listRuns`**: duplicates the logic in TypeScript, and M6 could not reuse it. `listRuns` is also capped at 92 days. Rejected.
- **Several endpoints** (summary, series, distribution, races): more requests and more cache keys for one page, with no reuse benefit. One report is also the natural unit for M6. Rejected for M5.
- **Server cache (`IMemoryCache`) or ETag/304**: little gain for a cheap computation, and invalidation is per user. Revisit if App Insights shows otherwise (M7).

## Amended 2026-11-04 (implementation)

The decision stands; the code differs as follows (M5 plan [§12](../plans/M5-insights.md#12-implementation-notes--deviations-2026-11-04)).

- **§1 query 2 range:** it starts at `min(Monday(from), 1st of from's month, asOf) − 34 days` (`asOf` is included for a wholly future period) and ends at `RequiredFactsEnd` = the later of Sunday(to's week) and the last day of to's month. Bucket semantics: ADR 0024 amendment.
- **§1 query 3:** the comparison facts reuse the rows of query 2 that fall in the comparison window; only the part **before** query 2's start is queried (completed runs only). Query 3 is skipped when query 2 covers the whole window. A request makes 1–5 queries.
- **§1 query 5:** completed **and planned** race-category runs with `Date ≤ to` and a race record, as a scalar projection of the `Race`/`Rating` complex-type columns (no `Notes`, location or tracks).
- **§2 contract:** the response type is `InsightsReportResponse`; `compare` is a plain string parameter in OpenAPI, validated case-sensitively in the handler. Details and the intentional enum casing: [`api-contract.md`](../api-contract.md#added-in-m5-insights).
- **Note for M6 – wholly future periods:** when `from > today`, `period.asOf` (= `min(to, today)` = today) is **before** `period.from`. Then `elapsedDays = 0`, `isPartial = true`, every period-level total is zero, `comparison` is `null` (nothing has elapsed to compare), and `load.atAsOf` is dated **outside** the period (today). The M6 tools must treat such a report as "planned only" and must not narrate `load.atAsOf` as part of the period.
