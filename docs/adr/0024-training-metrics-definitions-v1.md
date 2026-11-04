# 0024 – Training metrics v1: definitions, edge cases and versioning

**Status:** Accepted (M5 plan approved 2026-11-04) · Amended 2026-11-04 (implementation, see the end)
**Date:** 2026-11-04

## Context

Brief §1.1.8 asks for: weekly distance/time/elevation; session load = duration (min) × RPE; acute (7-day) vs chronic (28-day) load and their ratio (ACWR) with "low / optimal / high" bands; monotony; week-over-week and month-over-month comparisons; year-to-date totals; distribution by run type; pace trend for easy runs; race results history. M6's AI summary must narrate **only** these numbers, so they must be deterministic, documented and versioned. M6 reads them and never recomputes them.

Inputs that exist today (no schema change needed): `Runs.Date` (local date, ADR 0008), `Status`, `RunTypeId` (catalog flags `CountsTowardRunningDistance`, `CountsTowardLoad`, `IsRestDay`), `DistanceMeters`, `DurationSeconds`, `ElevationGainMeters`, `Rpe` (1–10, optional, already in the run form), `AvgHeartRate`, `Surface`, `Race.*`, `Rating.Stars`. The demo dataset has RPE on every completed session.

ACWR is contested in the literature. Rolling "coupled" ratios contain the acute week inside the chronic window, which creates a spurious correlation and damps spikes (Lolli et al. 2019). EWMA reacts faster (Williams et al. 2017). The ratio's value as an injury predictor is disputed (Impellizzeri et al. 2020). The app therefore shows ACWR as a **descriptive load-change indicator** and never as an injury-risk prediction.

## Decision

