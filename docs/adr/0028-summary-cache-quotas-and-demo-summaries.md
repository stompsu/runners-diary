# 0028 – AI summaries: cache by fact-sheet fingerprint, explicit generation, durable quotas, demo example summaries

**Status:** Accepted (M6 plan approved 2026-11-04) · Amended 2026-11-04 (implementation, see the end)
**Date:** 2026-11-04

## Context

Brief §2: cache generated summaries per user + period, and regenerate on demand or when the data changes. Brief §1.3 asks for an **AI summary limited for guests** (e.g. 3 generations per guest) and **pre-generated cached summaries for the demo periods**, so the feature works without spending tokens.

Constraints from earlier decisions:
- Scale-to-zero API with 1–2 replicas: in-memory counters reset on restart (ADR 0011).
- Guests can be created at 60 per hour globally and 5 per hour per client.
- Serverless SQL should not be woken without need.
- Every user-owned row is filtered by `Owner` (ADR 0010).
- The report is deterministic for a given (data, today) pair (ADR 0025 §3).
- The demo dataset is week-relative and materialized at guest creation in Europe/Warsaw (ADR 0011).

Cost model from ADR 0027: ≈ $0.012 per generation.

## Decision

### 1. Storage: one row per (user, period, compare, language)

New table `AiSummaries` (user-owned, `Owner` filter, FK → `Users` ON DELETE CASCADE, so guest cleanup removes it):
- the key `(UserId, PeriodFrom, PeriodTo, Compare, Language)` (unique, clustered);
- the validated draft and its resolved `values`, as JSON (`ContentJson`);
- the **validity tuple**: `Fingerprint` (SHA-256 of the canonical fact sheet, ADR 0026 §1), `MetricsVersion`, `FactSheetVersion`, `PromptVersion`, `ModelDeployment`;
- the reported `ModelId`, token counts and `CreatedAt`.

A generation **upserts** the row; there is no history. Rows per account are capped (users 1 000, guests 20); on insert the oldest row by `CreatedAt` is deleted. Only **model** output is stored: demo and template summaries are computed on read.

### 2. Validity, not invalidation

On read, the API recomputes the report and its fact-sheet fingerprint. A stored row is:
- **fresh** if the whole validity tuple equals the current one (the fingerprint, `metricsVersion`, `factSheetVersion`, current `promptVersion` and the configured deployment);
- **stale** otherwise.

- Nothing has to be invalidated on write. Run create, update and delete, metric or prompt version bumps, and a model change all show up as a different tuple. Run CRUD code does not change.
- A partial (current) period also turns stale when `today` moves, because `asOf`, `elapsedDays` and ACWR at `asOf` change. The UI says "newer data is available", not "your data changed".
- A stale summary is still shown, labelled with its creation date. Its numbers come from its stored `values`, so the text and its numbers stay consistent.

### 3. Generation is always an explicit user action

- `POST /api/insights/summary` **generates only on request**: never on page view, never automatically after a data change (plan §9 Q3).
- `regenerate: false` ("Generate") returns an existing **fresh** row without calling the model or using quota.
- `regenerate: true` ("Regenerate") always calls the model. Template periods (ADR 0026 §4) never call it.
- **One generation per user at a time:** a per-user in-flight guard, so a second request gets 409 `summary-in-progress`.
- **At most `Ai:MaxConcurrentGenerations` (2) per replica:** a third concurrent request gets 503 `ai-busy` with `Retry-After: 10`.
- Each attempt times out after `Ai:AttemptTimeout` (30 s), with ≤ 2 attempts (ADR 0026 §3).
- Client abort cancels the model call (`RequestAborted`).

### 4. Durable quotas from a usage ledger

New table `AiGenerations` (append-only ledger, one row per POST that reached the model):
- `UserId`, `IsGuest`, `CreatedAt`;
- `Outcome` (`Succeeded`, `InvalidOutput`, `ProviderError`, `Timeout`, `Cancelled`), `Attempts`;
- input/output tokens, duration, `PromptVersion`, `ModelDeployment`.

It is a **system ledger**: not `IUserOwned`, with no `Owner` filter and **no FK to `Users`**. Global budgets must survive guest cleanup and replica restarts. It is touched only by `AiUsageLedger`, which always filters by the caller's id explicitly or aggregates globally. This is the second documented exception to "everything user-related is filtered", after `GuestCleanup` (ADR 0010 §6); the reviewer checks it. Rows older than 30 days are purged by the existing cleanup service, in the same batch loop. It holds no content, only ids, counts and timings.

Quotas, checked before the model call. **Every attempt that reached the provider counts, whatever its outcome**, because the tokens were spent (plan §9 Q7):

