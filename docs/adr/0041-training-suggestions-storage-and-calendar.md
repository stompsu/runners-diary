# 0041 – Training suggestions: storage, accept/dismiss/regenerate, and the calendar display

**Status:** Accepted (M9 plan approved 2026-11-16; amended 2026-11-16 after the implementation and the review, see the end)
**Date:** 2026-11-16
**Related:** [0010](0010-current-user-and-data-scoping.md), [0013](0013-persistence-model-and-migrations.md), [0016](0016-calendar-grid-and-url-state.md) (grid, a11y), [0018](0018-run-forms-and-server-errors.md) (run form), [0028](0028-summary-cache-quotas-and-demo-summaries.md) §5 (demo precedent), [0036](0036-race-catalogue-model-and-race-runs.md) §5 (proposal → run precedent), [0037](0037-race-proposals-filtering-and-state.md) §4 (separate range endpoint), [0039](0039-training-goals-and-realistic-target-estimate.md), [0040](0040-training-plan-generation.md), [M9 plan](../plans/M9-adaptive-training-plan.md)

## Context

A generated plan is a list of dated sessions. The user should see them in the diary, distinct from runs and race proposals, and turn each into a real planned run, or decline it, or regenerate the whole set.

If suggestions were planned `Runs`, every consumer of runs would change meaning: calendar week totals, Insights `planned` figures and `missedCount`, the AI summary fact sheet, run caps and the M8 "signed up" logic. Unaccepted suggestions must reach none of them.

## Decision

### 1. Storage: a separate `TrainingSuggestions` table

One row per suggested session, `IUserOwned` with the `Owner` filter:
- `GoalId` + `UserId` → composite FK to `Goals(Id, UserId)` **ON DELETE CASCADE** (no FK to `Users`, so there is a single cascade path `Users → Goals → TrainingSuggestions`, the `GpxTracks` pattern);
- `GenerationId` (the generation that wrote it), `Date`, `RunTypeId` (FK → `RunTypes`; training types 20–31 and rest 41 only), `DistanceMeters?`, `DurationSeconds?`, `Intensity`, `TargetPaceMinSecondsPerKm?` / `TargetPaceMaxSecondsPerKm?` (min = faster), `Description` (≤ 300), `Phase`, `Status` (`Pending`, `Dismissed`), `CreatedAt`;
- clustered `(UserId, Date)` for the calendar range read; unique `(GoalId, Date)` (one session per day per goal).

There is **no `Accepted` status**: accepting turns the row into a run and deletes it.

Cap: ≤ 120 rows per goal (`7 × 16` + margin), enforced by the validator's `schema` rule; with one goal holding suggestions at a time (ADR 0040 §1 step 4) this also bounds rows per user.

### 2. Lifecycle

| Action | Effect |
|---|---|
| **Generate / Regenerate** (`POST /api/goals/{goalId}/plan`) | One transaction: delete **all** of this goal's suggestions (Pending and Dismissed), and with `replaceOtherGoals: true` all suggestions of the caller's other goals; insert the new set with a new `GenerationId`; write the goal's plan metadata (`PlanGeneratedAt`, `PlanGenerationId`, `PlanNotes`, `PlanPromptVersion`, `PlanModelDeployment`, `PlanModelId`, `PlanHorizonEnd`, `PlanHorizonCapped`, `PlanOutdated = 0`). **Runs are never touched**; accepted sessions go back to the model as fixed commitments, and the dismissed dates/types as "declined" hints (ADR 0040 §3). |
| **Accept** (`POST /api/training-suggestions/{suggestionId}/accept`) | One transaction: create a planned run (below), set `Runs.GoalId = suggestion.GoalId`, delete the suggestion. 201 `RunDto` + `Location`. The run caps of `createRun` apply (409 `run-limit`). Works on Pending and Dismissed rows (accepting a dismissed one is the undo path). |
| **Dismiss** (`POST /api/training-suggestions/{suggestionId}/dismiss`) | `Status = Dismissed` (idempotent). 200 `TrainingSuggestionDto`. |
| Goal deleted / status leaves `Active` | Suggestions are deleted (cascade / explicit, ADR 0039 §1). |

