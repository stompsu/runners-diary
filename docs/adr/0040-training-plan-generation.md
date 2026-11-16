# 0040 – Training plan generation: deterministic envelope and fact sheet, plan agent, validation, quotas, failure handling

**Status:** Accepted (M9 plan approved 2026-11-16; structured-output limits verified on Microsoft Learn 2026-11-16; amended 2026-11-16 after the implementation and the review, and again after the G-P3 live eval ("plan reliability"), see the end; **§1 step 8 "slot shared with summaries", §6 "the global and guest-global budgets are shared" and the matching Consequence and Alternative are refined by the amendment**)
**Date:** 2026-11-16
**Related:** [0024](0024-training-metrics-definitions-v1.md), [0025](0025-insights-report-computation-and-api.md), [0026](0026-training-insights-agent-and-grounding.md) (agent, data-residency rule), [0027](0027-ai-model-provider-foundry-and-local-dev.md) (provider), [0028](0028-summary-cache-quotas-and-demo-summaries.md) (quotas, ledger), [0029](0029-web-tier-nginx-container.md) (proxy timeouts), [0034](0034-cost-guardrails-and-observability.md) (cost), [0039](0039-training-goals-and-realistic-target-estimate.md), [0041](0041-training-suggestions-storage-and-calendar.md), [M9 plan](../plans/M9-adaptive-training-plan.md)

## Context

A user with an active goal asks for suggested trainings up to the goal. The M6 pipeline exists: one keyless Foundry client (`AiChatClientProvider`), Agent Framework `ChatClientAgent`, strict JSON-schema output, a durable ledger with per-user and global quotas, per-replica slots, a per-user in-flight guard, one retry on invalid output (ADRs 0026–0028).

Differences from the summary:
- **A plan is numbers** (dates, distances, paces). The M6 trick (numbers only as placeholders) cannot apply. Grounding moves to a **deterministic envelope** computed in Domain, and a validator that rejects plans outside it.
- **Output is ~5–10× larger** (up to ~80 sessions). Calls take longer than the nginx proxy's 120 s budget for two attempts (ADR 0029).
- The fact sheet stays **metrics-only** (ADR 0026 amendment: the deployment is Global Standard, so the prompt may be processed outside the EU).

## Decision

### 1. Flow (POST `/api/goals/{goalId}/plan`)

Order of checks; the first failing one answers (the `Goals:Enabled` group switch answers 503 `feature-unavailable` before all of them, M9 plan §4.1):
1. Goal: 404 (unknown or foreign); 400 `errors.goalId` when not `Active` or `targetDate < today + 2` (no plannable day left).
2. AI: 503 `feature-unavailable` when `Ai:Enabled` or `Ai:Plan:Enabled` is off or no provider is configured.
3. History: **422 `insufficient-history`** when the caller has fewer than **3 completed running sessions** (Race/Training categories) in the last 84 days. No model call, no quota.
4. Single active plan: **409 `plan-other-goal-active`** (extension `goalId`) when another goal has Pending suggestions and `replaceOtherGoals` is not `true` (approved default §7.2).
5. Inputs: history, commitments, envelope, fact sheet (§2–§3), all deterministic.
6. In-flight guard per (feature, user): **409 `plan-in-progress`**.
7. Quotas (§6): 429 `ai-quota`.
8. Replica slot (shared with summaries): 503 `ai-busy`.
9. ≤ 2 attempts (§5); invalid twice, timeout or provider error → **502 `plan-failed`** (`reason`).
10. Commit (one transaction, §4 of ADR 0041): re-check step 4, re-apply item-level collision drops against fresh commitments, replace suggestions, write plan metadata. A goal whose `RowVersion` changed since step 5 → 409 `concurrency-conflict` (the quota is spent; rare).

### 2. Plan envelope (Domain `PlanEnvelopeBuilder`, pure)

All dates are local dates in the caller's zone (ADR 0008); weeks are ISO, Monday-first.