| Quota | Default | Config | Answer |
|---|---|---|---|
| User generations, rolling 24 h | 10 | `Ai:Quotas:UserPer24h` | 429 `ai-quota` (`scope: account`, `resetsAt`, `Retry-After`) |
| Guest generations per account (lifetime ≤ 24 h) | 3 | `Ai:Quotas:GuestPerAccount` | 429 `ai-quota` (`scope: account`, `resetsAt: null`) |
| All generations, rolling 24 h | 200 | `Ai:Quotas:GlobalPer24h` | 429 `ai-quota` (`scope: global`) |
| Guest generations, rolling 24 h | 60 | `Ai:Quotas:GuestGlobalPer24h` | 429 `ai-quota` (`scope: global`) |

- The check-then-insert race can overshoot by at most `MaxConcurrentGenerations − 1` per replica. That is acceptable, the same as ADR 0011.
- Burst control on top: rate policy **`ai-summary`** (sliding window, per caller: users 6, guests 3 per 10 min). It replaces the general limiter on the POST.
- Worst-case spend is bounded by the global row: ≈ 200 × $0.0124 ≈ **$2.5/day**, even under guest farming. Guest farming alone is capped at 60 per day. (**Superseded by the amendment at the end:** the global budgets count attempts, and the ceiling is recomputed.)

### 5. Demo example summaries for guests (no tokens)

- **Fixtures as data:** `prompts/training-insights/demo/<lang>/<preset>.v<N>.json`. M6 ships one: **`12w-previous`**, the dashboard's default view ("Last 12 weeks" vs the previous 12).
  - The text uses the same placeholder format, and its numbers resolve from the **guest's current** fact sheet. The numbers are therefore always right, even after edits.
  - A fixture carries **`requires` predicates** on fact-sheet values (`eq`, `lt`, `lte`, `gt`, `gte`, `exists`), e.g. `week.1.acwrBand eq high` (the spike, week −11), `week.4.acwrBand eq low` (taper, week −8), `acwr.peak.isRaceWeek eq true` (peak in week −2, driven by the week −3 trail race; **corrected 2026-11-04**, review M1: the earlier text placed the peak in week −3), `pace.slope lt 0`, `races.completedCount gte 2`. **The fixture is served only while every predicate holds**, so it can never contradict the guest's own data.
  - If a guest's edit breaks a claim, the fixture disappears and the card offers "Generate", which uses quota.
- **Served on read** to guests only, when:
  - the requested `(from, to, compare)` equals the preset computed from the guest's `today`, and
  - no fresh stored row exists.
- Read precedence: template period (ADR 0026 §4) → fresh stored → demo fixture → stale stored → none.
- It has `source: "demo"`, the UI labels it **"Example summary for the demo data"**, and it is not counted.
- **Authored by hand** in M6 (no model exists before M7) and reviewed against the eval rules. It must pass `SummaryValidator` against the pristine demo fact sheet (a startup check, like `DemoDatasetStartupCheck`, and a test).
- **Consistency with the golden metrics:** a Domain/Ai test runs the pristine demo for **every "today" across a year** (as `DemoInsightsTests` does) and asserts that all predicates hold and the fixture validates. A demo-data change that breaks the story fails CI.
- Users never get demo fixtures. Guests with AI configured can still generate up to 3 times for any period.

### 6. Read path cost

- `GET /api/insights/summary` makes the report's 1–5 queries + 1 row read + 1 ledger count. The SPA requests it in parallel with the report. (Amended: as implemented, up to 3 ledger queries, and the SPA requests it after the report; see the end.)
- **No server-side cache of reports or fingerprints:** the report is milliseconds of work (ADR 0025 §3).
- The client invalidates `getInsightsSummary` on run create, update and delete, as it does for the report.

## Consequences

- Cost is bounded, observable (ledger + OpenTelemetry token metrics) and only ever caused by a click.
- Staleness is exact and needs no write-path coupling. The trade-off is one extra report computation per summary read.
- Guests see a meaningful summary on the default view with zero spend and zero configuration, also in CI and in the full-stack journey, because the demo fixture does not need a model.
- The ledger has no FK, so account deletion (post-MVP) must purge it explicitly. The 30-day retention bounds it meanwhile.
- One stored row per key: a regenerate replaces the old text, and there is no "undo".

## Alternatives considered

- **Invalidate on run write** (delete summaries on CRUD): couples the run handlers to AI. It misses metric, prompt and model changes and cannot tell which periods a run affects without recomputing. Rejected in favour of the fingerprint.
- **Auto-regenerate when stale on view:** surprising spend on every edit and view. Rejected by default; the user can opt in later (§9 Q3).
- **In-memory quotas** (like the GPX upload limits): reset on scale-to-zero restarts, so guest farming could exceed any budget. Rejected for AI because spend is real money.
- **Seeding demo summaries as rows at guest creation:** they turn stale at the first midnight (partial-period facts change) and need re-seeding. Predicate-guarded fixtures resolved on read stay valid for the guest's whole lifetime. Rejected.
- **Pre-generating demo summaries with the model at build time:** needs a model before M7, and its numbers would be frozen. Rejected for M6. A model-written fixture can replace the hand-written one later, after review.
- **Asynchronous generation (202 + polling):** needed only if generation took minutes. A synchronous call with a 30 s per-attempt timeout is simpler. Rejected.