**Accept mapping** (no body): `date` = suggestion date; `startTime` null; `timeZoneId` = the caller's profile zone; `status` = `Planned`; `runTypeId`; `distanceMeters`; `durationSeconds`; `notes` = `description`; everything else null. The target pace is not stored on the run (runs have no pace field; pace is derived); "Edit & accept" shows it in the form.

**Accept with overrides** ("Edit & accept"): the body `{ run: RunWriteRequest }` replaces the mapping entirely (the run form pre-filled from the suggestion), validated by `RunRules` exactly like `createRun`; `GoalId` is still set.

### 3. `Runs.GoalId`

Nullable, composite FK `(GoalId, UserId) → Goals(Id, UserId)` NO ACTION, cleared on goal delete (ADR 0039 §3). Read-only in `RunDto.goalId`; `updateRun` preserves it (like `catalogRaceId`). It gives the "from plan" marker in the calendar and run details, and lets the next generation mark commitments `fromThisGoal`.

### 4. Calendar display (desktop and phone) and accessibility

The diary reads suggestions with a separate range query, **`GET /api/training-suggestions?from&to&includeDismissed`** (span ≤ 92 days counting both ends), in parallel with `getCalendarMonth`, using the client-side grid bounds (the M8 pattern, ADR 0037 amendment II §1). A failed suggestions request never blocks the grid. Goal markers come from the cached `listGoals` (≤ 50 rows), not a range query.

- **`SuggestionBubble`** (desktop): **diagonal-hatched** background (a repeating linear gradient in the muted ink), a 1 px solid muted border, a `Sparkles` icon, the run-type label and distance in lighter text, and a "Suggested" badge. Planned runs are dashed 2 px and coloured; race proposals dotted 1 px with a flag. The three differ by **shape and icon**, never only by colour. `aria-hidden`, click-only, like `RunBubble`/`ProposalBubble`; a click opens the Day panel at the suggestion.
- **`SuggestionDot`** (phone): a `Sparkles` glyph.
- **Goal marker:** a `Target` icon next to the day number on each Active goal's target date (desktop and phone).
- **"From plan" marker:** a small `Sparkles` icon on `RunBubble` when `goalId` is set.
- **Order within a day:** runs, then suggestions, then race proposals, sharing `MAX_BUBBLES` and "+n".
- **Not counted in week totals** (they are not runs).
- **"Show suggestions" toggle** in the month header (`aria-pressed`), next to the proposals toggle, stored in `localStorage` (`rd.showSuggestions`, default on). It hides bubbles only; the Day panel still lists suggestions.
- **Legend:** "Suggested (hatched, sparkles)" and "Goal (target)" entries.
- **Accessible name of a cell:** after runs and before proposals, `"; 2 suggested trainings: Easy run, 8 km; Long run, 18 km"` (`calendar:cell.suggestions_one/_other`) and, on a goal date, `"; goal: Trail 25K"` (`calendar:cell.goal`). The grid keeps one tab stop and the ADR 0016 keyboard model.
- **Day panel** (the keyboard path): a "Suggested trainings" section; each suggestion is a card with type, distance, duration, pace range, phase, description and three real buttons: **Accept**, **Edit & accept** (opens `/diary/:y/:m/suggestions/:suggestionId/accept`, the run form pre-filled), **Dismiss**. A dismissed card shows **Accept** only ("Accept anyway").
- **Goals page** (`/goals/:goalId`): the same cards grouped by ISO week, with "Show dismissed".

### 5. Guest demo plan (no tokens)

