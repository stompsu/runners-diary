# 0039 – Training goals: model, rules, and the realistic-target estimate

**Status:** Accepted (M9 plan approved 2026-11-16; amended 2026-11-16 after the implementation and the review, see the end; **the §2 `raceRunId` rule is refined for an unchanged link**)
**Date:** 2026-11-16
**Related:** [0008](0008-run-date-and-time-storage.md) (dates), [0010](0010-current-user-and-data-scoping.md) (scoping), [0012](0012-request-validation.md), [0013](0013-persistence-model-and-migrations.md), [0024](0024-training-metrics-definitions-v1.md) (metrics), [0036](0036-race-catalogue-model-and-race-runs.md) (race runs), [0040](0040-training-plan-generation.md), [0041](0041-training-suggestions-storage-and-calendar.md), [M9 plan](../plans/M9-adaptive-training-plan.md)

## Context

M9 lets a runner state a **goal** ("10k under 40:00", "44 km trail with 1,000 m", "the Gliwice half on 2027-04-11") and get AI-suggested trainings towards it (ADR 0040, 0041). The brief listed a "training plan generator" as out of MVP scope; M9 is a post-MVP milestone the user approved on 2026-11-16.

Constraints:
- Every user-owned row is `Owner`-filtered and cascades with the user (ADR 0010, 0011).
- SI units in storage; numbers are computed in Domain, never by a model (brief §1.1.8).
- SQL Server rejects **multiple cascade paths**. `Users → Runs` and `Users → Goals` both cascade, so any `SET NULL` or `CASCADE` between `Goals` and `Runs` creates a second path. M4 solved the same problem for `GpxTracks` with a composite FK and no FK to `Users` (data-model.md, *Ownership rationale*).
- A goal is training content with a lifecycle and actions, not a set-and-forget preference (so not under `/settings`) and not a read-only report (so not on Insights).

## Decision

### 1. Goal types and fields

`Goals` (one row per goal, `IUserOwned`, `Owner` filter, FK → `Users` ON DELETE CASCADE):

| Type | Meaning | Required | Not allowed |
|---|---|---|---|
| `Race` | A named event | `eventName` (1–150) | — |
| `Performance` | A distance in a target time | `targetDurationSeconds` | — |
| `Distance` | Complete a distance, optionally with elevation | — | `targetDurationSeconds` (use `Performance`) |

Common fields: `title` (1–120, required), `targetDate`, `distanceMeters`, optional `elevationGainMeters`, `surface` (existing `Surface` enum), `location` (the existing `RunLocation` complex type), `raceRunId`, `notes` (≤ 1,000), `status` (`Active`, `Achieved`, `Archived`), plus plan metadata written only by plan generation (ADR 0040) and a `RowVersion` (API `version`, required on PUT like runs).

- **Short-term vs long-term is derived, never stored:** `term = ShortTerm` when `targetDate − today ≤ 84 days` (12 weeks) in the caller's zone, else `LongTerm`.
- **Presets are a UI concern** (5K 5,000 m; 10K 10,000 m; half 21,097.5 m; marathon 42,195 m; trail 25K 25,000 m + 800 m; trail 44K 44,000 m + 1,000 m; custom). The API stores only the values.
- **Caps:** users 50 goals, guests 10 (`Goals:MaxGoalsPerUser`, `MaxGoalsPerGuest`) → 409 `goal-limit`.
- **Status change away from `Active`** deletes the goal's suggestions (Pending and Dismissed) in the same transaction. Accepted runs stay (they are ordinary runs).
- **A change of a plan-relevant field** (`type`, `targetDate`, `distanceMeters`, `elevationGainMeters`, `targetDurationSeconds`, `surface`) sets `PlanOutdated = 1`; the suggestions stay, and the UI offers "Regenerate". A new generation clears the flag.

### 2. Hard rules (Domain `GoalRules`, 400 per ADR 0012, camelCase paths)