## References (checked 2026-11-04)

- ASP.NET Core rate limiting (per-endpoint policies, partitions; page dated 2026-10-27): https://learn.microsoft.com/aspnet/core/performance/rate-limit?view=aspnetcore-10.0
- Azure retail prices (gpt-5.4-mini Data Zone 0.825 / 4.95 USD per 1M): see ADR 0027.
- ADRs 0010, 0011, 0024, 0025 (this repo).

## Amended 2026-11-04 (implementation)

Details and review findings: [M6 plan §12](../plans/M6-ai-summary.md#12-implementation-notes--deviations-2026-11-04).

### Quotas: generations per account, attempts globally (replaces the table's counting rule in §4)

| Quota | Counts | Default |
|---|---|---|
| User, rolling 24 h | **generations** (ledger rows), every outcome except `Throttled` | 10 |
| Guest, per account | **generations** (ledger rows), every outcome except `Throttled` | 3 |
| Global, rolling 24 h | **attempts**: the sum of `Attempts` over all rows, every outcome | 200 |
| Guest-global, rolling 24 h | **attempts**: the sum of `Attempts` over guest rows | 60 |

- One generation is up to 2 attempts (ADR 0026 §3). Counting attempts keeps the global budgets a fixed number of model calls, so the cost ceiling no longer doubles when outputs are invalid.
- `Attempts` counts the calls that may have been billed, timeouts and provider errors included; a throttled or never-sent call is not included.
- The 24 h window excludes its start boundary (`CreatedAt > now − 24 h`). A row stops counting exactly 24 h after it was written.
- `resetsAt` (users only) is the time the oldest counted row ages out far enough to bring the count below the limit.

**New outcome `Throttled = 5`** (stored `tinyint`):
- written when the provider answers 429 after the transport retry, **and only if an earlier attempt of the same generation was billed**;
- it counts toward the **global attempt budgets only** and never toward the caller's own quota;
- a 429 on the first attempt writes no row. The answer is 503 `ai-busy` (ADR 0027 amendment).

Credential failures write no row (nothing was sent).

### Cost ceiling (replaces "$2.5/day" in §4)

Per HTTP call, at the fact-sheet cap: ≈ $0.0157 (ADR 0027 amendment).

| Scenario | Calls per day | Per day | Per month |
|---|---|---|---|
| Global budget, each attempt billed once | 203 (200 + worst overshoot: one admitted generation just under the limit and the per-replica check-then-insert race) | ≈ **$3.18** | ≈ $95 |
| Same, but every attempt also used the SDK's one transport retry and both calls were billed | 406 | ≈ **$6.37** | ≈ $191 |
| Guest share (60 attempts, ×1 or ×2) | 60–120 | ≈ $1–2 | — |
| *For comparison:* the plan's row-based ceiling (200 generations × 2 attempts × 2 transport calls) | 800 | ≈ $12.5 | — |

- **The M7 budget alert is sized to this table** (warn at the typical level, alert near $6.4/day). Typical calls cost about $0.007, so real spend is well below these figures.
- The per-replica overshoot of the check-then-insert race is accepted (unchanged).

### Ledger operations

- **Purge:** the guest cleanup run (`GuestCleanup`) calls the ledger purge through `EfAiGenerationStore`. It deletes rows older than **30 days** (`AiUsageLedger.Retention`) with one set-based delete, after the guest batch of each run. A purge failure is logged and never blocks guest removal; the next run retries.
- **Reads:** the per-user ledger reads use the caller's id from claims (the current user), never a request value.
- `AiSummaries.ModelId` is truncated to 100 characters (the column length). The provider's model id is untrusted for length and audit-only.

### Read path (§5, §6)

- The read order for GET is unchanged: template period → fresh stored row → guest demo fixture → stale stored row → none.
- Demo and template summaries are returned with `state: "fresh"` (they are computed from the current data on every read).
- Freshness ignores the deployment name when `Ai:Provider = None`. Otherwise a stored row would turn stale just because the model was switched off.
- `AiSummaries.CreatedAt` is truncated to whole seconds before storing (`datetime2(0)`), so the POST response equals later reads.
- **Info (review):** the SPA requests the summary **after** the report, not in parallel. A GET makes up to **3 ledger queries**: the user's generation times, then the global attempts and the guest attempts. Both are negligible at this scale; not changed.
