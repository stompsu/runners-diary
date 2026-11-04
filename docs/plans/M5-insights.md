# M5 – Insights: plan

**Status:** Approved 2026-11-04 (all §9 defaults); implemented 2026-11-04, awaiting CI + approval (deviations: [§12](#12-implementation-notes--deviations-2026-11-04)) · **Date:** 2026-11-04 · **Author:** architect
**Builds on:**
- M2: `Runs` with `Rpe`, run-type catalog flags, `IsoWeek`, `UserToday`, the `Owner` filter.
- M3: `/insights` placeholder route, `me.today`, the fake API, the `test:tz` and `phone-la` projects.
- M4: Recharts 3.10.1 in the lazy `chart-vendor` chunk; initial load **178.7 kB** gzip against a **201.5 kB** limit (`web/scripts/bundle-budget.mjs`).

**Related ADRs:** [0008](../adr/0008-run-date-and-time-storage.md) dates · [0010](../adr/0010-current-user-and-data-scoping.md) scoping · [0011](../adr/0011-guest-accounts-lifecycle.md) guests · [0017](../adr/0017-client-dates-without-a-date-library.md) client dates · [0019](../adr/0019-frontend-test-strategy.md) tests.
**New (accepted, amended 2026-11-04):** [0024](../adr/0024-training-metrics-definitions-v1.md) metric definitions v1 · [0025](../adr/0025-insights-report-computation-and-api.md) report computation, API, M6 contract.

## 1. Goal & acceptance criteria

**Goal (brief §4 M5):** a metrics service (weekly and monthly aggregates, session load, ACWR, monotony, type distribution, pace trends), dashboard charts and period comparisons. Also from brief §1.1.8: year-to-date totals and race results history. The numbers are deterministic and versioned, because M6 narrates them read-only.

**Out of scope:**
- AI summary (M6);
- projected ACWR from planned runs (§9 Q6);
- HR-based load (TRIMP), EWMA, fitness/fatigue (CTL/ATL);
- weekly load in the calendar's week column;
- Polish translation;
- server-side caching.

**No schema change:** the RPE field already exists in the model, the API and the run form.

| # | Criterion | How verified |
|---|---|---|
| AC1 | Domain implements ADR 0024 exactly (sRPE in integer RPE·s, default RPE flagged, uncoupled 7/28 ACWR with 35-day history and `noChronicLoad`, bands from the rounded value, monotony/strain on complete weeks with population σ, totals, buckets, comparison windows incl. partial-period alignment, type distribution, easy-pace OLS, PB at the time, rounding). Each worked example in the ADR is a test. | Domain.Tests (hand-computed) |
| AC2 | `RunnersDiary.Domain` still has no package references; `Insights/` has no I/O and no clock (today is an input). | Domain.Tests architecture test |
| AC3 | Demo guarantees for **every "today" across a year**: ACWR on the Sunday of week −11 is `high` (≥ 1.31), on the Sunday of week −8 is `low`, available at `today`; easy-pace trend has ≥ 4 weekly points in the last 12 weeks; ≥ 3 completed races with results in the last 26 weeks. Golden report snapshot for a fixed today. | Domain.Tests |
| AC4 | `GET /api/insights/report?from&to&compare` returns `InsightsReport` (§3.2) with `metricsVersion: 1`, `Cache-Control: private, no-store`; 400 for missing/invalid dates, `to < from`, span > 366 days, unknown `compare`; 401 without token; empty period → 200 with zeros. | Api.Tests |
| AC5 | Isolation: two guests, two users, guest vs user: a heavy week added by A never changes B's report (totals, load, ACWR, races, `historyStart`). Endpoint appears as **Auth** in the inventory; 401 sweep covers it. | Api.Tests |
| AC6 | A fresh guest's report for the last 12 weeks matches the Domain result on the same seeded facts (spike/taper visible, races listed, `dataQuality.sessionsWithEstimatedRpe = 0`). Queries are projections (no `Notes`/tracks loaded), ≤ 5 per request. | Api.Tests (+ reviewer reads the SQL log once) |
| AC7 | `/insights` is a lazy route. Period picker: **This week / This month / Last 12 weeks (default) / Year to date / Custom**, prev/next, and comparison **Previous period / Same period last year / None**. The state lives in the URL (`?view&at&from&to&compare`), Back/Forward work, invalid params fall back to the default (`replace`), and presets come from `me.today`, never `new Date()`. | Vitest (`insightsRange`) + `test:tz` + Playwright |
| AC8 | KPI cards: distance, time, runs, elevation, load, average pace, each with a delta vs comparison (signed absolute value and %, with the direction stated in words, not only by colour/arrow; "—" when previous = 0). Partial periods are labelled ("1–4 Nov vs 1–4 Oct"). Planned summary (upcoming / missed). | Vitest + Playwright |
| AC9 | Charts (Recharts, lazy): weekly volume (metric toggle: distance/time/load/elevation; weeks or months for > 16 weeks; planned shown distinct from completed), acute vs chronic load, ACWR with labelled band areas, type distribution (horizontal bars, time share), easy-pace trend (y-axis faster-up, slope stated in text). ACWR card shows the value, band **in words**, acute/chronic, the unavailable reason, and a "what this means / does not mean" note. Race history table with PB marker. | Vitest + Playwright |
| AC10 | Accessibility: every chart is a `<figure>` with caption, a one-sentence text summary and a "Show data" table; the chart SVG is `aria-hidden` and not focusable; no information is conveyed by colour alone; axe (serious/critical = fail) on the dashboard (data, empty, insufficient history) on desktop and phone. | Playwright + axe |
| AC11 | Empty states: no completed runs in the period ("Add a run" link to the diary); ACWR insufficient history ("available after 35 days of logged training; n so far"); no easy runs → pace card explains why; data-quality note when `estimatedLoadShare > 0` or sessions lack duration. | Vitest + Playwright |
| AC12 | Phone (360×640, LA zone): single column, no horizontal page scroll (tables scroll inside a labelled, focusable region), charts ≥ 180 px high, picker usable by touch. | Playwright `phone-la` |
| AC13 | Bundle: the initial load grows ≤ 1 kB gzip over M4 (178.7 kB). The Insights route chunk and `chart-vendor` stay lazy (`bundle-budget.mjs` `LAZY_ONLY` gains the insights chunk). `insights` i18n namespace lazily registered; typed keys; no literals. | CI |
| AC14 | Run create/update/delete invalidate the insights queries; returning to `/insights` after adding a completed run shows the new totals. | Vitest + full-stack |
| AC15 | OpenAPI + TS schema regenerated; `dotnet build` warning-free; all CI jobs green; the full-stack suite includes an insights journey. | CI |

## 2. Files / projects by owning agent

### backend-dotnet
```
src/RunnersDiary.Domain/Insights/
  MetricsVersion.cs, InsightsThresholds.cs           # Current = 1; 0.80 / 1.30 / 2.0 / 35
  RunFact.cs, RaceFact.cs                            # projection inputs (§3.3)
  DefaultRpe.cs, SessionLoad.cs                      # RPE·s (long), estimated flag
  DailyLoadSeries.cs                                 # daily, acute, chronic (uncoupled), ACWR, band, peak, bandDays
  WeeklyMonotony.cs                                  # complete ISO weeks only
  PeriodTotals.cs, BucketSeries.cs                   # totals; weeks[] / months[] (reuse Calendar/Totals shape + load)
  ComparisonWindow.cs, Deltas.cs                     # ADR 0024 §5 rules
  TypeDistribution.cs, PaceTrend.cs, RaceHistory.cs
  InsightsPeriod.cs, InsightsReport.cs, InsightsReportBuilder.cs
src/RunnersDiary.Infrastructure/Insights/{IInsightsReportService.cs, InsightsReportService.cs}   # ADR 0025 §1
src/RunnersDiary.Infrastructure/DependencyInjection.cs
src/RunnersDiary.Api/Features/Insights/{GetInsightsReport.cs, InsightsContracts.cs, InsightsMapping.cs}
src/RunnersDiary.Api/Program.cs                      # MapInsightsEndpoints
src/RunnersDiary.Api/openapi/runnersdiary-api.json   # regenerated
seed/demo/runs.json, seed/demo/README.md             # step 2, only if §9 Q5 = yes
```

### frontend-react
```
web/src/app/routes.tsx                               # insights route → lazy
web/src/features/insights/
  InsightsPage.tsx, insightsRange.ts (presets, URL parse/serialize, prev/next), insightsQueries.ts
  PeriodPicker.tsx, ComparisonSelect.tsx, KpiGrid.tsx, DeltaText.tsx, PlannedSummary.tsx
  AcwrCard.tsx, MonotonyCard.tsx, DataQualityNote.tsx, InsightsEmptyState.tsx, RaceHistoryTable.tsx
  ChartFigure.tsx                                    # figure + caption + summary + <details> data table
  charts/{WeeklyVolumeChart, LoadChart, AcwrChart, TypeDistributionChart, PaceTrendChart}.tsx
  insightsFormat.ts                                  # load (AU), ratio, signed delta, period labels
web/src/features/runs/runQueries.ts                  # invalidate getInsightsReport on create/update/delete
web/src/lib/format/units.ts                          # + pace from s/km (reuse formatPace)
web/src/i18n/en/insights.json, i18n/lazyBundles.ts, i18n/i18next.d.ts, i18n/en/common.json (drop placeholder)
web/scripts/bundle-budget.mjs                        # LAZY_ONLY += insights route chunk
web/src/api/generated/schema.d.ts                    # regenerated
```

### qa-tester
```
tests/RunnersDiary.Domain.Tests/Insights/
  SessionLoadTests, AcwrTests, MonotonyTests, TotalsAndBucketsTests, ComparisonWindowTests,
  TypeDistributionTests, PaceTrendTests, RaceHistoryTests, DemoInsightsTests, InsightsGoldenTests (+ golden/demo-report-v1.json)
tests/RunnersDiary.Api.Tests/Insights/InsightsEndpointTests.cs
tests/RunnersDiary.Api.Tests/Isolation/IsolationMatrixTests.cs, Security/EndpointAuthorizationTests.cs   # + insights
web/src/test/fakeApi/insights.ts + fixtures (demo 12 weeks, empty, insufficient history, estimated RPE)
web/src/features/insights/*.test.ts(x)
web/e2e/{insights.spec.ts, insights.phone.spec.ts}, web/e2e-fullstack/insights.spec.ts
```

### azure-infra
No change. The new Playwright specs and Domain/Api tests are picked up by the existing jobs.

### architect
After implementation: `api-contract.md` (endpoint and shapes), `data-model.md` (note that no aggregates are stored; drop the "M5 aggregate views" row), `architecture.md`, ADR status and deviations (§12).

## 3. Contracts

### 3.1 Endpoint
| Method & path | operationId | Policy | Success | Errors |
|---|---|---|---|---|
| `GET /api/insights/report?from=YYYY-MM-DD&to=YYYY-MM-DD&compare=previous\|previousYear\|none` | `getInsightsReport` | Auth, general limiter | 200 `InsightsReport`, `Cache-Control: private, no-store` | 400 (`from`, `to`, `compare`), 401, 429 |

`compare` defaults to `previous`. The span is ≤ 366 days inclusive. Dates must be within 1950–2100.

### 3.2 Response (`InsightsReport`; all units SI except load in AU and pace in s/km)
```ts
InsightsReport = {
  metricsVersion: 1;
  today: string;                                    // user's zone
  period: { from: string; to: string; asOf: string /* min(to, today) */; isPartial: boolean; days: number; elapsedDays: number };
  thresholds: { acwrLow: 0.8; acwrHigh: 1.3; monotonyHigh: 2.0; acwrMinHistoryDays: 35 };
  totals: PeriodTotals;                             // completed, [from, asOf]
  planned: { runCount: number; distanceMeters: number; durationSeconds: number; upcomingCount: number; missedCount: number };  // [from, to]
  comparison: null | { kind: "previous" | "previousYear"; from: string; to: string; totals: PeriodTotals; deltas: Deltas };
  load: {
    historyStart: string | null;
    atAsOf: AcwrPoint;
    peak: AcwrPoint | null;                         // max ACWR in [from, asOf]
    bandDays: { low: number; optimal: number; high: number; unavailable: number };
    daily: { date: string; load: number; acute: number; chronic: number | null; acwr: number | null; band: Band | null }[];  // Monday(from) … min(Sunday(to), asOf)
  };
  weeks: { isoYear: number; isoWeek: number; start: string; end: string; completed: Totals; planned: Totals;
           load: number; monotony: number | null; strain: number | null; acwrAtEnd: number | null; complete: boolean }[];
  months: { year: number; month: number; start: string; end: string; completed: Totals; planned: Totals; load: number }[];
  typeDistribution: { runTypeId: number; code: string; category: "Race" | "Training" | "Other";
                      runCount: number; distanceMeters: number; durationSeconds: number; load: number }[];
  paceTrend: { runTypeCodes: ["easy"]; slopeSecondsPerKmPerWeek: number | null; excludedRunCount: number;
               weeks: { start: string; runCount: number; distanceMeters: number; durationSeconds: number;
                        paceSecondsPerKm: number; avgHeartRate: number | null }[] };
  races: { runId: string; date: string; status: "Planned" | "Completed"; runTypeCode: string; name: string;
           distanceCategory: "FiveK" | "TenK" | "HalfMarathon" | "Marathon" | "Other"; officialDistanceMeters: number | null;
           distanceMeters: number | null; resultTimeSeconds: number | null; targetTimeSeconds: number | null;
           paceSecondsPerKm: number | null; overallPlace: number | null; categoryPlace: number | null; categoryName: string | null;
           ratingStars: number | null; isPersonalBest: boolean }[];
  dataQuality: { completedSessions: number; sessionsWithEstimatedRpe: number; estimatedLoadShare: number /* 0..1, 2 dp */;
                 sessionsWithoutDuration: number; runningSessionsWithoutDistance: number };
}
PeriodTotals = { runCount: number; distanceMeters: number; durationSeconds: number; elevationGainMeters: number;
                 averagePaceSecondsPerKm: number | null; longestRunMeters: number | null;
                 load: number; loadSessionCount: number; activeDays: number; restDays: number };
Deltas = { [k in "runCount" | "distanceMeters" | "durationSeconds" | "elevationGainMeters" | "load" | "activeDays"]:
           { absolute: number; percent: number | null } };
AcwrPoint = { date: string; value: number | null; band: Band | null; acute: number; chronic: number | null;
              unavailableReason: null | "insufficientHistory" | "noChronicLoad" };
Band = "low" | "optimal" | "high";
Totals = calendar `TotalsDto` (runCount, distanceMeters, durationSeconds, elevationGainMeters)   // same schema reused
```
OpenAPI: enums as strings. `thresholds` values are numbers (not literal types) in the schema. Every array is always present (possibly empty). Worst case (366 days): about 371 daily points, 54 weeks and 13 months, which is under 40 kB of JSON.

### 3.3 Domain inputs (projection, `(Date, Id)` order)
```csharp
public sealed record RunFact(Guid Id, DateOnly Date, RunStatus Status, short RunTypeId, double? DistanceMeters,
    int? DurationSeconds, double? ElevationGainMeters, int? Rpe, Surface? Surface, int? AvgHeartRate);
public sealed record RaceFact(Guid Id, DateOnly Date, RunStatus Status, short RunTypeId, string Name, RaceDistanceCategory Category,
    double? OfficialDistanceMeters, double? DistanceMeters, int? DurationSeconds, int? ResultTimeSeconds, int? TargetTimeSeconds,
    int? OverallPlace, int? CategoryPlace, string? CategoryName, int? RatingStars);
InsightsReport InsightsReportBuilder.Build(InsightsPeriod period, ComparisonKind compare, DateOnly today,
    IReadOnlyList<RunFact> facts, IReadOnlyList<RunFact> comparisonFacts, IReadOnlyList<RaceFact> races, DateOnly? historyStart);
```
The service computes the comparison window with `ComparisonWindow.For(period, compare, today)` **before** querying (ADR 0025 §1).

### 3.4 Client URL state & presets (from `me.today`; `plainDate` helpers only)
| `view` | from … to | default `compare` |
|---|---|---|
| `week` (`at` = any date in the week) | ISO week of `at` | previous |
| `month` (`at`) | calendar month of `at` | previous |
| `12w` (default) | Monday(today) − 77 d … Sunday(today) | previous |
| `ytd` | 1 Jan … today | previousYear |
| `custom` (`from`, `to`) | as given, ≤ 366 d | previous |

Prev/next shift `at` by one week, one month or 12 weeks; they are hidden for `ytd`. Example with today = Wed 2026-11-04: `12w` is 2026-08-17 … 2026-11-08 (partial, 80 elapsed days), compared with 2026-05-25 … 2026-08-12.

### 3.5 Dashboard layout
Desktop is a 12-column grid; phone is a single column in this order:
1. header (period picker, prev/next, comparison select, period label with "partial" note);
2. KPI grid (6 cards) + planned summary;
3. ACWR card + monotony card (last complete week);
4. weekly volume chart;
5. acute vs chronic chart + ACWR chart;
6. type distribution + easy-pace trend;
7. race history table;
8. data-quality note.

`ChartFigure` renders `<figure aria-labelledby>`, then the caption, then a summary `<p>` generated from the data (for example "ACWR on 4 Nov: 1.12, optimal"), then the chart (`aria-hidden`, `accessibilityLayer={false}`, `tabIndex={-1}`), then `<details><summary>Show data</summary><table>`. Band areas get text labels, and planned bars use a pattern plus a legend, not only a lighter colour.

### 3.6 Queries
| Query | Key | Cache |
|---|---|---|
| `getInsightsReport(from, to, compare)` | `$api` query key | `staleTime` 60 s; `placeholderData: keepPreviousData` (no flash when paging); invalidated by `createRun`/`updateRun`/`deleteRun` |

## 4. Implementation steps (each = one reviewable commit)

1. **backend-dotnet (+ qa-tester): Domain metrics.**
   - `Domain/Insights/*` per ADR 0024.
   - Hand-computed unit tests (§6), demo guarantee tests over a year, and the golden snapshot for today = 2026-11-04 (Europe/Warsaw).
   - No API change yet.
2. **backend-dotnet: demo pace story** (only if §9 Q5 = yes). Lengthen the earlier easy-run durations in `runs.json` so that easy pace improves from about 5:55 to 5:40 per km over 26 weeks. Spike and taper ratios are re-asserted by the step-1 tests. Update the README "Load story" and regenerate the golden snapshot.
3. **backend-dotnet: service + endpoint.**
   - `InsightsReportService` (projections; ≤ 5 queries), `GetInsightsReport` with validation and the `no-store` header, and DI.
   - Regenerate OpenAPI and the TS schema.
   - Api.Tests (§6): validation, guest demo report, isolation, inventory.
4. **frontend-react: page shell.**
   - Lazy route, `insightsRange` + URL state, `insightsQueries`, invalidation, `PeriodPicker`, `KpiGrid`/`DeltaText`, `PlannedSummary`, `AcwrCard`, `MonotonyCard`, empty states, the `insights` i18n namespace, and the `bundle-budget` `LAZY_ONLY` entry.
   - qa-tester extends the fake API with fixtures.
5. **frontend-react: charts and tables.** `ChartFigure`, the five charts, `RaceHistoryTable`, `DataQualityNote`, and the phone layout.
6. **qa-tester: E2E.** `insights.spec.ts`, `insights.phone.spec.ts` (axe), the full-stack `insights.spec.ts`, and Vitest/`test:tz` gaps.
7. **reviewer, then architect.**
   - reviewer: isolation/IDOR, the query shape (projections, index use, no N+1), metric correctness against ADR 0024, a11y, bundle.
   - architect: docs and deviations.
   - The main agent then updates CLAUDE.md, commits "M5: insights" and stops for approval.

## 5. Risks

| Risk | Impact | Mitigation |
|---|---|---|
| ACWR misread as an injury predictor (the evidence is disputed) | Misleading advice, worse in M6 | Band wording "load change vs your recent norm"; the card states what it does not mean; ADR 0024 and the M6 prompt rules forbid injury claims. |
| Default RPE estimates distort load for users who skip RPE | Wrong ACWR | Estimates flagged; `estimatedLoadShare` shown with a nudge to log RPE; defaults versioned. |
| Uncoupled ratios differ from Garmin/TrainingPeaks | User confusion | Tooltip + docs; the definition is versioned and EWMA is possible as v2. |
| Comparison semantics (partial periods, month lengths, 29 Feb, ISO week 53) | Wrong deltas | Explicit rules in ADR 0024 §5 with table tests; the UI states both date ranges. |
| `today` from the user's zone vs a stale `me.today` near midnight | Off-by-one preset | The server computes `asOf` itself; the UI labels from the response (`period`, `today`). |
| Golden snapshot churn | Noisy diffs | Changes only with intended definition changes (they bump `metricsVersion`); regeneration is a documented test switch. |
| Fake API returns canned reports (no TS metrics) | Fake/real drift | Fixtures typed against `schema.d.ts`; the full-stack spec checks the real report on `main`. |
| Recharts SVG accessibility | Screen-reader gaps | The chart is decorative and the table + summary are the accessible path; axe in CI. |
| Report size/CPU at 366 days + previous year | Slow on a cold SQL | Bounded by the span limit; ≤ 5 seeks; measured once on the AppHost (manual). |
| Demo pace tune (Q5) shifts load figures | Spike/taper story breaks | Step 1 guarantee tests run first and guard step 2. |
| The bundle grows if a chart module leaks into the initial graph | Slower first load | Lazy route; `LAZY_ONLY` check; ≤ 1 kB budget. |

## 6. Test plan outline (qa-tester)

**Domain.Tests (`Insights/`, hand-computed, no DB):**
- **Session load:** sRPE 45:00 × 6 = 270.0. Missing RPE → type default + estimated flag. Missing duration → 0 + counter. Rest → excluded. Cross-training → load but no running totals. Planned → excluded. Completed in the future → ignored. Integer exactness: a 1-second session at RPE 1 → 0.0 rounded, but summed exactly.
- **ACWR:**
  - constant 50/day → 1.00;
  - spike 40→60 → 1.50 `high` (and the coupled value 1.33 is *not* produced);
  - day 34 vs day 35 of history → `insufficientHistory` → available;
  - a 4-week break → `noChronicLoad`;
  - band edges 0.795 → 0.80 `optimal`, 1.305 → 1.31 `high`, 1.30 → `optimal`;
  - peak tie → earliest date; `bandDays` sum = elapsed days.
- **Monotony:** [300,0,300,0,300,0,0] → 0.87 / 779.4; constant week → null; empty week → null; the current incomplete week → null.
- **Totals/buckets:**
  - average pace only over runs with both values; `activeDays` distinct; `restDays`;
  - week buckets are whole ISO weeks across a year boundary (2026-W53 → 2027-W01, ISO year ≠ calendar year);
  - month buckets across December/January.
- **Comparison windows (table):**
  - whole month → previous month (31 vs 30 days);
  - partial month 1–4 Nov → 1–4 Oct;
  - 12w → −84 d;
  - ytd previousYear (calendar shift);
  - week previousYear → −364 d;
  - 2028-02-29 → 2027-02-28;
  - `none`;
  - deltas with previous = 0 → percent null.
- **Type distribution:** ordering, rest excluded, cross-training included.
- **Pace trend:** trail surface, < 1 km and out-of-range pace excluded (counted); distance-weighted weekly pace; OLS slope [360, 357, 354, 351] → −3.00; < 4 points → null; HR weighted by duration.
- **Races:** PB at the time (later slower race not PB; tie → earlier keeps PB; `Other` never PB); planned races listed with target time.
- **Demo:** every today in a year (as `DemoDatasetTests`): AC3 guarantees. The golden JSON for 2026-11-04 equals the serialized report.
- **Architecture:** Domain has no package references; `Insights` uses no `DateTime.Now`/`TimeProvider`.

**Api.Tests (Testcontainers):**
- 400 matrix (missing, malformed, `to < from`, 367 days, out-of-range year, bad `compare`) with camelCase keys.
- 200 shape and headers.
- Empty account → zeros.
- Fresh guest, last 12 weeks: spike/taper and races present, `metricsVersion` 1.
- Guest/guest, user/user, guest/user isolation (A's heavy week and races do not appear in B).
- Expired guest → 401.
- Inventory: Auth.
- `previousYear` with two disjoint ranges returns the right comparison totals.
- A run edit changes the next report (no server cache).

**Vitest + RTL:**
- `insightsRange`: presets from `me.today`, parse/serialize, invalid → default, prev/next, the 366-day custom guard; under `test:tz` (LA, Kiritimati).
- KPI deltas (signs, "—" for null percent, words for direction).
- AcwrCard states (value, each band, each unavailable reason).
- ChartFigure (caption, summary, data table rows match the fixture).
- Empty states and data-quality note.
- Invalidation after `createRun`.
- i18n keys exist.

**Playwright (fake API):**
- `insights.spec`: default 12-week view renders KPIs and charts; switch to month via the picker → URL `?view=month&at=…`; Back restores 12w; prev/next; compare = last year; "Show data" table; ACWR band text; empty month; insufficient-history fixture; axe on all three states.
- `insights.phone.spec`: 360×640, LA zone: no horizontal scroll, single column, picker usable, axe.

**Full-stack (main only), `e2e-fullstack/insights.spec.ts`:** guest → `/insights` → ACWR card shows a value and band, the weekly chart's data table has 12 rows, races are listed → add a completed run today (RPE 5, 30 min, 5 km) in the diary → back to `/insights` → distance KPI increased by 5 km and load by 150 AU.

**Manual:** AppHost timing of a YTD + previous-year report on a resumed SQL; VoiceOver pass over a chart's summary and table; phone Safari layout.

## 7. Dependencies

**None.** Recharts 3.10.1 (+ `react-is` pin) is already installed and lazy (M4). Formatting uses `Intl`. NuGet: none (BCL only). Rejected: a stats library (an OLS slope is ~10 lines), date libraries (ADR 0017), and chart a11y plugins (data tables suffice).

## 8. What the user must do

Nothing in the cloud. Answer §9 (above all Q1, Q2 and Q5).

## 9. Open questions (recommended defaults in bold)

1. **ACWR model:** **uncoupled rolling 7/28 days (chronic = the 4 weeks before the acute week)**, or coupled rolling (the common app formula; the demo's spike would then read 1.30 "optimal" unless the data is changed), or EWMA.
2. **Missing RPE:** **use a per-type default RPE, flagged as estimated and disclosed**, or leave those sessions out of load.
3. **ACWR bands:** **low < 0.80 ≤ optimal ≤ 1.30 < high** (three bands, as in the brief), or add a fourth "very high" band above 1.50.
4. **Default dashboard view:** **Last 12 weeks vs the previous 12 weeks**, or this month vs last month.
5. **Demo easy-pace story:** **yes, tune the demo easy-run durations so easy pace improves by about 15 s/km over 26 weeks** (today it is flat at about 5:50/km), or leave the demo unchanged.
6. **Projected ACWR from planned runs:** **not in M5** (it can be added later as extra fields without a version bump), or include it now.
7. **Easy-pace trend runs:** **`easy` only, excluding trail-surface runs**, or `easy` + `recovery` + `long`.
8. **Partial periods:** **compare the same number of elapsed days** (1–4 Nov vs 1–4 Oct), or compare against the full previous period.

**Answers (user, 2026-11-04):** all recommended defaults — Q1 uncoupled rolling 7/28; Q2 per-type default RPE, flagged; Q3 three bands; Q4 last 12 weeks vs previous 12; Q5 yes, tune the demo easy pace; Q6 not in M5; Q7 `easy` only, no trail; Q8 same number of elapsed days.

## 10. Assumptions

1. RPE is the brief's 1–10 scale (CR-10 style) and is stored as entered; the load unit is "AU".
2. The profile zone gives `today` for metrics. A run's own `TimeZoneId` does not matter for metrics: its `Date` is already local (ADR 0008).
3. Cross-training adds to load and the type distribution, not to running totals or pace. Rest days add only to `restDays`.
4. "Missed" planned runs are planned runs dated before today. The app never auto-converts them.
5. Report reads are cheap enough without a server cache or a new index (ADR 0025). App Insights can prove otherwise in M7.
6. The fake API serves canned reports, and the real computation is verified by Domain/Api tests and the full-stack spec.
7. Units stay metric in the UI. Imperial (`UnitSystem`) is not in scope.

## 11. Docs checked (2026-11-04)

- Lolli et al. 2019 (coupling): https://bjsm.bmj.com/content/53/15/921
- Williams et al. 2017 / Murray et al. 2017 (EWMA): https://acuresearchbank.acu.edu.au/item/8qv67/calculating-acute-chronic-workload-ratios-using-exponentially-weighted-moving-averages-provides-a-more-sensitive-indicator-of-injury-likelihood-than-rolling-averages
- Impellizzeri et al. 2020 (ACWR pitfalls): https://pmc.ncbi.nlm.nih.gov/articles/PMC7534938/
- Foster 1998 (monotony/strain): https://scilit.com/publications/e31bb6480eb258196e899f1d8de4d3d2

**Not verified:**
- The full text of Foster et al. 2001 (sRPE) and the primary source of "monotony > 2.0".
- Recharts 3.10.1 `accessibilityLayer={false}` + `aria-hidden` behaviour. It was not re-fetched; it is the version already installed in M4. Check it in step 5.

## 12. Implementation notes & deviations (2026-11-04)

ADRs 0024 and 0025 carry an "Amended 2026-11-04 (implementation)" section for every point below that changes a decision. `metricsVersion` stays **1** (no release has served the earlier semantics). The endpoint and shapes are documented in [`api-contract.md`](../api-contract.md#added-in-m5-insights); [`data-model.md`](../data-model.md#insights-reads-m5) records that there is no schema change.

### Backend

| # | Deviation from this plan | Reason |
|---|---|---|
| 1 | **DTO names end in `…Dto`; insights-only enums are `BandDto`, `AcwrUnavailableReasonDto`, `ComparisonKindDto`** with lowerCamel JSON values (`JsonStringEnumMemberName`). Reused domain enums (`RunStatus`, `RaceDistanceCategory`, `RunTypeCategory`) stay PascalCase. The mixed casing is intentional (api-contract.md). | The contract's lowerCamel values (§3.2) without changing the API-wide enum convention. |
| 2 | Root type is **`InsightsReportResponse`** (not `InsightsReport`); the Domain record keeps the name `InsightsReport`. | No name clash between the Domain record and the wire DTO in OpenAPI/C#. |
| 3 | **`compare` is a plain `string` query parameter** in OpenAPI (no enum), validated **case-sensitively** in the handler (`errors.compare`). | A bound enum would accept numbers/other casings and produce framework binding messages. |
| 4 | **The comparison query reuses overlapping rows**: query 3 reads only the part of the comparison window before query 2's start (completed only); skipped when fully covered. | Fewer rows and queries (1–5). |
| 5 | **Facts range start = `min(Monday(from), 1st of from's month, asOf) − 34 days`** (adds `asOf` for wholly future periods); end = `RequiredFactsEnd` (later of Sunday(to's week) and end of to's month). | ACWR at `asOf` and month buckets need history before the period. |
| 6 | **`comparison` is `null` when nothing has elapsed** (`elapsedDays = 0`, wholly future period), even with `compare ≠ none`. | Nothing to compare; avoids an all-zero comparison. |
| 7 | **Planned bucket totals count running-distance types only** (same rule as the calendar's `Totals`). | Reuses the calendar `TotalsDto` semantics. |
| 8 | **Bucket totals are rounded to 0.1 m** (distance, elevation), like the period totals. | ADR 0024 §9 rounding applied consistently. |
| 9 | **`historyStart`** = the first completed load-counting session ever, **even without a duration** (a session without load still starts the diary). | The diary's start, not the first non-zero load. |
| 10 | **`avgHeartRate`** in the pace trend is rounded to **0.1 bpm**. | Not specified in the plan. |
| 11 | **`dataQuality.completedSessions` excludes rest entries.** | Rest is not a session. |
| 12 | **`restDays` = distinct dates with a completed rest entry** (not "days without a session"). | Explicit, data-driven definition. |
| 13 | **PB ties:** a same-day slower race is not a PB; equal times → the **earlier date** holds the PB (same date and time: `(Date, Id)` order). | Deterministic "PB at the time". |
| 14 | **Bucket semantics changed (review finding M1):** buckets are whole weeks/months with completed data through `min(end, today)` regardless of `asOf`; `complete = Sunday ≤ today`; `acwrAtEnd` on the cutoff, null for future weeks; planned over the whole bucket. Full rules: ADR 0024 amendment. | A bucket must show the same numbers in every period it appears in. |
| 15 | **Race query (review finding L2):** completed **and planned** race-category runs with `Date ≤ to` and a race record, as a scalar projection of the `Race_*`/`Rating_Stars` columns (no full entity, no `Notes`/location/tracks). | Planned races with targets are listed; minimal columns read. |

Accepted without change (ADR 0024 amendment): **L4** `previousYear` for a whole February after a leap year compares Feb 1–28 (2028-02-29 is never counted); **N1** deltas are computed from rounded totals (error ≤ 0.1 AU / 0.1 %).

Demo (step 2 done, §9 Q5): spike week −11 load 1 441 AU, ACWR 1.48 (high); taper week −8 632 AU, ACWR 0.55 (low); easy pace about 5:56 → 5:40 per km (slope −0.58 s/km per week). The week −3 trail-race week ends at ACWR **1.59** (`acwrAtEnd`). **Corrected 2026-11-04 (M6 review M1):** the default 12-week view's daily `peak` is **1.79 on Fri 2026-10-23 (week −2)**, 5 days after the Sunday trail race (2026-10-18), still race-driven (the race is in its 7-day acute window) but not in the race's ISO week; see the ADR 0024 amendment. Golden snapshot regenerated.

### Frontend

| # | Deviation / note | Reason |
|---|---|---|
| 1 | Vitest setup wraps Node's `FormData` in a **`Proxy`** that returns an empty `FormData` only for `new FormData(<jsdom form>)` (React 19 builds one when a submit handler starts a transition, as react-router navigations do; Node's class would throw on a jsdom form). Consequence: under test, form values must be read via react-hook-form/controlled state, never `new FormData(form)`. | Test infrastructure only; M4 multipart uploads keep Node's `FormData`. |
| 2 | **`at` is always written to the URL for `week`/`month`**, so a bookmarked month stays pinned instead of following "today". | Stable, shareable URLs. |
| 3 | **The pace KPI delta is derived client-side** from the two `averagePaceSecondsPerKm` values (the server has no pace delta) and worded "slower/faster", not "more/less". | `Deltas` has no pace member; lower pace is better. |
| 4 | Scrollable tables use **`ScrollRegion`, a labelled `<section tabindex="0">`** (not `role="region"` on a `div`). | Keyboard-scrollable, named landmark; axe clean. |
| 5 | **Toggle groups** (chart metric, weeks/months) are a **`<fieldset>` with `aria-pressed` buttons**. | Simple, accessible grouping without a new UI component. |
| 6 | **The chart components load with the Insights route chunk**; Recharts stays in the lazy **`chart-vendor`** chunk. | One fewer waterfall step; initial bundle unaffected. |
| 7 | The **`common.json` placeholder** for `/insights` was removed. | Replaced by the `insights` namespace. |
| 8 | **The empty state keeps the planned summary and the race table** (planned races still show). | A period with only plans is still useful. |
| 9 | **URL ranges outside 1950–2100 and a `ytd` `at`** are canonicalized (`replace`) to valid values. | Matches server validation; no 400 from a hand-edited URL. |

**Verified (2026-11-04):** build 0 warnings, no OpenAPI/TS drift; .NET 1455/1455 (`CI=true`, Testcontainers, incl. the GPX timing tests on a quiet machine); Vitest 799/799, `test:tz` 626 per zone; Playwright 93/93; bundle initial 178.7 kB (182,948 B, 3 B under M4) / limit 201.5 kB; local replica of the `e2e-fullstack` job 7/7 (journey, 3 tracks, 3 insights); lint, typecheck, Prettier clean.

### Still open

- **Manual pass not done:** VoiceOver over a chart's summary and data table, and the phone Safari layout (§6 Manual). The AppHost timing of a YTD + previous-year report is also manual.
- **A server 400 shows the generic error state** on the dashboard (the client canonicalizes the URL first, so it should not happen in normal use); a specific message can come later.
- **The 2 GPX simplification timing tests** (M4, Domain.Tests) are load-sensitive under full parallel runs and may flake; they are not M5 code.