| Path | Rule |
|---|---|
| `distanceMeters` | 1,000 … 250,000 |
| `elevationGainMeters` | 0 … 10,000 |
| `targetDate` | `today + 7` … `today + 730` (caller's zone). Checked **on create and when the date changes**; an unchanged date on update is not re-checked, so an approaching goal stays editable. |
| `targetDurationSeconds` | the implied pace `targetDurationSeconds / (distanceMeters / 1000)` is within 150 … 900 s/km (2:30–15:00 min/km) |
| `title`, `eventName`, `notes`, `location.name` | lengths as above (location as in `RunRules`) |
| `type` / `status` / `surface` | known enum values; type-specific requirements of §1 |
| `raceRunId` | the caller's own run (else 400 "Unknown run." — not 404, it is a body member), of a Race-category type, `Planned`, and dated on `targetDate` |

`raceRunId` is optional. A planned race run created by the M8 signup is the typical link. When the run later moves, the goal keeps its own date; `GoalDto.raceRunDate` exposes the run's current date and the UI shows "Your race run is now on …" with a one-click update (a normal PUT).

### 3. Links between goals and runs: composite FKs, NO ACTION, cleared by the application

Two links, both nullable:
- `Goals.RaceRunId` → the linked planned race run;
- `Runs.GoalId` → the goal whose suggestion created the run (ADR 0041 §3): the "from plan" marker, and how the next generation recognises accepted sessions.

Both are **composite** FKs to the existing alternate keys, so the database also enforces "same owner" (the M4 `GpxTracks` pattern): `(RaceRunId, UserId) → Runs(Id, UserId)` and `(GoalId, UserId) → Goals(Id, UserId)` (new `AK_Goals_Id_UserId`). Both are **ON DELETE NO ACTION** because `SET NULL` would add a second cascade path from `Users`. Instead:
- **Deleting a goal** first runs `UPDATE Runs SET GoalId = NULL WHERE GoalId = @id` (Owner-scoped), then deletes the goal; its suggestions cascade (ADR 0041 §1). One transaction.
- **Deleting a run** first runs `UPDATE Goals SET RaceRunId = NULL WHERE RaceRunId = @id` in the same transaction as the run delete (a change to `DeleteRun`; the per-user track lock order of ADR 0023 is unchanged).
- **Guest cleanup** deletes `Users` rows; both `Goals` and `Runs` are removed by their cascades in the same statement, so the NO ACTION FKs between them are satisfied at the end of the statement. ⚠ **Verify on Testcontainers SQL in step 2** (a guest with a goal linked to a run and a run linked to the goal). **Fallback** if SQL Server rejects it: drop the two FKs and keep the columns as application-validated soft links (precedent: `AiGenerations.UserId`), with the clears above unchanged.

### 4. Realistic-target estimate (Domain, pure, never blocking)

`POST /api/goals/estimate` takes the goal's planning fields and returns a prediction and warnings. It is a computation without side effects (POST because the input is a small document; the general rate limiter applies). Warnings **never block** saving a goal.

**Inputs** (read through the `Owner` filter): completed runs of the last 112 days (16 weeks) as `RunFact` projections, and race facts (ADR 0024 `RaceFact`).

**Prediction** (`RaceTimePredictor`):
- **Effort distance** (ITRA-style "km-effort"): `E = distanceMeters + 10 × elevationGainMeters` (100 m of climb ≈ 1 km flat). Applied to both the basis and the goal; a missing elevation counts as 0.
- **Candidates:** completed Race-category runs (time = `Race.ResultTimeSeconds ?? DurationSeconds`), else completed Training-category runs with `Rpe ≥ 8`, both with distance ≥ 3,000 m and a time.
- **Basis:** races first; among them the one whose effort distance is closest to the goal's (smallest `|ln(E_goal / E_basis)|`), ties to the most recent. Without races, the same rule over the hard training runs. No candidate → no prediction, warning `noRecentEffort`.
- **Riegel:** `T_goal = T_basis × (E_goal / E_basis)^1.06`, rounded to whole seconds; pace = `T_goal / (distanceMeters / 1000)`, rounded to whole seconds per km.
- **Confidence:** `High` when the basis is a race and `E_goal / E_basis` ∈ [0.5, 2]; otherwise `Low`.

**Warnings** (`{ code, value }`, lowerCamel codes like `invalid-gpx` codes):

| Code | When | `value` |
|---|---|---|
| `noRecentEffort` | no prediction possible | null |
| `targetMuchFasterThanPredicted` | `targetDurationSeconds < 0.95 × predicted` | percent faster, 1 dp |
| `tooLittleTime` | `weeksToGoal < minimumWeeks` (distance < 15 km: 4; < 30 km: 8; ≤ 42.2 km: 12; longer: 16) | weeks short |
| `lowWeeklyVolume` | goal ≥ 21,000 m and the mean running distance of the last 4 complete ISO weeks < 0.5 × goal distance | the mean, metres |
| `beyondPlanHorizon` | `daysToGoal > 112` (informational: a plan covers 16 weeks at a time, ADR 0040 §2) | null |

`weeksToGoal = floor((targetDate − today) / 7)`. "Running distance" means Race and Training categories (the calendar `Totals` rule).

The UI shows "Based on your recent runs, ~1:52 looks realistic" with **Use as target** (sets `targetDurationSeconds` and switches a `Distance` goal to `Performance`).

## Consequences

- Goals are small, per-user rows; no aggregate is stored. The estimate recomputes in milliseconds from ≤ 112 days of projections.
- The composite NO ACTION links keep "same owner" in the database without new cascade paths; the price is two explicit clears (goal delete, run delete), both covered by tests.
- The predictor is deliberately simple (Riegel + km-effort). It is a hint, labelled as such, and is versioned only by code (no stored predictions).
- Short/long-term needs no migration when the 12-week threshold changes.

## Alternatives considered

- **Goals in Settings or on Insights:** rejected in the approved outline (§1): goals have a lifecycle and actions.
- **A stored `Term` column:** goes stale as days pass. Rejected.
- **`SET NULL` FKs between goals and runs:** rejected by SQL Server (multiple cascade paths).
- **Goals as rows of `Runs`** (a "goal" run type): mixes targets into totals, insights and the AI fact sheet. Rejected.
- **VDOT/Daniels tables or a fitted model:** more accurate for road races but needs tables or heart-rate data; trail elevation still needs a heuristic. Riegel + km-effort is transparent and testable. Revisit with feedback.
- **Blocking unrealistic targets:** the user decides; warnings only.

## Amendment (2026-11-16, implementation and review)

The decision stands, with these refinements. As built in Domain `Goals/GoalRules.cs` and `Infrastructure/Goals/GoalService.cs` (M9 plan §11.1, review §12 M1 and L1, commit `f73be6c`):
1. **Unchanged race-run link (§2; review M1).** `GoalRules.Validate` takes the stored `previousRaceRunId` on update. When `raceRunId` equals it, the link only has to **still name one of the caller's runs** (else 400 "Unknown run."); the Race-category, `Planned` and dated-on-`targetDate` rules apply only when the link is set on create or changed. The linked run may have been completed or moved since, and editing another field of the goal must not fail on it. `raceRunDate` still exposes a moved run's date.
2. **Verified (§3 ⚠).** Deleting a guest whose goal links a run and whose run links the goal succeeds in one statement on SQL Server 2022; **no fallback was needed** (plan §11.1).
3. **Goal delete is serialised (§3; review L1).** The delete transaction takes the per-user application lock `GoalWriteLock` (`sp_getapplock`, resource `user-goals:<userId>`), which suggestion accept and plan commit also take, before clearing `Runs.GoalId` and deleting the goal. An accept or a plan commit therefore cannot insert a run or suggestions for a goal that is being deleted; they see the goal gone afterwards (accept → 404, plan commit → 409 `concurrency-conflict`, ADR 0040 amendment).
4. **Estimate validation.** `POST /api/goals/estimate` uses `GoalRules.ValidateEstimate`: `targetDate` from **today** to today + 730 (no 7-day minimum), no type-specific requirements; distance, elevation and implied pace as for goals. Warning `code` is a plain string in the OpenAPI document.
5. **Caps (§1).** The goal caps are count-then-insert, like the run caps: two concurrent creates can exceed the cap by one. Accepted (review L5).

## References

- Riegel, P. S. (1981), "Athletic records and human endurance", *American Scientist* 69(3): the `T2 = T1 × (D2/D1)^1.06` model.
- ITRA performance index / "km-effort" (distance + elevation gain / 100): https://itra.run/About/FAQ
- SQL Server cascading referential integrity (multiple cascade paths): https://learn.microsoft.com/sql/relational-databases/tables/primary-and-foreign-key-constraints