All of this lives in `RunnersDiary.Domain/Insights/` (pure C#, no packages). `MetricsVersion.Current = 1`.

### 1. Which runs count

| Metric family | Runs included |
|---|---|
| Everything "done" (totals, load, ACWR, monotony, distribution, pace, race results) | `Status = Completed` and `Date ≤ asOf` |
| Running totals (distance, time, elevation, count, average pace, longest run) | + run type `CountsTowardRunningDistance` (the same rule as the calendar's week totals; cross-training and rest are excluded) |
| Load | + run type `CountsTowardLoad` (cross-training included, rest excluded) |
| Planned | `Status = Planned`. Shown separately as count, distance and time. Never part of load or ACWR. `missedCount` = planned with `Date < today`; `upcomingCount` = planned with `Date ≥ today`. |

- Grouping is by the run's local `Date` only. `StartUtc` is never used.
- Weeks are ISO weeks (Monday first, `Domain/Calendar/IsoWeek`). Months are calendar months.
- `today` = `UserToday.Get(clock, user.TimeZoneId)`, the caller's profile zone (ADR 0008).
- `asOf = min(to, today)`. Completed runs dated after today cannot exist in normal use. If one does exist, it is ignored by every metric, so metrics never look into the future.

### 2. Session load (sRPE, Foster 2001)

`load = RPE × durationSeconds / 60` in **AU** (arbitrary units). Internally the load is kept as an **exact integer in RPE·seconds** (`long`) and divided by 60 only at the output boundary. Sums are therefore order-independent and bit-for-bit deterministic.

| Case | Rule | Flag |
|---|---|---|
| RPE and duration present | as above | — |
| RPE missing, duration present | the **run type's default RPE** (table below) | `estimated` (counted in `dataQuality.sessionsWithEstimatedRpe`; the share of load from estimates is `estimatedLoadShare`) |
| Duration missing | no load (0) | counted in `dataQuality.sessionsWithoutDuration` |
| Rest-day type | no load (the rules forbid RPE and duration on a rest day) | counted in `restDays` |

Default RPE by type (v1): `recovery` 2 · `easy`, `stridesDrills`, `treadmill` 3 · `crossTraining` 4 · `long`, `trailTraining` 5 · `fartlek`, `progression` 6 · `tempo`, `racePace` 7 · `intervals`, `hillRepeats` 8 · `ultra` 8 · other race types (`roadRace`, `trailRace`, `trackRace`, `crossCountry`, `virtualRace`) 9.

Worked example: 45:00 at RPE 6 → 6 × 2 700 = 16 200 RPE·s → **270.0 AU**.

### 3. Daily load, acute, chronic, ACWR (uncoupled rolling 7 / 28)

- `daily(d)` = sum of session loads dated `d`. Days without sessions count as 0.
- `acute(d)` = Σ `daily(d−6 … d)`, the last 7 days.
- `chronic(d)` = Σ `daily(d−34 … d−7)` / 4, the **28 days before the acute week**, expressed per 7 days so that it has the same scale as acute (**uncoupled**).
- `ACWR(d) = acute(d) / chronic(d)`, computed as `(decimal)(4 × Σacute) / Σchronic` on the integer RPE·s sums, so midpoints such as 1.305 round exactly, then rounded to **2 decimals** (midpoint away from zero).
- **Availability** (`value = null` plus `unavailableReason`):
  - `insufficientHistory` when `d − 34 < historyStart`. `historyStart` is the date of the user's first completed load-bearing session ever. ACWR therefore needs **35 days** of diary history, and time before the diary started is never treated as rest.
  - `noChronicLoad` when `Σchronic = 0`, for example after a 4-week break.
- **Bands** are computed from the rounded value: `low` < 0.80 ≤ `optimal` ≤ 1.30 < `high`. The thresholds are returned in the report, so neither the UI nor M6 hard-codes them.
- The report gives the ACWR at `asOf`, its peak in the period (the earliest date on ties), the number of days in each band, and the daily series.

Worked example: 28 days at 40 AU per day, then 7 days at 60 AU per day. Acute = 420 and chronic = 1 120 / 4 = 280, so **ACWR = 1.50 (high)**. The coupled formula would give 420 / ((840 + 420) / 4) = 1.33.

### 4. Monotony and strain (Foster 1998), per complete ISO week

- Only weeks whose Sunday is ≤ `asOf` get a value. The current, incomplete week gets `null`.
- `mean` and `σ` are taken over the **7 daily loads, zeros included**. σ is the **population** standard deviation (÷ 7), because the week is the whole population.
- `monotony = mean / σ`. It is `null` when σ = 0 (all days equal, including a week with no load).
- `strain = weekly load × monotony`. It is `null` when monotony is `null`.
- Threshold `monotonyHigh = 2.0`. Foster 1998 is commonly cited for this value, but it is reported for context only.

Worked example: daily loads [300, 0, 300, 0, 300, 0, 0] give mean 128.57 and σ 148.46, so **monotony 0.87 and strain 779.4**.

### 5. Totals, buckets and comparisons

- **Period totals** cover completed runs in `[from, asOf]`:
  - `runCount`, `distanceMeters`, `durationSeconds` (as `long`), `elevationGainMeters` (missing values count as 0)
  - `averagePaceSecondsPerKm` = Σduration / Σdistance × 1 000, only over running sessions that have **both** values; `null` if there are none
  - `longestRunMeters`
  - `load`, `loadSessionCount`
  - `activeDays` (distinct dates with a completed non-rest session) and `restDays`
- **Buckets:** `weeks[]` are the whole ISO weeks that cover `[from, to]`, and `months[]` are the whole calendar months that cover it. Each bucket contains completed totals, planned totals and load. A weekly bucket also contains monotony, strain and `acwrAtEnd` (ACWR on `min(Sunday, asOf)`). Buckets may extend outside the period, and their `start`/`end` say so.
- **Comparison windows** (`compare`):
  - `previous`: if `[from, to]` is a run of whole calendar months, shift by that number of months. Otherwise shift by the period length L in days.
  - `previousYear`: if `from` is a Monday and L is a multiple of 7, shift by 364 days (same weekdays). Otherwise shift by one calendar year, with 29 Feb becoming 28 Feb.
  - `none`: no comparison.
  - **Partial periods** (`to > today`) are compared over the **same number of elapsed days**. The comparison runs from the shifted start for `asOf − from + 1` days, clamped to the shifted end. For example, the month of November viewed on 4 Nov is compared with 1–4 Oct, not with all of October.
- **Deltas**, for runCount, distance, duration, elevation, load and activeDays: `absolute = current − previous`, and `percent = absolute / previous × 100` rounded to 1 decimal. `percent` is `null` when previous = 0.

### 6. Type distribution

Completed sessions in `[from, asOf]` grouped by run type. Each group has `runCount`, `distanceMeters`, `durationSeconds` and `load`. Rest is excluded (see `restDays`). Groups are ordered by duration descending, then catalog order. Shares are derived by the client. Time is the default share basis, because cross-training has no distance.

### 7. Easy-pace trend

- Runs counted: completed `easy` runs in `[from, asOf]` with distance ≥ 1 000 m and duration present. Runs with `Surface = Trail` are excluded, and so are runs whose pace is outside **150–900 s/km**, which treats the value as a data-entry error. Excluded runs are counted in `excludedRunCount`.
- Weekly points use the ISO weeks of the series. Each point has: `paceSecondsPerKm = Σduration / Σdistance × 1 000` (distance-weighted, not a mean of paces), `runCount`, the sums, and `avgHeartRate`. The heart rate is the duration-weighted mean over the runs that have one, otherwise `null`.
- `slopeSecondsPerKmPerWeek` is an ordinary least-squares fit of the weekly pace against the week index (weeks since the first ISO week of the series; empty weeks are skipped, not zero). It needs **≥ 4 points**, otherwise it is `null`. A negative slope means faster.
- Worked example: paces 360, 357, 354, 351 at weeks 0–3 give a **slope of −3.0 s/km per week**.

### 8. Race results

- Covered: race-category runs in `[from, to]`, both completed (results) and planned (upcoming, with the target time).
- `resultTimeSeconds` = `Race.ResultTimeSeconds ?? DurationSeconds`.
- `isPersonalBest` means **a PB at the time**: the fastest completed result in the same `DistanceCategory` (`Other` is excluded) on or before that date, among all of the user's races. On a tie, the earlier race holds the PB.

### 9. Precision, rounding, versioning

- Outputs are rounded only at the boundary: load 0.1 AU, ratios (ACWR, monotony) 0.01, pace 0.1 s/km, slope 0.01, percentages 0.1, metres 0.1, seconds whole. Midpoints round away from zero.
- Facts are processed in `(Date, Id)` order.
- **`metricsVersion`** is in every report. Any change to a definition, default RPE, threshold, window or rounding rule **bumps the version** and is recorded in a new ADR that supersedes this one. Additive fields do not bump it.
- A golden report test (the demo dataset at a fixed "today") makes any unintended change fail.

## Consequences

- No migration is needed. Every input already exists, and the RPE field is already in the run form.
- The demo dataset tells its story under these rules. Hand-computed: spike week −11 has load 1 445 AU against an uncoupled chronic of 978 AU, so **ACWR ≈ 1.48 (high)**. The coupled formula would give ≈ 1.30, which is optimal. Taper week −8 has 636 AU against 1 157 AU, so **≈ 0.55 (low)**. A Domain test locks both in for every "today" across a year.
- Estimated RPE keeps load continuous for users who skip RPE. It is always disclosed (`dataQuality`), and M6 must mention it when `estimatedLoadShare` is large.
- Uncoupled ratios run higher than coupled ones and differ from Garmin/TrainingPeaks figures. A tooltip and the docs say so.
- Planned runs never affect ACWR. A "projected ACWR" from plans is possible later (as new fields, without a version bump).

## Alternatives considered

- **Coupled rolling 7/28**: the most common in apps, but it damps exactly the spikes the demo and M6 need to call out, and it has the coupling artefact. Rejected.
- **EWMA (λa = 2/8, λc = 2/29)**: more responsive and smooth. It is harder to verify by hand and to explain, it needs a seed value and a longer warm-up, and the brief explicitly says "7-day vs 28-day". Possible as `metricsVersion` 2.
- **Missing RPE → session excluded**: load would drop silently for users who skip RPE, which would produce false "low" ACWR values. Rejected.
- **Missing RPE → user's own median RPE for that type**: more personal, but one run's estimate would then change when other runs change, which is less explainable. Revisit if users ask.
- **HR-based load (TRIMP) or Banister fitness/fatigue (CTL/ATL/TSB)**: they need max/rest HR or a different model. Out of scope for the brief.
- **Sample σ (÷ 6) for monotony**: equally valid. Population σ was chosen and documented.

## References (checked 2026-11-04)

- Lolli et al., "Mathematical coupling causes spurious correlation within the conventional acute-to-chronic workload ratio calculations", BJSM 2019;53(15):921–922: https://bjsm.bmj.com/content/53/15/921
- Williams et al., "Better way to determine the acute:chronic workload ratio?", BJSM 2017;51(3):209 (EWMA): https://acuresearchbank.acu.edu.au/item/8qv67/calculating-acute-chronic-workload-ratios-using-exponentially-weighted-moving-averages-provides-a-more-sensitive-indicator-of-injury-likelihood-than-rolling-averages (Murray et al. 2017, the companion study)
- Impellizzeri et al., "Acute:Chronic Workload Ratio: Conceptual Issues and Fundamental Pitfalls", IJSPP 2020;15(6):907–913: https://pmc.ncbi.nlm.nih.gov/articles/PMC7534938/
- Foster 1998, "Monitoring training in athletes with reference to overtraining syndrome", MSSE 30(7):1164–1168 (monotony, strain): https://scilit.com/publications/e31bb6480eb258196e899f1d8de4d3d2
- ⚠ The full text of Foster et al. 2001 (sRPE, JSCR) and the original source of the "monotony > 2.0" threshold were not verified; only secondary sources were found. The threshold is informational only.

## Amended 2026-11-04 (implementation)

The decision stands; the code differs as follows (M5 plan [§12](../plans/M5-insights.md#12-implementation-notes--deviations-2026-11-04)). **`metricsVersion` stays 1**: the change below was made before any release, so no report with the earlier bucket semantics was ever served.

### Bucket semantics (replaces the "Buckets" bullet of §5 and the first bullet of §4; review finding M1)

Buckets describe **whole calendar units**, not the clipped period. A bucket therefore shows the same numbers whichever period it appears in.

- **Completed data for buckets:** every completed fact with `Date ≤ today` feeds the buckets, **regardless of `asOf`** (including days before `from` and after `to`). Completed facts dated after today are still ignored (§1).
- **Week buckets:** whole ISO weeks, Monday–Sunday, from Monday(`from`) through the week that contains `to`. For each week, `cutoff = min(Sunday, today)`:
  - `completed` totals and `load` cover **[Monday, cutoff]**;
  - `complete = Sunday ≤ today`; `monotony` and `strain` are computed **only when `complete`** (otherwise `null`);
  - `acwrAtEnd` = ACWR on `cutoff`; `null` when `cutoff < Monday` (a wholly future week);
  - `planned` totals cover the **whole week** [Monday, Sunday].
- **Month buckets:** 1st … last day, from `from`'s month through `to`'s month. `cutoff = min(monthEnd, today)` for `completed` and `load`; `planned` covers the whole month.
- **Internal daily load series:** starts at `min(Monday(from), 1st of from's month, asOf) − 34 days` and ends at `max(asOf, min(today, Sunday of to's week))`, so every bucket's load, monotony and `acwrAtEnd` can be read from it.
- **Unchanged:**
  - the public `load.daily` array still covers **[Monday(from), asOf]**;
  - every period-level metric (totals, comparison and deltas, `peak`, `bandDays`, ACWR at `asOf`, type distribution, pace trend, races) still covers **[from, asOf]** as in §1–§8.
- **Facts query** (ADR 0025 §1, query 2): it still ends at `RequiredFactsEnd` = the later of Sunday(to's week) and the last day of to's month, because future **planned** runs are needed for the planned bucket totals. Completed runs are filtered to `≤ today` in the builder, not in SQL. A request still makes 1–5 queries.

### Demo numbers (replaces the figures in §Consequences)

After the easy-pace tune (M5 plan §9 Q5) the golden report gives:
- spike week −11: load **1 441 AU** (1 440.7; was 1 445), ACWR **1.48 (high)**, unchanged;
- taper week −8: load **632 AU** (632.2; was 636), ACWR **0.55 (low)**, unchanged;
- easy pace improves from about **5:56 to about 5:40 per km** over 26 weeks (slope **−0.58 s/km per week**).
- The trail-race week (week −3) peaks at **ACWR 1.59**, higher than the story spike. The default 12-week view's `load.peak` is therefore the race week, not week −11. This is correct behaviour; M6 must narrate it as a race week, not as a training spike.

### Accepted edge cases (review findings)

- **L4 – `previousYear` after a leap year:** for a whole February of the year after a leap year (e.g. 2029-02-01 … 2029-02-28) the comparison is 2028-02-01 … 2028-02-28; **2028-02-29 is never counted**. The "shift by one calendar year, 29 Feb → 28 Feb" rule of §5 is kept, because it is simple and stated; a month-aware exception would complicate the window rules for one day in four years.
- **N1 – deltas from rounded totals:** `absolute` and `percent` are computed from the already-rounded totals (load 0.1 AU, metres 0.1). The error is at most 0.1 AU / 0.1 %, and it guarantees that `current − previous = absolute` holds exactly for the numbers the UI and M6 show.