- **Horizon:** `planStart = today + 1`; `planEnd = min(goal.TargetDate − 1, Sunday of the ISO week of planStart + 15 weeks)` (≤ 16 ISO weeks, the first possibly partial; approved default §7.1). `horizonCapped = planEnd < goal.TargetDate − 1`. The goal day itself never gets a suggestion.
- **Phases** over the full span to the goal: `W` = ISO weeks from the week of `planStart` to the week of `targetDate`, inclusive.
  - taper `T`: goal distance < 15 km → 1; < 30 km → 2; else 3.
  - `W ≤ T + 1` → every week is **taper** ("goal three weeks away = taper only").
  - otherwise: the last `T` weeks taper; before them `P` peak weeks (`W ≥ 8` → 2, else 1); the remaining `R = W − T − P` split into base `R ≥ 6 ? floor(R/2) : (R ≥ 3 ? 1 : 0)` then build.
  - With a capped horizon only the first 16 weeks are emitted (mostly base/build).
- **Base volume** `V0` = mean running distance (Race + Training categories, completed) of the last 4 complete ISO weeks, floored at 10,000 m.
- **Weekly cap** for plan week `k` (1-based): base/build/peak `cap_k = min(V0 × 1.10^k, 160,000)`; taper week `j` of `T` multiplies the last pre-taper cap by `[0.6]` (T=1), `[0.75, 0.55]` (T=2) or `[0.8, 0.65, 0.5]` (T=3). A partial first week is scaled by `days / 7`.
- **Long-run cap:** `min(max(longest run of the last 4 complete weeks, 8,000) × 1.10^k, ceiling)`, ceiling by goal distance: < 15 km → 18 km; < 30 km → 24 km; ≤ 42.2 km → 35 km; longer → `min(0.6 × goal, 50 km)`. Taper weeks apply the taper factor.
- **Frequency:** `maxSessionsPerWeek = clamp(ceil(mean running sessions per week over 10 weeks) + 1, 3, 7)`.
- **Reference pace:** the goal's target pace, else the predicted pace (ADR 0039 §4), else 0.90 × the mean pace of the last 4 complete weeks; null if none.
- **Fixed commitments** (sent, counted and never touched):
  - every planned run in `[planStart, planEnd]` (accepted suggestions included, marked `fromThisGoal`);
  - completed runs in that range (only today+1 onwards, so normally none);
  - Race-category planned runs in `(planEnd, targetDate]`;
  - other **Active** goals' target dates in range (`otherGoal`).
  Each week carries `fixedDistanceMeters` and `availableDistanceMeters = max(0, cap − fixed)`.

The envelope parameters are v1 constants in `PlanEnvelopeRules`, versioned with `planFactSheetVersion`.

### 3. Plan fact sheet v1 (Domain `PlanFactSheetBuilder`, canonical JSON)