- `DemoDataSeeder` seeds one **demo goal** (`Distance`, "Demo Beskids Trail 25K", 25,000 m, 900 m, `Trail`, Sunday of week +10, Active) and a **pre-built suggestion set** from `seed/demo/training-plan.json` (week-relative, like `runs.json`): weeks +6 … +10 (after the demo's planned runs that end with the half marathon in week +5), on days without a running commitment. The goal's plan metadata has `PlanModelDeployment = "demo"` and no prompt version; `GoalDto.plan.source = "Demo"`, labelled "Example plan for the demo data".
- Unlike demo summaries (computed on read because they go stale at midnight), suggestions are **rows**: they carry no derived numbers that change with "today", and the guest can accept and dismiss them.
- Consistency: a test runs the pristine demo for **every "today" across a year** and asserts that the fixture passes `TrainingPlanValidator` against the demo's own envelope (phases, caps, collisions). A demo-data change that breaks the plan fails CI; a startup check (like `DemoDatasetStartupCheck`) validates the file.
- Guests can still regenerate (2 per account, ADR 0040 §6).

## Consequences

- Suggestions are invisible to totals, insights, the AI summary and run caps until accepted, with no change to those code paths.
- One extra read per month view (only when goals are enabled and the toggle is on), served by a clustered seek.
- An accepted session is a normal run: editing, deleting, GPX and rating all work; the link survives edits.
- Regenerate loses the dismissed rows (they become hints for that one generation). A second regeneration no longer knows them (accepted).
- The toggle is per browser, not per account (no schema for a UI preference).

## Alternatives considered

- **Suggestions as planned `Runs` with a flag:** every reader of runs would need a filter; one missed filter silently corrupts totals or the AI fact sheet. Rejected (approved outline §2).
- **Keep accepted suggestions with `Accepted` status:** two sources of truth for one session. Rejected; `Runs.GoalId` is the record.
- **Suggestions inside `getCalendarMonth`:** the ADR 0037 §4 reasoning applies (hot endpoint, separate invalidation). Rejected.
- **Server-side toggle** (a column on `Users` or `Goals`): a migration for a display preference. Rejected for M9; easy to add later.
- **Demo suggestions computed on read** (M6 style): they would not support accept/dismiss. Rejected.

## Amendment (2026-11-16, implementation and review)

The decision stands, with these refinements. As built in `Infrastructure/Goals/` and the SPA (`web/src/features/calendar`, `features/goals`, `features/runs/RunFormPanels.tsx`; M9 plan §11.4–§11.5, review §12 L1 and L4):
1. **Accept and dismiss under concurrency (§2; review L1).** Accept runs in one transaction under the per-user `GoalWriteLock` (shared with goal delete and plan commit) and reads the suggestion under the lock. A suggestion removed meanwhile (a concurrent accept, regenerate or goal delete) answers **404**, also when the run insert hits the goal FK (SQL 547 safety net). Dismiss takes no lock; a dismiss whose row was removed meanwhile (optimistic concurrency) also answers **404**.
2. **Goal markers ignore the toggle (§4).** "Show suggestions" hides **suggestion bubbles and dots only**; goal markers always show (they come from `listGoals`, not the suggestions range). The Day panel lists suggestions regardless of the toggle, as decided.
3. **"Suggested" badge (§4).** The text badge is shown from the `lg` breakpoint up. Between `sm` and `lg` the bubble is identified by the hatching and the `Sparkles` icon; below `sm` the phone dot is the sparkles glyph. The legend explains both, so the shape-and-icon rule (WCAG 1.4.1) still holds.
4. **Edit & accept (§4).** The panel finds the suggestion in the month's grid-range list (there is no get-one endpoint; `includeDismissed: true`) and shows "no longer available" when it is gone. The run form submits through `acceptTrainingSuggestion` with `{ run }` and then opens the new run in place of the panel.
5. **Goals page reads (§4).** A goal's suggestions are read as two consecutive 92-day ranges from today (about 26 weeks), because a plan can reach 112 days and the list endpoint spans at most 92.
6. **Cancel (review L4).** Cancel aborts the request. If the abort reaches the server during the model call, nothing is stored, but the generation still counts toward the plan quota (a `Cancelled` ledger row). If the model had already answered, the server still commits the plan (ADR 0040 amendment §4). The SPA therefore says that cancelling does not give the generation back, says that a finished plan "will appear shortly", and refreshes the goal and the suggestions after cancel.
7. **Demo plan (§5).** The demo goal (Sunday of week +10) is 70–76 days away, so it is always Short-term. Seeding does not depend on `Goals:Enabled`. A demo plan that does not validate at seed time is skipped with a warning (the guest gets the diary without the goal); the startup check `DemoTrainingPlanStartupCheck` fails the host instead.

## References

- WAI-ARIA grid pattern (single tab stop, cell names): https://www.w3.org/WAI/ARIA/apg/patterns/grid/
- WCAG 2.2 SC 1.4.1 Use of Color: https://www.w3.org/WAI/WCAG22/Understanding/use-of-color