A nested JSON document (not M6's flat key list, because the model must produce numbers):
- `factSheetVersion`, `today`, `language`;
- `goal`: type, distance, elevation, surface, target date, target duration and pace, predicted duration, `weeksToGoal`;
- `history.weeks` (10 complete ISO weeks, oldest first): start, distance, duration, sessions, longest run, hard sessions, load, `acwrAtEnd` + band (from `InsightsReportBuilder` over that window: the M5 metrics, reused, ADR 0025 §4), plus `usualWeekdays` (weekdays with a running session in ≥ 40 % of the weeks), `sessionsPerWeek`, the latest ACWR point, and up to 6 completed races of the last 365 days (date, distance, time, run type code);
- `commitments` (≤ 200; date, run type code, distance, duration, `kind`: `planned` | `race` | `otherGoal`, `fromThisGoal`);
- `dismissed` (≤ 60; date + run type code of the goal's Dismissed suggestions: "the user declined these");
- `envelope`: as §2, per week `start`, `phase`, `maxDistanceMeters`, `fixedDistanceMeters`, `availableDistanceMeters`, `maxLongRunMeters`, plus `planStart`, `planEnd`, `horizonCapped`, `maxSessionsPerWeek`, `referencePaceSecondsPerKm`, `allowedRunTypes`.

"Hard session" = a completed run of type `tempo`, `intervals`, `racePace`, `hillRepeats`, `fartlek` or a race, or `Rpe ≥ 7`.

**Never included** (data residency and injection, ADR 0026 §1 + amendment): goal `title`, `eventName`, `notes`, `location`; run titles, notes, locations, gear, weather, heart rate; any id or name. Size cap `Ai:Plan:MaxFactSheetChars` (24,000); exceeding it is a bug (logged, 502 `provider`).

### 4. Agent, prompt and output schema

- **`TrainingPlanAgent`**: a second `ChatClientAgent` (name `TrainingPlan`, no tools, no memory) on the **same** `AiChatClientProvider` client. No new provider, package or Azure resource.
- **Prompt:** `prompts/training-plan/v1.md` with its own `manifest.json` (`{ current, planFactSheetVersion, versions: { "1": { file, sha256, denyList } }, languages }`), verified at startup like ADR 0026 §6 (immutable released versions, LF-normalised SHA-256).
- **Options:** `MaxOutputTokens` 8,000 (`Ai:Plan:MaxOutputTokens`), reasoning effort `low`, strict JSON schema, no temperature.
- **Network timeout of the shared client** becomes `max(Ai:AttemptTimeout, Ai:Plan:AttemptTimeout)` (today it is `Ai:AttemptTimeout`, 30 s, which would cut plan calls); each feature's own per-attempt timeout is still enforced by its cancellation token.
- **Strict output schema** (wire form; every property required, `additionalProperties: false`; 11 properties, 3 levels; `enum` and nullable unions are supported, `minimum`/`maxItems`/`pattern`/`format` are not, so ranges are validator rules):

```json
{ "type": "object", "additionalProperties": false, "required": ["notes", "sessions"],
  "properties": {
    "notes": { "type": "string" },
    "sessions": { "type": "array", "items": {
      "type": "object", "additionalProperties": false,
      "required": ["date", "runType", "intensity", "phase", "distanceMeters", "durationSeconds",
                   "paceMinSecondsPerKm", "paceMaxSecondsPerKm", "description"],
      "properties": {
        "date": { "type": "string" },
        "runType": { "type": "string", "enum": ["easy", "recovery", "long", "tempo", "intervals", "fartlek", "hillRepeats",
                     "progression", "racePace", "stridesDrills", "trailTraining", "treadmill", "rest"] },
        "intensity": { "type": "string", "enum": ["recovery", "easy", "steady", "tempo", "threshold", "interval", "racePace", "rest"] },
        "phase": { "type": "string", "enum": ["base", "build", "peak", "taper"] },
        "distanceMeters": { "type": ["number", "null"] },
        "durationSeconds": { "type": ["integer", "null"] },
        "paceMinSecondsPerKm": { "type": ["integer", "null"] },
        "paceMaxSecondsPerKm": { "type": ["integer", "null"] },
        "description": { "type": "string" } } } } } }
```

Run types are **codes** (the model handles words better than ids); the server maps them to `RunTypeCatalog` ids. `paceMin` is the faster bound.

### 5. Validation (Domain `TrainingPlanValidator`, pure) and retries

**Plan-level rules** (any violation → one retry whose extra user message lists only the rule codes and the affected week starts or dates from the envelope, never the model's text; a second failure → 502 `plan-failed`, `reason: invalidOutput`):

| Code | Rule |
|---|---|
| `schema` | Parses (a truncated answer fails here); `notes` ≤ 600 chars; 1 … `7 × weeks` sessions; `date` is `yyyy-MM-dd`; `description` 1–300 chars; non-rest: `distanceMeters` 1,000–60,000 required, `durationSeconds` null or 600–21,600; rest: `intensity = rest`, no distance, duration or pace; `rest` intensity only with the rest type; paces both null or both 150–900 with min ≤ max ≤ min + 90 |
| `dateRange` | every date in `[planStart, planEnd]` |
| `sessionsPerDay` | at most one session per date |
| `phase` | each session's phase equals its week's envelope phase |
| `weeklyVolume` | per week: if the week has suggested distance, `fixed + suggested ≤ cap × 1.05` |
| `longRun` | each session's distance ≤ its week's long-run cap × 1.05 |
| `frequency` | per week: if it has suggestions, fixed running sessions + suggestions ≤ `maxSessionsPerWeek` |
| `hardSessions` | per ISO week ≤ 2 hard sessions (3 when `maxSessionsPerWeek ≥ 6`), counting hard commitments; no hard suggestion on a day adjacent to another hard suggestion or a hard/race commitment. Hard = intensity `tempo`, `threshold`, `interval`, `racePace` |
| `taperIntensity` | when the goal is inside the horizon: no hard suggestion in `[targetDate − 3, targetDate − 1]` |
| `pace` | with a reference pace: no `paceMin < 0.85 × ref`; `easy`/`recovery` sessions have `paceMin ≥ ref` |
| `emptyWeek` | every full non-taper plan week without a fixed running commitment has ≥ 1 suggestion |
| `markup` | as ADR 0026 (`<`, `](`, `**`, `#` at line start, `://`, `www.`, control characters) in `notes` and descriptions |
| `forbiddenTopic` | the manifest deny list (v1 = the summary deny list) in `notes` and descriptions |
| `empty` | no session left after the item-level drops |

Digits are **allowed** in descriptions ("6 × 800 m"): they describe the session, not the user's data.

**Item-level rule** (drop that session only, count it; never a retry): `collision`, a session on a date that already has a fixed running or rest commitment (Race, Training or rest type, planned or completed) or another goal's target date. Cross-training and Body Bar entries do **not** block a day. The response returns `droppedCount`.

### 6. Shared runner, quotas and limits

- **`AiGenerationRunner`** (Infrastructure, extracted from `SummaryGenerator` with no behaviour change for summaries) owns: the in-flight guard keyed by **(feature, user)**, the quota check, the replica slot, the attempt loop with per-attempt and **total** deadlines, the retry-once rule, 429/credential handling, and the ledger row. Each feature supplies one "attempt" delegate (run the agent, validate) and its settings.
- **Ledger:** `AiGenerations.Feature` (`Summary = 0`, `Plan = 1`, default 0 so the previous release still writes valid rows). Per-user quotas count the caller's rows **of that feature**; global budgets count **weighted attempts** of all features: `SUM(Attempts × weight)`, with plan weight `Ai:Plan:GlobalAttemptWeight` = 3 (one plan attempt costs ≈ 3 summary attempts), so the daily dollar ceiling of ADR 0028 stays unchanged.
- **Plan quotas:** users 5 per rolling 24 h (`Ai:Plan:Quotas:UserPer24h`), guests 2 per account (`Ai:Plan:Quotas:GuestPerAccount`); the global and guest-global budgets are shared (`Ai:Quotas:GlobalPer24h`, `GuestGlobalPer24h`).
- **Burst limiter:** rate policy `ai-plan` (users 3, guests 2 per 10 min) replaces the general one on the POST.
- **Timeouts:** `Ai:Plan:AttemptTimeout` 90 s, `Ai:Plan:TotalTimeout` 190 s; the second attempt runs only when ≥ 30 s remain. The web tier gets a **per-route** `proxy_read_timeout`/`proxy_send_timeout` of **200 s** for `POST /api/goals/{id}/plan` only (Container Apps ingress allows 240 s; ADR 0029 keeps 120 s elsewhere).
- **Cancellation:** a client abort cancels the call; the ledger row is still written (`Cancelled`), as in M6.

### 7. Local development, tests and demo

- `RuleBasedFakeChatClient` gains a **plan mode**, selected by the response-format schema name `training_plan`. It parses the plan fact sheet and writes a deterministic plan inside the envelope (e.g. easy on the first usual weekday, a quality session mid-week outside the taper, the long run on the last usual weekday, 85 % of each week's available distance), which always passes the validator. Local dev, CI and E2E never call Foundry.
- **Guests:** the demo seeds one goal and a pre-built suggestion set (ADR 0041 §5), so the feature works without quota.
- **Evals** (`tests/RunnersDiary.Ai.Tests`, CI mode with scripted clients; live mode only with `RD_AI_LIVE_EVAL=1`): half marathon in 10 weeks; over-ambitious 10k under 40:00; trail 44 km / 1,000 m; goal 3 weeks away (taper only); collisions with existing planned runs; capped horizon (goal 30 weeks away).

## Consequences

- The model chooses *which* session goes on *which* day and how to describe it; *how much* is bounded by code. A plan cannot exceed the 10 % growth rule, skip the taper or stack hard days without failing validation.
- Plans and summaries share one client, one ledger, one global budget and one set of replica slots. A plan holds a slot for up to ~3 minutes; with 2 slots per replica, two concurrent plans make summaries answer `ai-busy` meanwhile (acceptable at this scale).
- The fact sheet carries no names, notes or places, so the Global Standard data-residency position of ADR 0026 is unchanged.
- The per-route nginx timeout is the one infrastructure change; no new Azure resource.
- 8,000 output tokens is tight for a full 16-week plan (~80 sessions ≈ 5–6k tokens plus reasoning). A truncated answer fails `schema`. The live eval reports output tokens; raising the cap is a configuration change (and the weight should follow the cost).

## Alternatives considered

- **Let the model plan freely and only check the schema:** no protection against unsafe load jumps. Rejected.
- **Generate the whole plan deterministically (no model):** possible, and the envelope is most of it; but session choice, variety and wording are what the user asked the AI for. The fake client's rule-based plan proves the fallback exists if the model fails often.
- **Placeholders for numbers (M6 style):** a plan's numbers are its content. Rejected.
- **Function tools** (`get_history`, `get_commitments`): extra round trips for the same data (ADR 0026 §1 reasoning). Rejected.
- **Asynchronous generation (202 + polling or a job):** robust against proxy timeouts but needs a job store, polling UI and cleanup. A per-route proxy timeout is enough for ≤ 190 s. Revisit if plans grow.
- **Separate global budget for plans:** two ceilings to reason about. The weighted shared budget keeps one dollar ceiling.
- **Reuse `summary-in-progress` / `summary-failed`:** the SPA would need the request context to word messages; separate types are clearer. Rejected.

## Amendment (2026-11-16, implementation and review)

The decision stands, with these refinements. As built in `Infrastructure/Ai/` (`AiGenerationRunner`, `AiUsageLedger`, `TrainingPlanGenerator`, `TrainingPlanService`) and Domain `Goals/Planning/` (M9 plan §11.2–§11.3, review §12 findings L1–L3, commits `f73be6c`, `f44f8d0`):
1. **Plan slots (§1 step 8, §6; review L2).** Plans no longer compete freely for the shared replica slots. New option `Ai:Plan:MaxConcurrentGenerations` (default **1**) bounds plans per replica **inside** `Ai:MaxConcurrentGenerations` (default 2); startup validation requires it to be ≤ `Ai:MaxConcurrentGenerations − 1` when that is ≥ 2. A plan needs **a plan slot and a shared slot** (both taken without waiting, else 503 `ai-busy`); a summary needs a shared slot only. With the defaults, at most one plan runs per replica and a summary always finds a slot. With `Ai:MaxConcurrentGenerations = 1` the rule does not apply and a running plan still blocks summaries on that replica. The options are read once at startup.
2. **Guest plan sub-cap (§6; review L2).** The global and guest-global budgets stay shared and weighted as decided, but guest plans get a **sub-cap inside the guest-global budget**: `Ai:Plan:GuestGlobalPer24h` (default **20 weighted attempts** per rolling 24 h, i.e. about 7 plan attempts at weight 3, of the guest budget of 60; checked before a generation starts, so the last admitted generation may overshoot by its second attempt). It is checked for guest plan requests only, after the account quota, the global budget and the guest-global budget; when it trips the answer is **429 `ai-quota` with `scope: "global"`, `limit: 20`** and no `resetsAt`. Guest plans can therefore no longer use up the guest summaries' share. This refines, not reverses, the rejected "separate global budget for plans": there is still one dollar ceiling (`Ai:Quotas:GlobalPer24h`), and the sub-cap only limits guest plans within it.
3. **Implied pace (§5 `schema`; review L3).** A non-rest session with both `distanceMeters` and `durationSeconds` must have an implied pace of **150–900 s/km** (`TrainingSuggestion.HasPlausibleImpliedPace`); otherwise the validator reports `schema` at `sessions[n].durationSeconds`. `TrainingSuggestion.Create` applies the same rule as its own safety net. Each value in range is not enough (30 km in 10 min would be an impossible planned run on accept).
4. **Commit serialisation (§1 step 10; review L1).** The commit transaction takes the per-user application lock `GoalWriteLock` (`sp_getapplock`, resource `user-goals:<userId>`, exclusive, transaction-owned, 15 s timeout), which goal delete and suggestion accept also take, then re-reads the goal. A goal that was deleted, left `Active` or changed its `RowVersion` meanwhile answers **409 `concurrency-conflict`** (also for a delete, not 404). A foreign-key violation (SQL error 547) on the insert is a safety net mapped to the same 409. The commit runs with `CancellationToken.None` once the model's work is done, so a client abort after the model answered still stores the plan.
5. **Replaced goals (§1 step 10; review nit).** Goals whose suggestions are removed by `replaceOtherGoals` get `PlanOutdated = 1` (the UI shows "Goal changed since this plan" instead of a plan with nothing pending).
6. **Ledger index.** Migration `AiGenerationsFeatureIndex` rebuilds `IX_AiGenerations_Created (CreatedAt) INCLUDE (IsGuest, Feature, Attempts)` so the weighted global sums are covered. The weighting is applied in C# over one grouped query (EF Core 10 cannot translate a weighted `SUM` over the `tinyint` column).

Consequence (replaces "with 2 slots per replica, two concurrent plans make summaries answer `ai-busy`"): with the defaults one plan per replica can run; summaries keep the other slot.

## Amendment (2026-11-16, plan reliability after the G-P3 live eval)

The live eval passed 12/18, then 14/18 with diagnostics. Nothing was truncated; every failure was invalid output twice: mostly `weeklyVolume` on the partial first week (odd caps like 28,286 m, overshot by round sessions) and too-wide pace ranges on long trail runs (M9 plan §11.8). Refinements:
1. **Envelope v2 / plan fact sheet v2 (§2, §3).** `maxDistanceMeters`, `availableDistanceMeters` (= floor(cap − fixed)) and `maxLongRunMeters` are rounded **down to 500 m** (`PlanEnvelopeRules.CapStepMeters`). The fact sheet sends the rounded values and the validator checks the same ones (tolerance still 1.05). The shape is unchanged.
2. **Prompt v2 (§4).** It states the per-week sum rule explicitly ("add up the `distanceMeters` … at most `availableDistanceMeters`"), the partial first week, the 500 m caps, the pace widths (item 4) with examples, and that a rejection note's numbers must be fixed. v1 stays in the manifest (immutable, loadable); `current` is 2.
3. **Retry note numbers (§5).** A violation may carry two validator-computed integers (`Value`, `Limit`): `weeklyVolume` (the week's suggested running distance, `availableDistanceMeters`), `longRun` (session distance, `maxLongRunMeters`), and a too-wide pace range (`schema` at `paceMaxSecondsPerKm`: width, the run type's limit). The retry note lists them under "Numbers to fix". It still carries only codes, safe locations and app-computed integers, never model text, so the injection and data-residency position is unchanged.
4. **Pace range width and location (§5 `schema`, replaces "min ≤ max ≤ min + 90").** The width (slower minus faster bound) may be at most **135 s for `long` and `trailTraining` sessions, and for `easy` and `recovery` sessions when the goal's surface is Trail**, where terrain makes pace vary. Everything else keeps **90 s**. The live eval's long trail runs used 95–135 s ranges. The rule lives in one place, `TrainingSuggestion.MaxPaceSpreadSeconds(runTypeId, trailGoal)`. The validator uses it with the fact sheet's goal surface. `TrainingSuggestion.Create` takes an optional `trailGoal`, and the plan service and the demo plan pass the goal's surface, so the safety net is never stricter than the validator. A caller without the goal gets the run-type-only rule, which is stricter. A missing slower bound, a slower bound above 900, faster > slower, or a too-wide range is reported at `sessions[n].paceMaxSecondsPerKm` (a safe retry-note location now). Problems with the faster bound alone stay at `paceMinSecondsPerKm`.
5. **Diagnostics.** A failed generation's in-process result carries attempts, usage, the finish reason and each attempt's violations, for the live eval only. API responses, logs and the ledger row are unchanged.

### Amendment (2026-11-16, repair step after the third live eval)

Run 3 of the live eval (prompt v2) passed 14/18. Every failure was the model fixing one local issue on the retry and breaking another (`phase`, `taperIntensity`, `pace`, a 92/90 pace spread, a 43,000/36,000 `weeklyVolume`). All-or-nothing validation over about 40 sessions is the cause, so a **deterministic repair step** (`TrainingPlanRepair`, Domain) runs between parsing and the plan-level verdict, on every attempt and before the decision to retry. A repairable answer costs one attempt. Each repair can only make the plan easier or safer. Order: phase, pace-spread clamp (while a session is converted), item-level drops, long-run clamp, weekly-volume scale, then the normal checks run on the repaired sessions. The step is idempotent.

| Rule | Before | Now | Repair |
|---|---|---|---|
| `phase` | plan-level violation | **never fails a plan** | each session's phase is set from its envelope week (the field stays in the strict schema; an unknown code is still `schema`) |
| pace range too wide (`schema` at `paceMaxSecondsPerKm`) | violation | repaired | slower bound clamped to faster bound + `MaxPaceSpreadSeconds(runType, trailGoal)` (90/135) |
| `taperIntensity` | violation | **item-level drop** | the session is dropped, counted in `droppedCount` |
| `pace` (target faster than allowed) | violation | **item-level drop** | the session is dropped, counted in `droppedCount` |
| `weeklyVolume` | violation | repaired up to 25 % above the tolerant cap (cap x 1.05, fixed distance included) | the week's suggested distances are scaled down to `availableDistanceMeters`, floored to 100 m, durations in proportion (whole seconds). More than 25 % stays a violation (retry) |
| `longRun` | violation | repaired up to 25 % above the tolerant cap (cap x 1.05) | that session's distance is clamped to `maxLongRunMeters`, duration in proportion. More stays a violation |
| `schema` (shape, other fields), `dateRange`, `sessionsPerDay`, `frequency`, `hardSessions`, `emptyWeek`, `markup`, `forbiddenTopic`, `empty`, implied pace out of bounds | violation | **unchanged: violation** | none (retry once, then 502 `plan-failed`) |

Details and choices. The long run is clamped before the weekly sum is judged, so a week that overshoots only because of one long run can be rescued. A volume or long-run repair that would push a session out of its own range (distance under 1,000 m, duration under 10 minutes, implied pace outside 2:30-15:00 min/km) is skipped, and the original violation stands, so scaling never invents a schema error. Distances are floored to 100 m (so the sum never exceeds the cap by rounding); caps are multiples of 500 m, so a long-run clamp lands on the cap exactly. Repairs are recorded per attempt in `PlanAttemptDetails.Repairs` (rule code, location = date, week start or fixed path, before/after numbers; never model text) and printed by the live eval. The API response shape is unchanged; `droppedCount` includes the new item-level drops. The demo plan fixture and the rule-based fake plan writer must stay repair-free (asserted for every day of 2028). Prompt v2 and its manifest hash are unchanged: the model is still asked to meet every rule, the repair is a safety net, not a licence.

## References (checked 2026-11-16)

- Azure OpenAI structured outputs (supported types incl. `enum` and `anyOf`; all fields required; `additionalProperties: false`; ≤ 100 properties and 5 nesting levels; `minimum`, `maxItems`, `pattern`, `format` unsupported; updated 2026-08-24): https://learn.microsoft.com/azure/foundry/openai/how-to/structured-outputs
- Agent Framework structured outputs (`ChatClientAgent`, `ResponseFormat`): https://learn.microsoft.com/agent-framework/agents/structured-outputs
- Container Apps ingress (240 s request timeout): https://learn.microsoft.com/azure/container-apps/ingress-overview
- Reasoning models (`max_completion_tokens` includes reasoning tokens): https://learn.microsoft.com/azure/foundry/openai/how-to/reasoning
