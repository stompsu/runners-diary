# M9 – Adaptive training plan: plan

**Status:** Approved 2026-11-16 (outline and all four §7 defaults approved by the user on 2026-11-16; this plan turns the outline into contracts). **Part A implemented 2026-11-16** on `m9-adaptive-training-plan` (steps 1–11, reviewed: §12); **awaiting user approval**. Deviations: §11; follow-ups: §13. **Part B (gates G-P1…G-P4, §6.2) not started**; each gate needs the user's explicit "yes".
**Builds on:**
- M2/M3: `Runs`, run types, `Owner` filter, run form (ADR 0018), month grid and panel routes (ADR 0016), guest lifecycle and `DemoDataSeeder`.
- M5: `InsightsReportBuilder` (weekly load, ACWR) and `RaceFact`; reused, unchanged (`metricsVersion` 1).
- M6: the AI pipeline (`AiChatClientProvider`, `ChatClientAgent`, strict schema, `AiUsageLedger`, slots, in-flight guard, `RuleBasedFakeChatClient`, prompt manifests).
- M7: nginx web tier, Bicep flags, gated provisioning. M8: the "separate range endpoint + grid bounds client-side" calendar pattern, the 503-group feature switch, the signup → planned race run that a `Race` goal can link to.

**Related ADRs:** [0008](../adr/0008-run-date-and-time-storage.md) · [0010](../adr/0010-current-user-and-data-scoping.md) · [0011](../adr/0011-guest-accounts-lifecycle.md) · [0012](../adr/0012-request-validation.md) · [0013](../adr/0013-persistence-model-and-migrations.md) · [0016](../adr/0016-calendar-grid-and-url-state.md) · [0024](../adr/0024-training-metrics-definitions-v1.md)–[0028](../adr/0028-summary-cache-quotas-and-demo-summaries.md) · [0029](../adr/0029-web-tier-nginx-container.md) · [0034](../adr/0034-cost-guardrails-and-observability.md) · [0036](../adr/0036-race-catalogue-model-and-race-runs.md) · [0037](../adr/0037-race-proposals-filtering-and-state.md).
**New (accepted with the plan):**
- [0039](../adr/0039-training-goals-and-realistic-target-estimate.md): goals model, rules, links to runs, realistic-target estimate;
- [0040](../adr/0040-training-plan-generation.md): envelope, fact sheet, agent/prompt/schema, validator, shared runner, quotas, timeouts, fake;
- [0041](../adr/0041-training-suggestions-storage-and-calendar.md): suggestion storage, accept/dismiss/regenerate, calendar display + a11y, guest demo plan.

M9 is post-MVP: brief §1.4 lists a "training plan generator" as out of MVP scope "but keep the design open". Like M7/M8 it has an **offline part A** and **user-confirmed gates** (§6.2). No new Azure resource is expected.

## 1. Goal & acceptance criteria

**Goal:** a runner (user or guest) records **goals** on a Goals page, gets a **realistic-target hint** from their recent runs, and asks the AI for **suggested trainings** up to the goal (≤ 16 weeks at a time). Suggestions appear in the diary, visually distinct, and can be **accepted** (→ ordinary planned run), **edited and accepted**, or **dismissed**; the set can be **regenerated**. Load and safety are bounded by deterministic code; the model chooses and describes sessions.

**Out of scope:** `pl` strings (keys ready); plan notifications; automatic re-planning after missed sessions (regenerate is explicit); FIT/TCX/Strava; plan templates without AI for users (the fake exists only for dev/CI); multi-goal simultaneous plans (approved default §7.2); goal sharing; heart-rate zones.

| # | Criterion | How verified |
|---|---|---|
| AC1 | `RunnersDiary.Domain/Goals/` implements `GoalRules` (ADR 0039 §2), `GoalTerm`, `RaceTimePredictor` + `GoalEstimator` (ADR 0039 §4), `PlanEnvelopeBuilder` (ADR 0040 §2), `PlanFactSheetBuilder` (ADR 0040 §3, no free text: reflection + JSON test), `TrainingPlanValidator` (all rule codes of ADR 0040 §5) and `TrainingSuggestion` (accept mapping passes `RunRules`). Domain still has no package references. | Domain.Tests |
| AC2 | One additive migration `TrainingGoals`: `Goals`, `TrainingSuggestions`, `Runs.GoalId`, `AiGenerations.Feature` (default 0). Guest cleanup removes goals and suggestions (cascade) also when a goal links a run and a run links the goal (⚠ ADR 0039 §3, verified or fallback applied). Deleting a goal keeps accepted runs (`goalId` null); deleting a run clears `raceRunId`. | Api.Tests (Testcontainers) |
| AC3 | Endpoints of §4.1 behave as specified (validation matrix, 404s, 409s, 422, headers, order of checks). OpenAPI + `schema.d.ts` regenerated; api-contract CI green. All new endpoints are **Auth** in the inventory test and in the 401 sweep; the `/api/goals` and `/api/training-suggestions` groups answer 503 while `Goals:Enabled` is false. | Api.Tests + CI |
| AC4 | Isolation: guest A's goals, suggestions, estimates and generations never affect guest B or user C (matrix), including a foreign `raceRunId` (400) and a foreign `suggestionId` (404). | Api.Tests |
| AC5 | Generation with the fake model: a valid plan is stored; regenerate replaces Pending + Dismissed of that goal only, never runs; `replaceOtherGoals` semantics (409 without it); 422 below 3 sessions in 84 days; quotas per feature (plan 5/24 h, guest 2) independent of summary quotas; global budget weighted (×3); in-flight 409 `plan-in-progress`; slot 503; invalid output twice → 502 `plan-failed` `invalidOutput`; item collisions dropped and counted. Existing M6 Ai.Tests and Api.Tests keep **all their assertions** after the runner refactor (only constructor/record call sites may change). | Ai.Tests + Api.Tests |
| AC6 | A fresh guest sees the demo goal with an "Example plan" of suggestions in weeks +6…+10 in the diary and on the Goals page, without any model call; the fixture validates against the pristine demo for every "today" across a year. | Ai.Tests/Domain.Tests + Api.Tests + full-stack |
| AC7 | Calendar: suggestion bubbles (hatched, sparkles, "Suggested") are distinct from planned runs (dashed) and proposals (dotted) by shape and icon; goal markers on target dates; "from plan" marker on accepted runs; suggestions not in week totals; toggle persisted per browser; cell accessible name lists suggestions and the goal; one tab stop + arrow keys unchanged; Day panel cards with Accept / Edit & accept / Dismiss. | Vitest + Playwright + axe |
| AC8 | Goals page (`/goals`, lazy, nav entry when `client-config.goals.enabled`): list, create/edit/delete with presets, type-specific fields, realistic-target hint with "Use as target", generate/regenerate with progress, cancel, the replace-other-goal confirmation, every problem type mapped to a friendly message (§5), suggestions per ISO week with actions. react-hook-form + zod mirror the server rules; server errors per ADR 0018. Responsive, keyboard-usable. | Vitest + Playwright (desktop + phone-la) |
| AC9 | i18n: new lazy `goals` namespace; calendar keys in `calendar.json`; no literals. Initial bundle grows ≤ 2 kB gzip; the goals chunks are `LAZY_ONLY`. | CI (`bundle-budget`) |
| AC10 | Infra: Bicep params `plansUiEnabled` (`PLANS_UI_ENABLED` → `Goals__Enabled`) and `plansAiEnabled` (`PLANS_AI_ENABLED` → `Ai__Plan__Enabled`), both default false; nginx per-route 200 s timeout for the plan POST; `ci.yml` e2e-fullstack env (`Goals__Enabled=true`, `Ai__Provider=Fake`). `az bicep build/lint` clean; header checks green. **Nothing provisioned in part A.** | CI `deploy-artifacts` + review |
| AC11 | `dotnet build` warning-free; all CI jobs green; the full-stack suite includes a goals journey. | CI |

### 1.1 Adaptations of the approved outline (recorded)

| # | Outline said | Plan does | Why |
|---|---|---|---|
| A1 | `TargetPaceMinSecPerKm` / `…MaxSecPerKm` | `targetPaceMinSecondsPerKm` / `targetPaceMaxSecondsPerKm` (API and columns) | Existing naming (`paceSecondsPerKm`, `…Seconds`) |
| A2 | Intensity/phase values in lower case | API enums **PascalCase** (`Recovery`, `RacePace`, `Base`, …) like `RunStatus`/`RaceKind`; the **model schema** uses lowerCamel codes, mapped server-side | API-wide enum convention (api-contract "Enum casing") |
| A3 | Intensity list | + `Rest`, valid only with the `rest` run type | Rest suggestions are allowed by the outline; a rest day has no training intensity |
| A4 | `plan-in-progress` or reuse `summary-in-progress` — decide | **New** `plan-in-progress`, plus `plan-failed`, `insufficient-history`, `plan-other-goal-active`, `goal-limit` | Clear SPA messages per feature (ADR 0040 alternatives) |
| A5 | `replaceOtherGoals=true`, else 409 | 409 type **`plan-other-goal-active`** with extension `goalId` | Typed problem like `race-already-linked` |
| A6 | `Runs.GoalId` FK SET NULL; `RaceRunId` link | Composite FKs **NO ACTION**, cleared by the application on goal/run delete | SQL Server multiple cascade paths (ADR 0039 §3) |
| A7 | Suggestions cascade with goal | Composite FK `(GoalId, UserId)` → `Goals`, **no FK to `Users`** | Same reason; the `GpxTracks` pattern |
| A8 | Plan quotas share the global budget | Shared, but plan attempts are **weighted ×3** | Keeps the ADR 0028 dollar ceiling (§7) |
| A9 | 90 s attempt timeout | + `TotalTimeout` 190 s, nginx **per-route 200 s**, shared client network timeout raised | nginx proxies `/api` with 120 s (ADR 0029); 2 × 90 s does not fit |
| A10 | Accept "optionally with user overrides" | Body `{ run?: RunWriteRequest }`: absent = mapping, present = the full run (the pre-filled form) | Reuses `RunWriteRequest` validation as is |
| A11 | Toggle "like the proposals toggle" | Stored in `localStorage` (`rd.showSuggestions`), not on the server | The proposals toggle lives in the interest profile; suggestions have no settings row; no migration for a UI flag |
| A12 | Collision with "a day that already has a planned run" | Only Race/Training/rest runs and other goals' dates block a day; cross-training and Body Bar do not | The demo has Body Bar on Tue/Thu/Sat evenings |
| A13 | Generate endpoint name | `POST /api/goals/{goalId}/plan` (`generateTrainingPlan`); suggestions under `/api/training-suggestions` (kebab-case like `/api/run-types`, `/api/guest-sessions`); actions as `POST …/{id}/accept|dismiss` like `POST /api/races/{raceId}/signup` | ADR 0007 + M8 conventions |
| A14 | Goal status change | Leaving `Active` deletes the goal's suggestions; changing a plan field sets `plan.outdated` | Stale sessions for a changed goal would be wrong; no silent regeneration |

## 2. Files / projects by owning agent

### 2.1 backend-dotnet
```
src/RunnersDiary.Domain/Goals/
  Goal.cs (IUserOwned; Create/Update/RecordPlan/MarkPlanOutdated), GoalValues.cs, GoalType.cs, GoalStatus.cs
  GoalRules.cs, GoalTerm.cs, GoalLimits.cs (defaults 50 / 10)
  RaceTimePredictor.cs, GoalEstimator.cs, GoalEstimate.cs          # ADR 0039 §4
src/RunnersDiary.Domain/Goals/Planning/
  PlanEnvelopeRules.cs (v1 constants), PlanEnvelopeBuilder.cs, PlanEnvelope.cs
  PlanFactSheet.cs, PlanFactSheetBuilder.cs, PlanFactSheetJson.cs (canonical JSON), CommitmentFact.cs
  TrainingSuggestion.cs (IUserOwned), TrainingIntensity.cs, TrainingPhase.cs, SuggestionStatus.cs, PlannedSession.cs
src/RunnersDiary.Domain/Runs/Run.cs                    # + GoalId, LinkToGoal(goalId) on creation only; Update never touches it
src/RunnersDiary.Domain/Ai/AiGeneration.cs, AiFeature.cs  # + Feature (Summary = 0, Plan = 1)
src/RunnersDiary.Infrastructure/Persistence/Configurations/{Goal,TrainingSuggestion}Configuration.cs
src/RunnersDiary.Infrastructure/Persistence/Configurations/{Run,AiGeneration}Configuration.cs   # GoalId composite FK; Feature default 0
src/RunnersDiary.Infrastructure/Persistence/RunnersDiaryDbContext.cs                              # DbSets, Owner filters
src/RunnersDiary.Infrastructure/Persistence/Migrations/<ts>_TrainingGoals.cs
src/RunnersDiary.Infrastructure/Goals/
  GoalService.cs                 # CRUD, caps, raceRunId checks, clears on delete, status-change suggestion delete
  TrainingHistoryReader.cs       # projections: RunFacts (112 d), RaceFacts, commitments, other goals' dates, dismissed hints
  GoalEstimateService.cs
  TrainingSuggestionService.cs   # list, accept (mapping/overrides, run caps via RunLimitGuard logic), dismiss
  DemoTrainingPlan.cs            # loads seed/demo/training-plan.json; startup check
src/RunnersDiary.Infrastructure/Guests/DemoDataSeeder.cs                 # + demo goal + suggestions
src/RunnersDiary.Api/Features/Goals/
  GoalContracts.cs, GoalsEndpoints.cs (503 group filter), ListGoals.cs, GetGoal.cs, CreateGoal.cs, UpdateGoal.cs, DeleteGoal.cs, EstimateGoal.cs
src/RunnersDiary.Api/Features/TrainingSuggestions/
  TrainingSuggestionContracts.cs, TrainingSuggestionsEndpoints.cs (503 group filter), ListTrainingSuggestions.cs,
  AcceptTrainingSuggestion.cs, DismissTrainingSuggestion.cs
src/RunnersDiary.Api/Features/Runs/{RunContracts.cs, RunMapping.cs, DeleteRun.cs}   # RunDto.goalId; delete clears Goals.RaceRunId
src/RunnersDiary.Api/Features/ClientConfig/GetClientConfig.cs                     # goals.enabled, ai.planGeneration
src/RunnersDiary.Api/Http/ProblemTypes.cs                                          # 5 new types (§4.3)
src/RunnersDiary.Api/openapi/runnersdiary-api.json                                 # regenerated
src/RunnersDiary.AppHost/AppHost.cs                                                # Goals__Enabled=true
src/RunnersDiary.Api/appsettings.json                                              # Goals section (§4.7)
```

### 2.2 ai-insights
```
src/RunnersDiary.Domain/Goals/Planning/
  TrainingPlanDraft.cs, TrainingPlanDraftJson.cs, TrainingPlanValidator.cs, PlanRuleCode.cs, PlanValidationPolicy.cs
src/RunnersDiary.Infrastructure/Ai/
  AiGenerationRunner.cs          # extracted from SummaryGenerator: guard (feature,user), quotas, slot, attempts, deadlines, ledger
  SummaryGenerator.cs            # thin adapter over the runner; behaviour unchanged
  AiOptions.cs                   # + Plan section + validation (§4.7)
  AiUsageLedger.cs, EfAiGenerationStore.cs   # Feature; per-feature user counts; weighted global sums
  ChatClientFactory.cs           # NetworkTimeout = max(summary, plan attempt timeout)
  TrainingPlanAgent.cs           # ChatClientAgent "TrainingPlan", schema "training_plan"
  PlanPromptCatalog.cs           # prompts/training-plan/manifest.json loader (ADR 0026 §6 rules)
  TrainingPlanService.cs         # ADR 0040 §1 orchestration, commit transaction
  RuleBasedFakeChatClient.cs     # + plan mode (schema name training_plan)
  AiServiceCollectionExtensions.cs, AiStartupCheck.cs
src/RunnersDiary.Api/Features/Goals/GenerateTrainingPlan.cs     # endpoint + problem mapping
src/RunnersDiary.Api/RateLimiting/RateLimitingSetup.cs          # policy ai-plan
prompts/training-plan/manifest.json, v1.md, README.md
seed/demo/training-plan.json                                    # demo suggestion fixture (authored here, seeded by backend)
tests/RunnersDiary.Ai.Tests/Plans/*                             # validator, runner, agent wire, fake, evals, demo fixture
```

### 2.3 frontend-react
```
web/src/app/routes.tsx                     # /goals, /goals/new, /goals/:goalId, /goals/:goalId/edit (lazy); diary panel route suggestions/:suggestionId/accept
web/src/components/layout/AppShell.tsx     # nav "Goals" (common:nav.goals) when client-config.goals.enabled
web/src/features/goals/
  GoalsPage.tsx, GoalCard.tsx, GoalDetailPage.tsx, GoalFormPage.tsx, goalSchema.ts, GoalPresetPicker.tsx,
  TargetTimeField.tsx, RealisticTargetHint.tsx, RaceRunSelect.tsx,
  PlanSection.tsx (generate/regenerate, progress, cancel, errors), ReplaceOtherGoalDialog.tsx,
  SuggestionCard.tsx, SuggestionWeekList.tsx, goalQueries.ts, goalProblems.ts, goalFormat.ts
web/src/features/calendar/
  SuggestionBubble.tsx (+ SuggestionDot), GoalMarker.tsx, SuggestionsToggle.tsx,
  DayCell.tsx, MonthGrid.tsx, DiaryMonthPage.tsx, CalendarLegend.tsx, RunBubble.tsx ("from plan" icon), calendarQueries.ts
web/src/features/runs/{DayPanel.tsx, panelRoutes.ts, RunFormPanels.tsx (AcceptSuggestionPanel), runQueries.ts, RunDetailsDialog.tsx}
web/src/i18n/en/goals.json (new, lazy), calendar.json, common.json, i18n/lazyBundles.ts, i18n/i18next.d.ts
web/scripts/bundle-budget.mjs              # LAZY_ONLY += goals chunks
web/src/api/generated/schema.d.ts          # regenerated
```

### 2.4 azure-infra
```
infra/main.bicep, infra/main.parameters.json  # params plansUiEnabled → Goals__Enabled, plansAiEnabled → Ai__Plan__Enabled on the api app
                                              # (string 'false' defaults, like racesUiEnabled; azd env PLANS_UI_ENABLED, PLANS_AI_ENABLED)
web/nginx/default.conf.template               # a location for POST /api/goals/<guid>/plan: proxy_read/send_timeout 200s (rest of /api stays 120 s)
web/scripts/verify-web-image.sh               # assert the per-route timeout
.github/workflows/ci.yml                      # e2e-fullstack API env: Goals__Enabled=true, Ai__Provider=Fake
```

### 2.5 qa-tester
```
tests/RunnersDiary.Domain.Tests/Goals/*, Goals/Planning/*
tests/RunnersDiary.Api.Tests/Goals/*, TrainingSuggestions/*, Isolation/IsolationMatrixTests.cs, Security/EndpointAuthorizationTests.cs
web/src/test/fakeApi/goals.ts (+ fixtures); web/src/features/goals/*.test.tsx; calendar tests
web/e2e/goals.spec.ts, goals.phone.spec.ts; web/e2e-fullstack/goals.spec.ts
```

### 2.6 reviewer (checklist)
- IDOR on goals, suggestions, `raceRunId`, accept/dismiss; composite FKs; the clears on delete.
- Fact sheet: no title, event name, notes, location, ids (ADR 0040 §3); retry message without model text; nothing logged but codes and counts.
- Quotas per feature, weighted global sum, in-flight guard per feature, guest limits; timeouts vs nginx/ACA.
- Model output rendered as plain text only; `markup`/`forbiddenTopic` rules.
- Migration expand rule; cost; a11y of the grid with three bubble kinds.

### 2.7 architect
After implementation: deviations (§11), ADR amendments, `data-model.md`, `api-contract.md`, `architecture.md` moved from "planned" to "added".

## 3. Domain model summary

Full tables: [data-model.md](../data-model.md#added-in-m9-adaptive-training-plan). Rules: ADR 0039 §2 (goals), ADR 0040 §2/§5 (envelope, validator), ADR 0041 §1–§2 (suggestions).

## 4. Contracts

Full list: [api-contract.md](../api-contract.md#added-in-m9-adaptive-training-plan).

### 4.1 Endpoints (all **Auth**, users and guests; general rate limiter unless noted)

While `Goals:Enabled` is false, every route of both groups answers **503 `feature-unavailable`** (after authentication), like `/api/races`.

| Method & path | operationId | Success | Errors |
|---|---|---|---|
| `GET /api/goals` | `listGoals` | 200 `GoalListResponse` (all of the caller's goals, ≤ 50; Active first, then by `targetDate`), `private, no-store` | 401, 503 |
| `GET /api/goals/{goalId}` | `getGoal` | 200 `GoalDto`, `private, no-store` | 401, 404, 503 |
| `POST /api/goals` | `createGoal` | 201 `GoalDto` + `Location: /api/goals/{id}` | 400, 401, 409 `goal-limit`, 413, 503 |
| `PUT /api/goals/{goalId}` | `updateGoal` | 200 `GoalDto` (full replace; `version` required) | 400, 401, 404, 409 `concurrency-conflict`, 503 |
| `DELETE /api/goals/{goalId}` | `deleteGoal` | 204 (suggestions deleted, accepted runs keep existing with `goalId: null`) | 401, 404, 503 |
| `POST /api/goals/estimate` | `estimateGoal` | 200 `GoalEstimateResponse` (no side effects), `private, no-store` | 400, 401, 503 |
| `POST /api/goals/{goalId}/plan` | `generateTrainingPlan` | 200 `TrainingPlanResponse`, `private, no-store` | 400 (`goalId`, body), 401, 404, 409 (`plan-other-goal-active`, `plan-in-progress`, `concurrency-conflict`), 422 `insufficient-history`, 429 (`ai-quota`, `rate-limited`), 502 `plan-failed`, 503 (`feature-unavailable`, `ai-busy`). **Rate policy `ai-plan`** replaces the general one. |
| `GET /api/training-suggestions?from&to&includeDismissed` | `listTrainingSuggestions` | 200 `TrainingSuggestionListResponse`, `private, no-store` | 400 (`from`, `to` 1950–2100, `to ≥ from`, span `to − from + 1 ≤ 92`; `includeDismissed` boolean), 401, 503 |
| `POST /api/training-suggestions/{suggestionId}/accept` | `acceptTrainingSuggestion` | 201 `RunDto` + `Location: /api/runs/{id}` | 400 (run fields as `createRun`), 401, 404, 409 `run-limit`, 413, 503 |
| `POST /api/training-suggestions/{suggestionId}/dismiss` | `dismissTrainingSuggestion` | 200 `TrainingSuggestionDto` (idempotent) | 401, 404, 503 |
| `GET /api/client-config` (changed) | `getClientConfig` | + `goals`, `ai.planGeneration` | — |
| Runs endpoints (changed) | `getRun`, `listRuns`, `createRun`, `updateRun`, `getCalendarMonth` | `RunDto` gains read-only `goalId`; `updateRun` preserves it | unchanged |
| `DELETE /api/runs/{id}` (changed) | `deleteRun` | also clears `Goals.raceRunId` pointing at the run | unchanged |

**Generate order of checks** (after the group's `Goals:Enabled` 503): 404 goal → 400 (`goalId`: not Active or `targetDate < today + 2`; body) → 503 `feature-unavailable` → 422 `insufficient-history` → 409 `plan-other-goal-active` → 409 `plan-in-progress` → 429 `ai-quota` → 503 `ai-busy` → model → 502 `plan-failed` → commit (409 `concurrency-conflict` if the goal changed meanwhile) → 200.

### 4.2 Shapes

```ts
GoalType = "Race" | "Performance" | "Distance"
GoalStatus = "Active" | "Achieved" | "Archived"
GoalTerm = "ShortTerm" | "LongTerm"                      // derived: targetDate − today ≤ 84 days
TrainingIntensity = "Recovery" | "Easy" | "Steady" | "Tempo" | "Threshold" | "Interval" | "RacePace" | "Rest"
TrainingPhase = "Base" | "Build" | "Peak" | "Taper"
SuggestionStatus = "Pending" | "Dismissed"
Surface = "Road" | "Trail" | "Track" | "Treadmill" | "Mixed"   // existing

GoalWriteRequest = {
  type: GoalType; title: string /* 1..120 */; targetDate: string;
  distanceMeters: number /* 1000..250000 */; elevationGainMeters?: number | null /* 0..10000 */;
  targetDurationSeconds?: number | null;   // required for Performance, forbidden for Distance; pace 150..900 s/km
  surface?: Surface | null; eventName?: string | null /* 1..150, required for Race */;
  location?: { latitude: number; longitude: number; name?: string | null /* ≤200 */ } | null;
  raceRunId?: string | null;               // caller's own planned Race-category run dated targetDate
  notes?: string | null /* ≤1000 */;
  status?: GoalStatus | null;              // POST: default Active; PUT: required
  version?: string | null                  // PUT: required (GoalDto.version)
}

GoalDto = Omit<GoalWriteRequest, "version" | "status"> & {
  id: string; status: GoalStatus; term: GoalTerm;
  raceRunDate: string | null;              // current date of the linked run (may differ from targetDate)
  plan: GoalPlanDto | null;                // null until a plan was generated or seeded
  version: string; createdAt: string; updatedAt: string
}
GoalPlanDto = {
  source: "Model" | "Demo"; generatedAt: string /* UTC */; generationId: string;
  notes: string | null;                    // model text, plain text only
  promptVersion: number | null; model: string | null;   // null for Demo
  horizonEnd: string; horizonCapped: boolean; outdated: boolean;
  pendingCount: number; dismissedCount: number; acceptedCount: number   // acceptedCount = runs with goalId = this goal
}
GoalListResponse = { items: GoalDto[] }

EstimateGoalRequest = { type: GoalType; distanceMeters: number; elevationGainMeters?: number | null;
                        surface?: Surface | null; targetDate: string; targetDurationSeconds?: number | null }   // GoalRules subset
GoalEstimateResponse = {
  today: string; daysToGoal: number; weeksToGoal: number; term: GoalTerm; minimumWeeks: number;
  recentWeeklyDistanceMeters: number;      // mean of the last 4 complete ISO weeks (running types)
  prediction: null | {
    durationSeconds: number; paceSecondsPerKm: number; confidence: "High" | "Low";
    basis: { runId: string; date: string; kind: "Race" | "Training"; distanceMeters: number;
             durationSeconds: number; elevationGainMeters: number | null }
  };
  warnings: { code: "noRecentEffort" | "targetMuchFasterThanPredicted" | "tooLittleTime" | "lowWeeklyVolume" | "beyondPlanHorizon";
              value: number | null }[]     // ADR 0039 §4 table
}

GenerateTrainingPlanRequest = { replaceOtherGoals?: boolean }          // body optional; unknown members → 400
TrainingPlanResponse = {
  goal: GoalDto; suggestions: TrainingSuggestionDto[];                 // the new set, by date
  droppedCount: number;                                                // item-level collisions
  replacedGoalIds: string[]                                            // other goals whose suggestions were removed
}

TrainingSuggestionDto = {
  id: string; goalId: string; generationId: string; date: string; runTypeId: number;
  distanceMeters: number | null; durationSeconds: number | null;
  intensity: TrainingIntensity; targetPaceMinSecondsPerKm: number | null /* faster */; targetPaceMaxSecondsPerKm: number | null;
  description: string /* ≤300, plain text */; phase: TrainingPhase; status: SuggestionStatus
}
TrainingSuggestionListResponse = { from: string; to: string; items: TrainingSuggestionDto[] }   // by date, then goal
AcceptTrainingSuggestionRequest = { run?: RunWriteRequest | null }   // body optional; absent = default mapping (ADR 0041 §2)

ClientConfigResponse.goals = { enabled: boolean }                    // Goals:Enabled, read per request
ClientConfigResponse.ai = { summaryGeneration: boolean; planGeneration: boolean }   // planGeneration = Goals:Enabled && Ai available && Ai:Plan:Enabled
RunDto.goalId: string | null                                         // read-only; not in RunWriteRequest
```

Generate problem extensions: `plan-other-goal-active` → `goalId` (the goal holding Pending suggestions); `ai-quota` → `scope`, `limit`, `resetsAt` (as M6); `plan-failed` → `reason: "invalidOutput" | "timeout" | "provider"`.

### 4.3 New problem types

| Status | `type` | When |
|---|---|---|
| 409 | `urn:runnersdiary:problem:goal-limit` | goals per account reached (users 50, guests 10) |
| 409 | `urn:runnersdiary:problem:plan-other-goal-active` | another goal has Pending suggestions and `replaceOtherGoals` is not true; extension `goalId` |
| 409 | `urn:runnersdiary:problem:plan-in-progress` | a plan generation of this account is already running (per replica) |
| 422 | `urn:runnersdiary:problem:insufficient-history` | fewer than 3 completed running sessions in the last 84 days |
| 502 | `urn:runnersdiary:problem:plan-failed` | invalid output twice, timeout or provider error; extension `reason` |

Reused: `feature-unavailable`, `ai-busy`, `ai-quota`, `rate-limited`, `concurrency-conflict`, `run-limit`.

### 4.4 Schema delta (migration `TrainingGoals`, additive)

| Object | Summary |
|---|---|
| `Goals` | Per user, `Owner` filter, FK → `Users` CASCADE; clustered `(UserId, TargetDate)`; `AK_Goals_Id_UserId`; plan metadata columns; `(RaceRunId, UserId)` → `Runs(Id, UserId)` NO ACTION |
| `TrainingSuggestions` | Per user, `Owner` filter; `(GoalId, UserId)` → `Goals(Id, UserId)` CASCADE (no FK to `Users`); clustered `(UserId, Date)`; unique `(GoalId, Date)` |
| `Runs.GoalId` | `uniqueidentifier NULL`; `(GoalId, UserId)` → `Goals(Id, UserId)` NO ACTION; filtered index |
| `AiGenerations.Feature` | `tinyint NOT NULL DEFAULT 0` (`Summary = 0`, `Plan = 1`) |

### 4.5 Domain signatures (sketch)

```csharp
public sealed record GoalValues(GoalType Type, string Title, DateOnly TargetDate, double DistanceMeters, double? ElevationGainMeters,
    int? TargetDurationSeconds, Surface? Surface, string? EventName, RunLocation? Location, Guid? RaceRunId, string? Notes, GoalStatus Status);
IReadOnlyList<DomainError> GoalRules.Validate(GoalValues values, DateOnly today, DateOnly? previousTargetDate, LinkedRaceRun? raceRun);
GoalTerm GoalTerm.Of(DateOnly targetDate, DateOnly today);
TimePrediction? RaceTimePredictor.Predict(double distanceMeters, double? elevationGainMeters, IReadOnlyList<EffortFact> candidates);
GoalEstimate GoalEstimator.Estimate(GoalSpec goal, DateOnly today, IReadOnlyList<RunFact> facts, IReadOnlyList<RaceFact> races);
PlanEnvelope PlanEnvelopeBuilder.Build(GoalSpec goal, DateOnly today, TrainingHistory history, IReadOnlyList<CommitmentFact> commitments,
    int? referencePaceSecondsPerKm);
PlanFactSheet PlanFactSheetBuilder.Build(GoalSpec goal, GoalEstimate estimate, DateOnly today, InsightsReport historyReport,
    TrainingHistory history, IReadOnlyList<CommitmentFact> commitments, IReadOnlyList<DismissedHint> dismissed, PlanEnvelope envelope, string language);
PlanValidation TrainingPlanValidator.Validate(string modelJson, PlanFactSheet sheet, PlanValidationPolicy policy);
//   PlanValidation(IReadOnlyList<PlanViolation> Violations, IReadOnlyList<PlannedSession> Kept, int DroppedCount, string? Notes)
TrainingSuggestion TrainingSuggestion.Create(Guid userId, Guid goalId, Guid generationId, PlannedSession session, DateTimeOffset now);
RunValues TrainingSuggestion.ToRunValues(string profileTimeZoneId);   // passes RunRules
```

### 4.6 Model contract

Strict output schema, envelope rules and validator codes: **ADR 0040 §4–§5** (normative). Prompt v1 (`prompts/training-plan/v1.md`, front matter `version: 1`) covers: role and safety framing (suggestions, not medical advice); dates only in `[planStart, planEnd]`; one session per day; never on commitment dates; respect `availableDistanceMeters`, `maxLongRunMeters`, `maxSessionsPerWeek`, phases and taper; prefer `usualWeekdays`; no hard days back to back; do not re-propose a declined date+type; paces relative to `referencePaceSecondsPerKm` (null for trail when unsure); descriptions ≤ 120 characters of plain English, no markup, no names; `notes` ≤ 3 sentences.

### 4.7 Configuration (defaults in `appsettings.json`; Production overrides via Bicep env)

| Key | Default | Notes |
|---|---|---|
| `Goals:Enabled` | `false` (AppHost, tests, CI full-stack: `true`) | → `client-config.goals.enabled`; 503 groups when false |
| `Goals:MaxGoalsPerUser` / `MaxGoalsPerGuest` | 50 / 10 | → 409 `goal-limit` |
| `Ai:Plan:Enabled` | `true` (Bicep: `false` until G-P4) | Plan-only kill switch on top of `Ai:Enabled` |
| `Ai:Plan:MaxOutputTokens` | 8000 | 256–16,000 |
| `Ai:Plan:ReasoningEffort` | `low` | |
| `Ai:Plan:AttemptTimeout` | `00:01:30` | ≤ 2 min |
| `Ai:Plan:TotalTimeout` | `00:03:10` | ≥ AttemptTimeout, ≤ 200 s (nginx per-route limit) |
| `Ai:Plan:MaxAttempts` | 2 | 1 or 2; the second only if ≥ 30 s remain |
| `Ai:Plan:MaxFactSheetChars` | 24000 | |
| `Ai:Plan:PromptsPath` | `prompts/training-plan` | copied to output like `prompts/training-insights` |
| `Ai:Plan:Quotas:UserPer24h` / `GuestPerAccount` | 5 / 2 | per feature |
| `Ai:Plan:GlobalAttemptWeight` | 3 | 1–10; weight of a plan attempt in `Ai:Quotas:GlobalPer24h` / `GuestGlobalPer24h` |
| `Ai:Plan:MaxConcurrentGenerations` | 1 | *Added after the review (§12 L2).* Plan slots per replica, inside `Ai:MaxConcurrentGenerations` (default 2); validated ≤ shared − 1 when shared ≥ 2. A plan needs a plan slot **and** a shared slot |
| `Ai:Plan:GuestGlobalPer24h` | 20 | *Added after the review (§12 L2).* Guest plans' sub-cap in weighted attempts per 24 h, inside the guest-global budget (60); → 429 `ai-quota`, `scope: global`, `limit: 20` |
| `RateLimiting:AiPlan:UserPermitLimit` / `GuestPermitLimit` / `Window` | 3 / 2 / 10 min (Development 60 / 30) | policy `ai-plan` |

### 4.8 Client queries and invalidation

| Query | Cache | Invalidated by |
|---|---|---|
| `listGoals` | `staleTime` 1 min | goal create/update/delete, `generateTrainingPlan`, accept, dismiss, run update/delete (`raceRunDate`, `acceptedCount`) |
| `getGoal(id)` | `staleTime` 1 min | same as `listGoals` (for the affected ids) |
| `estimateGoal(input)` | `keepPreviousData`, 400 ms debounce, only when the form part is valid | — (pure) |
| `listTrainingSuggestions(gridStart, gridEnd)` | `staleTime` 5 min; prefetched with adjacent months while the toggle is on | `generateTrainingPlan`, accept, dismiss, goal delete/status change |
| accept success | — | + the shared run-write helper (`getCalendarMonth`, `listRuns`, `getInsightsReport`, `getInsightsSummary`, race proposals) |

The diary issues `listTrainingSuggestions` only when `client-config.goals.enabled` and the toggle is on; the Day panel and the Goals page read suggestions regardless of the toggle.

## 5. UI

- **Nav:** Diary · Insights · **Goals** · Settings (`common:nav.goals`, shown when `goals.enabled`).
- **Goals page (`/goals`):** cards grouped Active / Achieved / Archived with title, type, date, a **Short-term / Long-term** badge, distance (+ elevation), target time, and the plan state ("Plan: 23 suggestions, generated 3 Nov" or "No plan yet"). "New goal" button. Empty state explains goals and links to the form.
- **Goal form (`/goals/new`, `/goals/:goalId/edit`):** preset chips (5K, 10K, Half, Marathon, Trail 25K + 800 m, Trail 44K + 1,000 m, Custom); type radio (Race / Performance / Distance) with type-specific fields; title (pre-filled from the preset); date; distance km; elevation m; surface; target time `h:mm:ss`; event name; optional link to a planned race run (`RaceRunSelect` lists the caller's planned race runs; picking one sets the date); location (existing `LocationField`); notes. **Realistic-target hint** under the target-time field: "Based on your 10K on 12 Oct, ~1:52 looks realistic" + **Use as target**; warnings as a polite list (never blocking); "Low confidence" note when `confidence = Low`.
- **Goal detail (`/goals/:goalId`):** summary; `raceRunDate ≠ targetDate` banner with "Use race date"; **Plan section**:
  - **Generate suggestions** / **Regenerate** (primary), disabled with a reason when `ai.planGeneration` is false ("AI suggestions are not available right now");
  - while running: an indeterminate progress bar, "This can take up to two minutes", **Cancel** (aborts the request), a polite live region;
  - before sending, if another goal has `pendingCount > 0`: `ReplaceOtherGoalDialog` ("Generating replaces the suggestions of 'X'"), then sends `replaceOtherGoals: true`; a 409 `plan-other-goal-active` opens the same dialog;
  - last generated date, source badge ("AI-generated" / "Example plan for the demo data"), the plan notes (plain text), "Goal changed since this plan" when `outdated`, "Covers the next 16 weeks; regenerate later for the rest" when `horizonCapped`, and "n suggestions skipped because you already have runs on those days" when `droppedCount > 0`;
  - suggestions grouped by ISO week (week label + phase), each a `SuggestionCard` with Accept / Edit & accept / Dismiss; "Show dismissed" switch.
- **Error messages** (`goals:errors.*`): `featureUnavailable`, `busy` (with retry hint), `quota` (users: "You can generate again at {time}"; guests: "The AI suggestions of this demo session are used up"; global: "used up for today"), `inProgress`, `failed` (`reason` → "The AI could not produce a safe plan. Try again later." / timeout / provider), `insufficientHistory` ("Log at least 3 runs in the last 12 weeks so the plan can start from your current training"), `otherGoalActive`, `goalLimit`, `rateLimited`.
- **Diary:** `SuggestionBubble`/`SuggestionDot`, `GoalMarker`, "from plan" icon on `RunBubble`, `SuggestionsToggle`, legend entries, cell accessible names, Day panel section (ADR 0041 §4). **Edit & accept** opens `/diary/:y/:m/suggestions/:suggestionId/accept`: the run form pre-filled (date, type, distance, duration, notes = description; the pace range shown as a read-only hint), submitting calls `acceptTrainingSuggestion` with `{ run }`, then navigates (`replace`) to `runs/:runId`.
- **Run details:** "From your plan for {goal title}" link to `/goals/:goalId` when `goalId` is set.
- **i18n:** `goals.json` (`nav`, `list.*`, `form.*`, `presets.*`, `types.*`, `status.*`, `term.*`, `estimate.*`, `warnings.<code>`, `plan.*`, `suggestion.*`, `intensity.<Value>`, `phase.<Value>`, `errors.*`); `calendar.json`: `cell.suggestions_one/_other`, `cell.goal`, `legend.suggested`, `legend.goal`, `suggestionsToggle.*`, `suggestionBubble`. English only (approved default §7.4).

## 6. Implementation steps and confirmation gates

### 6.1 Part A: offline (each step = one reviewable commit on `m9-adaptive-training-plan`)

1. [x] **backend-dotnet (+ qa-tester): Domain goals.** `Goals/` (rules, term, predictor, estimator) and `Goals/Planning/` envelope, fact sheet, suggestion entity, with hand-computed unit tests (§9). No API change.
2. [x] **backend-dotnet: persistence.** Configurations, migration `TrainingGoals`, `Run.GoalId`, `AiGeneration.Feature`; **verify the ⚠ of ADR 0039 §3 first** on Testcontainers SQL (guest delete with mutual links); `DeleteRun` clear; AppHost `Goals__Enabled=true`.
3. [x] **backend-dotnet: endpoints.** Goals CRUD + estimate, suggestions list/accept/dismiss, `RunDto.goalId`, client-config, problem types; OpenAPI → `npm --prefix web run generate:api`; Api.Tests (validation matrix, isolation, inventory, cascades, caps).
4. [x] **ai-insights: shared runner refactor.** `AiGenerationRunner`, ledger `Feature` + weighted global sum, guard per (feature, user), client network timeout. **All existing Ai.Tests and Api.Tests pass with their assertions unchanged.**
5. [x] **ai-insights: plan generation.** Prompt v1 + manifest, `TrainingPlanAgent`, `TrainingPlanValidator`, parsing, `TrainingPlanService`, `generateTrainingPlan` endpoint, `ai-plan` policy, fake plan mode, the demo fixture `seed/demo/training-plan.json` with its every-today test, eval cases (CI mode); OpenAPI regenerated.
6. [x] **backend-dotnet: demo seed.** Demo goal + suggestions in `DemoDataSeeder`, `DemoTrainingPlan` startup check; Api.Tests (fresh guest sees the demo plan, AC6).
7. [x] **frontend-react: Goals page** (+ qa-tester fake API for goals): list, form, estimate hint, detail, plan section, errors.
8. [x] **frontend-react: calendar integration.** Bubbles, dots, goal markers, toggle, legend, Day panel cards, Edit & accept route, run details link, invalidations, i18n, bundle budget.
9. [x] **azure-infra:** Bicep params/env, nginx per-route timeout (+ header/timeout check in `deploy-artifacts`), `ci.yml` full-stack env; `az bicep build/lint`, actionlint.
10. [x] **qa-tester: E2E** (fake API, phone, axe; full-stack journey).
11. [x] **reviewer**, then **architect** (docs, deviations). The main agent updates CLAUDE.md, commits "M9: adaptive training plan" and stops for approval.

### 6.2 Part B: user-confirmed gates (each needs the user's explicit "yes")

| Gate | What | Cloud change |
|---|---|---|
| **G-P1** | Merge, then approve `deploy.yml`: the migration job applies `TrainingGoals` (additive); the app runs with `Goals:Enabled=false` (appsettings default), so nothing is visible. The nginx image carries the per-route timeout. | schema; images |
| **G-P2** | `azd env set PLANS_UI_ENABLED true` → `azd hooks run preprovision` → `azd provision` (first time with the new parameters; later `--no-state`) → `azd hooks run postprovision`. Live check as a guest: Goals nav, demo goal and example plan, suggestions in the diary, accept → planned run; Generate shows "not available" (`PLANS_AI_ENABLED` still false). | env |
| **G-P3** (optional, costs ≈ $0.5) | Live eval with the user's `az login`: `RD_AI_LIVE_EVAL=1` + endpoint/deployment, the six plan cases × 3. Record pass rate, output tokens (vs the 8,000 cap) and cost in §11. | none (model calls only) |
| **G-P4** | `azd env set PLANS_AI_ENABLED true` → `azd provision --no-state` → live check: one generation as a guest (quota 2) and the ledger row (`Feature = 1`). | env |

**Rollback / kill:** `PLANS_AI_ENABLED=false` (generation off, suggestions stay) and/or `PLANS_UI_ENABLED=false` (everything 503, UI hidden), then `azd provision --no-state`. `AI_ENABLED=false` still stops all AI.

## 7. Cost

| Item | Estimate |
|---|---|
| Per plan attempt (gpt-5.4-mini Global Standard, $0.75 / $4.50 per 1M) | input ≈ 9–10k tokens (prompt ≈ 3k + fact sheet ≤ 6k) ≈ $0.007; output ≤ 8k ≈ $0.036 → **≤ $0.044**, typical ≈ $0.025 |
| Per generation (≤ 2 attempts) | ≤ $0.09, typical ≈ $0.03 |
| Global ceiling | Unchanged from ADR 0028: 200 weighted attempts/day, a plan attempt weighs 3 → ≤ 66 plan attempts ≈ **$2.9/day** (≈ $5.8 with transport retries billed) |
| Expected use | A handful of plans a month ≈ **< $1/month** |
| SQL | A few reads and one transaction per generation; one range read per month view; negligible vs the free allowance |
| Azure resources | None new. The token alert (> 400k tokens/24 h) fires after ≈ 20 plan attempts in a day (informational) |

## 8. Risks

| Risk | Impact | Mitigation |
|---|---|---|
| 8,000 output tokens too few for 16 weeks | Truncated JSON → `schema` → 502 | Prompt asks for short descriptions; live eval measures; config raise (and the weight) without code |
| Calls slower than the timeouts | 502 `timeout` after quota is spent | 90 s attempt, 190 s total, nginx 200 s per route; evals report latency; async generation is the documented fallback (ADR 0040 alternatives) |
| Validator too strict (envelope v1 parameters) | Frequent 502s | Retry with rule codes; eval pass-rate gate G-P3; constants in one Domain file, bump `planFactSheetVersion` |
| Validator too lax | Unsafe plan suggested | Caps from the user's own history, 10 % growth, taper, hard-day rules; suggestions are opt-in; "not medical advice" line |
| Multiple cascade paths / NO ACTION on guest delete | Migration or cleanup fails | Verified in step 2; fallback soft links (ADR 0039 §3) |
| A plan holds a replica slot for minutes | Summaries get `ai-busy` | Small scale; slots per replica; after the review plans get at most `Ai:Plan:MaxConcurrentGenerations` (1) of the 2 shared slots, so a summary always finds one (§12 L2) |
| Prompt-injection or personal data in the fact sheet | Data leaves the EU (Global Standard) | No free text, names or places by construction; a test asserts the fact sheet's member list |
| Model text with markup or medical claims | XSS or harmful advice | Plain text rendering only; `markup` and `forbiddenTopic` rules |
| Grid clutter (runs + suggestions + proposals) | Unreadable cells | Shared `MAX_BUBBLES`, toggles, "+n" → Day panel |
| Predictor misleads (trail, few data) | Wrong target | `confidence: Low`, "hint" wording, never blocking |

## 9. Test plan outline (qa-tester)

**Domain.Tests (hand-computed):**
- **GoalRules:** every edge (999 / 1,000 / 250,000 / 250,001 m; elevation −1 / 10,001; date today+6 / +7 / +730 / +731; unchanged past-window date on update accepted; pace 149 / 150 / 900 / 901 s/km; type-specific requirements; `raceRunId` rules).
- **GoalTerm:** 84 vs 85 days, across DST.
- **Predictor:** Riegel with a 10K race → half (hand value); km-effort with elevation; basis choice (closest effort distance, recency tie, races before hard runs); `Rpe ≥ 8` filter; < 3 km ignored; no candidate → null; confidence edges 0.5 / 2.0.
- **Estimator warnings:** each code on/off at its threshold; `weeksToGoal` floor.
- **Envelope:** phases for W = 1…20 and T = 1/2/3 (taper-only at `W ≤ T + 1`; peak 1 vs 2 at W = 8); partial first week scaling; growth cap and 160 km ceiling; long-run ceilings per distance; capped horizon; commitments incl. other goals' dates and races past `planEnd`; floor of `V0`.
- **Fact sheet:** canonical JSON stable; **no member named title/name/notes/location/eventName/id** (reflection over all records + JSON scan); ≤ 24k chars for 16 weeks with 200 commitments.
- **Validator:** one adversarial draft per rule code (ADR 0040 §5), truncated JSON, rest/intensity combinations, pace bounds, collision drop counting, `empty` after drops, retry message contains codes and week dates only.
- **Suggestion → run:** mapping passes `RunRules` for every allowed type incl. rest.

**Ai.Tests:**
- Runner: summary behaviour unchanged (existing suite); per-feature quotas; weighted global sum (2 plan attempts + 5 summary = 11 units); guard per (feature, user); total deadline skips the retry; 429/credential paths per feature.
- Agent wire test (`FoundryWireTests` style): `max_completion_tokens` 8000, `reasoning_effort` low, `response_format.json_schema.name` `training_plan`, `strict: true`, schema equals ADR 0040 §4; client network timeout ≥ 90 s.
- Fake plan mode passes the validator for all eval fact sheets.
- **Eval cases (CI mode, scripted good + bad drafts; live mode opt-in):** HM in 10 weeks; over-ambitious 10k under 40:00 (warning present, paces bounded by the reference pace); trail 44 km / 1,000 m (long-run ceiling, nullable paces); goal 3 weeks away (taper only, no hard sessions in the last 3 days); collisions with planned runs (dropped, counted); capped horizon (goal 30 weeks away).
- Demo fixture valid for every "today" across a year (AC6).
- Prompt manifest hash and immutability.

**Api.Tests (Testcontainers):**
- Validation matrix per endpoint (camelCase keys); goal CRUD round trip; `version` conflicts; caps → `goal-limit`; status change deletes suggestions; plan field change sets `outdated`.
- Links: foreign/unknown/completed/non-race `raceRunId` → 400; run delete clears it; goal delete keeps accepted runs with `goalId` null; guest cleanup with mutual links (⚠).
- Estimate with the demo history (prediction present, basis = a demo race).
- Generate (fake): 200 + stored set; regenerate replaces Pending + Dismissed only; runs untouched; `replaceOtherGoals`; 422; 409s; quotas; 503 when `Ai:Plan:Enabled=false`; `droppedCount` with a planned run on a suggested date.
- Suggestions: range validation; accept default mapping (all fields) and with `{ run }`; accept of a dismissed one; run-limit 409; dismiss idempotent; isolation matrix; inventory (all Auth) and 401 sweep; 503 groups with `Goals:Enabled=false`; client-config.
- Fresh guest: demo goal + suggestions in weeks +6…+10, no ledger row.

**Vitest + RTL:** bubble/dot/marker rendering and `data-kind="suggestion"`; `MAX_BUBBLES` order runs → suggestions → proposals; cell accessible names; toggle persistence; week totals unchanged with suggestions; goal form (zod mirrors server, presets, type switching, target time parsing, estimate hint + "Use as target", server error mapping); plan section states for every problem type, cancel, replace dialog; invalidations after accept/generate/goal delete; i18n keys exist; `test:tz` for term and "today" under LA and Kiritimati.

**Playwright (fake API):**
- `goals.spec`: nav → Goals; create a 10K Performance goal from a preset with the hint; generate → suggestions on the page and in the diary (hatched bubbles, legend); accept one → planned run with the "from plan" marker; Edit & accept with a changed distance; dismiss → gone, "Show dismissed" → back, accept anyway; regenerate with another goal active → dialog → replaced; each error state via fake API switches; keyboard path cell → Day panel → Accept; axe (serious/critical = fail) on the Goals pages and the grid with suggestions.
- `goals.phone.spec` (360×640, LA): dots and markers, form usable, no horizontal scroll, axe.

**Full-stack (`e2e-fullstack/goals.spec.ts`, main only, fake model):** guest → Goals shows the demo goal and example plan → diary week +6 shows suggestions → accept one → it appears as a planned run and in week totals → `/insights` planned count includes it → create a goal → generate (fake) → suggestions replace the demo plan after confirmation.

**Manual (G-P2/G-P4):** VoiceOver over a cell with suggestions; phone Safari; one real generation reviewed by the user for plausibility.

## 10. Open questions (recommended defaults in bold; the plan proceeds with them unless the user objects)

**Confirmed by the user 2026-11-16: all recommended defaults below.**

1. **Timeout handling:** **nginx per-route 200 s for the plan POST** (one config line), or asynchronous generation (202 + polling; more code), or a 1-attempt plan within 120 s.
2. **Global budget weighting:** **plan attempts weigh 3 in the shared global budget** (keeps today's dollar ceiling), or unweighted sharing (ceiling up to ≈ $8.8/day), or a separate plan budget.
3. **Rest-day suggestions:** **allowed** (accepting creates a planned rest entry), or excluded from the model's run types.
4. **Collisions:** **only Race/Training/rest runs block a day; Body Bar / cross-training do not**, or any run blocks the day.
5. **Leaving `Active`:** **deletes the goal's suggestions**, or keeps them hidden.
6. **Demo goal:** **"Demo Beskids Trail 25K", 25 km / 900 m, Sunday of week +10, suggestions weeks +6…+10**, or link the demo's planned half marathon (week +5) as a `Race` goal (but weeks +1…+5 are already full of planned runs, so few suggestions would show).
7. **Output cap:** **8,000 tokens (outline), raised by configuration if G-P3 shows truncation**, or 12,000 from the start (weight 4).
8. **Toggle persistence:** **per browser (`localStorage`)**, or a server-side preference (migration).
9. **Token alert:** **leave `alert-rd-ai-tokens` at 400k/24 h** (may fire on a busy plan day), or raise it to ≈ 1M with M9.

## 11. Implementation notes & deviations

(Filled in during and after part A.)

### 11.1 Part A steps 1-3 (backend-dotnet; commits "M9 step 1/2/3")

**Verified (ADR 0039 §3 ⚠).** On Testcontainers SQL Server 2022, deleting a user whose goal links a run (`Goals.RaceRunId`) **and** whose run links the goal (`Runs.GoalId`) succeeds in one `ExecuteDelete` on `Users`; the two composite NO ACTION FKs are satisfied at the end of the statement. **No fallback was needed** (`GoalSchemaTests.Deleting_a_guest_with_mutual_goal_and_run_links_...`, plus `GoalGuestLifecycleTests` through the real `GuestCleanup`). The same tests show the database refuses a goal/run delete while a link exists, so the application clears (`GoalService.DeleteAsync`, `TrackDeletionService.DeleteRunAsync`).

**Domain (step 1)**
1. `GoalTerm` is an enum and the derivation is `GoalTerms.Of(targetDate, today)` (an enum cannot carry `GoalTerm.Of`).
2. The sketches' undefined types: `TrainingHistory` (record in `Domain/Goals`: ten complete ISO weeks oldest first as `HistoryWeek`, `MeanWeeklyDistanceMeters`/`LongestRunMeters` of the last 4 weeks, `SessionsPerWeek` over 10, `MeanPaceSecondsPerKm`, `RunningSessionsLast84Days` for the 422 check, `UsualWeekdays`), `EffortFact`, `TimePrediction`, `CommitmentFact`, `DismissedHint`, `PlanWeek`, `GoalSpec` (planning fields only), `GoalPlanRecord` (what `Goal.RecordPlan` stores). Added helpers: `PlanEnvelopeBuilder.Phases(weeks, taper)` (testable phase table) and `PlanEnvelopeBuilder.ReferencePace(goal, prediction, history)` (target pace, else predicted, else 0.90 x mean pace).
3. `Goal.Create/Update` take `today` and the resolved `LinkedRaceRun?` (the rules need them). `Update` sets `PlanOutdated` only when the goal **has a plan** (a flag on a plan-less goal would be invisible and meaningless); `RecordPlan` clears it.
4. Estimator interpretations of ADR 0039 §4: runs read are `[today-111, today]` (the last 112 days); races `[today-364, today]` (last 365 days, so an old result is not a basis); a race's elevation comes from the matching `RunFact` (`RaceFact` has none); "last 84 days" for `insufficient-history` is `[today-83, today]`. A 10K race predicts a half marathon at ratio 2.11, hence confidence **Low** (the rule is `E_goal/E_basis` in [0.5, 2]).
5. Envelope details left open by ADR 0040 §2: a taper-only span (W <= T+1) has no growth (pre-taper cap = base volume `V0`), and taper factors are aligned to the goal week (the last week gets the last factor; extra leading weeks repeat the first); the partial-first-week scaling applies to the weekly cap only (not the long-run cap, and not a partial goal week); `HasPlannableDays` is false when `planEnd < planStart`; caps are rounded to whole metres.
6. Fact sheet: the JSON omits members without information (null values, `fromThisGoal: false`, the default `planned` kind) so that 200 commitments + 60 hints + 16 weeks stay <= 24,000 characters (tested); `usualWeekdays` are lowercase English day names; `history.races` and per-week load/ACWR come from the `InsightsReport` the caller passes, which must therefore be built over `[today-364, today]`; commitments (nearest first) and hints are capped at 200/60 by the builder.
7. `TrainingSuggestion.Create` validates its input (run type 20-31 or 41, intensity/rest consistency, distance 1,000-60,000 required for non-rest, duration 600-21,600, pace pair 150-900 within 90 s, description 1-300); it is a safety net behind the validator of step 5, not a replacement.

**Persistence (step 2)**
8. Migration `20261116122838_TrainingGoals` as planned. `Goals.PlanHorizonCapped` and `PlanOutdated` have no SQL default (EF always writes them; the previous release never writes `Goals`). `AiGenerations.Feature` has `DEFAULT 0` as specified (tested: an old-release insert and the upgrade of existing rows both give `Summary`). The `INCLUDE Feature` on `IX_AiGenerations_Created` belongs with the ledger changes of step 4.
9. `AiGeneration.Create(..., AiFeature feature = AiFeature.Summary)`: optional last parameter, so existing call sites are unchanged; the ledger does not write `Plan` yet (step 4).

**Endpoints (step 3)**
10. Request validation of goals runs in the handler through `GoalRules` (it needs the caller's today and linked run): 400 with camelCase paths, the same shape as the DataAnnotations errors; `[StringLength]` on title/eventName/notes still document the limits. Order on create: 400, then 409 `goal-limit`.
11. `estimateGoal` validates with the new `GoalRules.ValidateEstimate`: targetDate from **today** to today+730 (no 7-day minimum, so the hint works for a close or existing goal), no type-specific requirements (a Performance form without a target time still gets a hint); distance, elevation and implied pace as for goals.
12. Estimate `warnings[].code` is a plain string (the lowerCamel code), not an enum schema, so the TS client types it as `string`; `confidence`, `basis.kind`, `term`, `plan.source` are PascalCase enums.
13. `ClientConfigResponse` gains `goals` and `AiClientConfig` gains `planGeneration`, which is **always false** until step 5 adds `Ai:Plan:Enabled` and the generation endpoint (`planGeneration = goals on && model available && plan switch on`). The five new problem-type constants (`plan-*`, `insufficient-history`, `goal-limit`) exist; only `goal-limit` is used so far. The generate endpoint is not stubbed.
14. `acceptTrainingSuggestion` with `{ run }` reports errors keyed `run.*`; the order is 404, 400, 409 `run-limit`. A concurrent double accept creates exactly one run (the loser answers 404).
15. Goal reads add no `userId`; `GoalDto.plan.model` is `PlanModelId ?? PlanModelDeployment` (null for the demo plan).
16. Frontend (minimal, caused by the regenerated types): `web/src/api/generated/schema.d.ts`; `goalId: null` in two `RunDto` test fixtures and `goalId: previous?.goalId ?? null` in the fake API run builder; `goals: { enabled: false }` and `planGeneration: false` in the fake client-config; `test/requests.ts` and `runFormMapping.test.ts` treat `goalId` as read-only; `summary.test.ts` expects the new `ai` shape.

### 11.2 Part A step 4 (ai-insights; commit "M9 step 4: shared AI generation runner")

17. `AiGenerationRunner` (Infrastructure/Ai) owns the guard, quotas, slot, attempt loop, total deadline, 429/credential handling and the ledger row; each feature passes one attempt delegate (run the agent + validate) and `AiRunSettings` (attempts, attempt timeout, optional total timeout, prompt version, deployment). `SummaryGenerator` keeps its constructor and its statics (`RetryAfterOf`, `IsCredentialFailure` now forward to the runner) and builds its runner internally with its own logger, so the M6 tests run unchanged; the plan service gets the runner from DI. `GenerationSlots`, `GenerationInFlightGuard`, `GenerationStatus` and `GenerationFailure` moved to `AiGenerationRunner.cs` (same namespace). Log lines now read "AI {Feature} generation: ..." (feature `summary`/`plan`).
18. `GenerationInFlightGuard` is keyed by (feature, user); the M6 overload `TryEnter(userId)` means `Summary`. `AiUsageLedger.GetStatusAsync(userId, isGuest, ct)` stays the summary overload of `GetStatusAsync(feature, userId, isGuest, ct)`. `AiGenerationRecord` gains an optional last `Feature` (default `Summary`). `IAiGenerationStore` changed: `GetUserGenerationTimesAsync(userId, feature, since, ct)` and `GetGlobalUsageAsync(since, planAttemptWeight, ct)`.
19. The weighted global sum is one grouped query (per feature and guest flag) weighted in C#: EF Core 10 cannot translate a conditional or multiplied `SUM` over the `tinyint` `Attempts` (it strips the `int` cast and fails in `GetSumWithoutSelector(Byte)`).
20. Migration `20261116130010_AiGenerationsFeatureIndex` rebuilds `IX_AiGenerations_Created` with `INCLUDE (IsGuest, Feature, Attempts)` (Attempts too, so the global sum is covered; the previous index included only `IsGuest`). Drop and re-create of a non-unique index: safe for the previous release.
21. `Ai:Plan:*` options and their validation are added in this step (the network timeout and the weight need them); `AiOptions.ClientNetworkTimeout = max(Ai:AttemptTimeout, Ai:Plan:AttemptTimeout)` (90 s by default) is the shared client's `NetworkTimeout`. `appsettings.json` gets the section in step 5.
22. Test support only (no assertion changed): the in-memory ledger store implements the new interface (its old unweighted `GetGlobalUsageAsync(since, ct)` stays as a helper), and the Api.Tests helper `GlobalLedgerAsync` weights plan rows by 3, because the summary global-budget tests set the limit to "current usage + 1" on the shared database, where plan rows of other tests now count 3 per attempt.

### 11.3 Part A step 5 (ai-insights; commit "M9 step 5: training plan generation")

23. Split as in M6: `TrainingPlanGenerator` (Infrastructure/Ai, no database: availability, fact-sheet size guard, the runner with the agent + validator attempt, retry with codes) and `TrainingPlanService` (the goal checks, history, commitments, envelope, fact sheet and the commit transaction). `TrainingPlanAgentProvider` mirrors the summary one. The plan prompt manifest is checked at startup by a separate `PlanPromptStartupCheck`, so `AiStartupCheck` is unchanged.
24. Validator interpretations of ADR 0040 §5: draft parsing is as strict as the schema (exactly the 2 + 9 members with their JSON types, else `schema` at `draft`); a session with a per-field `schema` error, or outside `[planStart, planEnd]` (`dateRange`), is left out of the later rules; rest suggestions count for neither `weeklyVolume`, `frequency` nor `emptyWeek`; the weekly `hardSessions` count applies only to weeks with a hard suggestion (hard commitments alone are not the model's doing); hard commitments are races and the history's hard types (tempo, intervals, racePace, hillRepeats, fartlek); an empty plan also reports `emptyWeek`. `DropCollisions`, `BlockedDates` and `IsHardCommitment` are public (the commit step and the fake use them). Violation locations are only parsed dates, envelope week starts or fixed paths (`notes`, `sessions`, `draft`, `sessions[n].member`); the retry note lists at most 40 of them.
25. The strict schema is the ADR 0040 §4 text verbatim (`TrainingPlanAgent.ResponseSchemaJson`), not generated from a type (no `$ref` to inline, exact enum order); a test compares it with the ADR and with what goes over the wire. `TrainingPlanDraft` keeps the codes as strings until validation.
26. The fake's plan mode is chosen by the response-format schema name `training_plan`, or, when called without options (the Api.Tests scripted client), by the user-message header `Plan fact sheet:`. Its rules are documented on `RuleBasedFakePlanWriter`; it passes the validator for the six eval cases and for the demo goal on every day of 2028.
27. `TrainingHistoryReader` (backend file) gains `ReadCommitmentsAsync` and `ReadDismissedAsync`: every run in `[planStart, planEnd]` (race-category runs as `race`), planned race-category runs in `(planEnd, targetDate]`, other Active goals' dates in `[planStart, planEnd]`; `fromThisGoal` from `Runs.GoalId`.
28. The fact sheet's report is built with `InsightsReportBuilder` from the same 112-day facts and 365-day races the estimate uses (not through `IInsightsReportService`), so everything shares one "today"; 112 days cover the ten history weeks and their ACWR lead-in.
29. The commit runs with `CancellationToken.None` (the model's work is paid for, like the M6 summary store). A goal that changed, left `Active` or was deleted meanwhile answers 409 `concurrency-conflict` (also for a delete, not 404). The 400 for `goalId` uses the validation-problem shape (`errors.goalId`).
30. Infra, one line each, needed so the image passes its startup check: the API and migrate Dockerfiles copy `prompts/training-plan`. The Api project copies `seed/demo/training-plan.json` to the output (read by step 6).
31. Demo plan (for step 6): `seed/demo/training-plan.json` holds the demo goal (`Distance`, "Demo Beskids Trail 25K", 25,000 m, 900 m, `Trail`, Sunday of week +10) and 20 sessions in weeks +6...+10 (2 rest days; trail long runs, hill/tempo sessions; no paces, as the goal is a trail), week-relative like `runs.json`. The pure parser/expander is Domain `DemoTrainingPlanTemplate` (`Parse`, `Goal.TargetDate/Spec(today)`, `Draft(today)`); `DemoTrainingPlanFixtureTests` validates it against the pristine demo for every day of 2028 (also the goal against `GoalRules` and each session through `TrainingSuggestion.Create` and `RunRules`).
32. Size check (plan §8 risk): the fake's 16-week plan (capped-horizon case, 48 sessions) is 11,491 characters, about 3,600 tokens (chars/3.2), i.e. 45 % of the 8,000 cap before reasoning tokens; a model writing 4-5 sessions a week would be about 5-6k. Fact sheets of the eval cases are 2.9-4.8k characters; prompt v1 is 5.4k characters (about 1.3k tokens).
33. Tests changed beyond the new files: the endpoint inventory (`ai-plan` in `SpecificRateLimits`, goal-group count 10 with the plan route asserted separately), the 401 sweep (10 routes), the 503 group list, a stale comment in the client-config test (its assertions hold); `AiTestHost` raises the `ai-plan` burst limit; `ScriptedChatClient.ReplyValidTransformed`. The plan API tests delete their own ledger rows afterwards so the summary tests' global budgets on the shared database stay as they were.

### 11.4 Part A step 6 (backend-dotnet; demo seed)

34. `DemoTrainingPlan` is its **own singleton** in `Infrastructure/Goals/` (it reads `training-plan.json` from `Guests:DemoDataPath`, next to `runs.json`, parsed once per process), not part of `DemoDatasetLoader`, because existing tests load demo data from directories that have no `training-plan.json`. The startup check is a separate hosted service, `DemoTrainingPlanStartupCheck.cs`, registered right after `DemoDatasetStartupCheck` under the same condition (not in job mode).
35. Seeding happens **regardless of `Goals:Enabled`**: a guest created while the feature is off already has the demo goal and suggestions when it is switched on (they are invisible until then, the groups answer 503).
36. An invalid or missing demo plan **at seed time** (`DomainRuleException` or `IOException`) logs a warning (exception type and message length only) and seeds the guest **without** the goal and suggestions, so guest sign-in is never blocked; the **startup check still fails the host**, so a broken package does not reach a running replica.
37. The demo plan is validated by the same `TrainingPlanValidator` against a fact sheet built from the in-memory demo diary for the same "today". Its commitments helper (`DemoTrainingPlan.Commitments`) duplicates the one in Ai.Tests `PlanSheets` (Infrastructure cannot reference test code). With the goal on Sunday of week +10 it is 70–76 days away depending on the weekday, so it is always **Short-term**.

### 11.5 Part A steps 7–8 (frontend-react; one combined commit "M9 steps 7-8")

38. **One commit for steps 7 and 8** (the calendar and the Goals page share the queries and the accept path).
39. The Goals page loads a goal's suggestions as **two consecutive `listTrainingSuggestions` ranges** from today (92 + 92 days, about 26 weeks) and merges them: there is no get-one-suggestion or per-goal endpoint, and a plan can reach 112 days against the 92-day list limit. For the same reason the diary's "Edit & accept" panel finds the suggestion in the month's grid-range list (`includeDismissed: true`) and shows "no longer available" when it is gone.
40. `RaceRunSelect` lists the caller's planned race runs from `listRuns` in a 92-day window around the target date (−45 … +46 days); picking one sets the date.
41. **No goal location field** in the form (the API accepts and returns `location`; the SPA neither sends nor shows it). Follow-up §13.
42. **Goal markers ignore the "Show suggestions" toggle** (the toggle hides suggestion bubbles only); goal markers come from the cached `listGoals`.
43. Goal **delete** is on the detail page (with a confirmation), not on the cards.
44. `RunForm` gained `createWith?: (body) => Promise<RunDto>` (create only: replaces `POST /api/runs`; Edit & accept passes `acceptTrainingSuggestion` with `{ run }`) and `onSaved(run)` now receives the saved run (Edit & accept navigates to it with `replace`).
45. The **"Suggested" badge is hidden below `lg`**: between `sm` and `lg` the bubble keeps the hatching and the sparkles icon; below `sm` the phone dot (sparkles) is shown; the legend explains both.
46. Fake API defaults: `goals.enabled` false and `planGeneration` false (existing tests unchanged); the fake plan and estimate are simplified (deterministic sessions, no envelope), enough for the UI states.

### 11.6 Part A step 9 (azure-infra)

47. `Ai__Plan__Enabled` is in **`sharedApiEnv`**, so the guest cleanup job (`cleanupEnv = sharedApiEnv + …`) gets it too: harmless, it never generates. The race import job has its own env list and does not get it. `Goals__Enabled` is API-only (`apiOnlyEnv`).
48. `infra/main.json` (the compiled ARM template) is regenerated and committed with the Bicep change.
49. nginx: a regex location `~ ^/api/goals/[0-9a-fA-F-]+/plan/?$` with 200 s read/send timeouts. A regex location inherits nothing from `/api/`, so it **repeats the whole proxy block** (upstream, TLS verification, forwarded headers, 21 MB body limit, hidden upstream headers) and **includes the security-headers snippet**. It matches every method on that path (only POST exists).
50. `verify-web-image.sh` checks the per-route timeout with the stub upstream's `?delay=`: a 150 s response on the plan path is not cut, while the existing 90 s check covers the rest of `/api`.
51. No Production override for any other `Ai:Plan:*` key (the `appsettings.json` defaults apply in Azure).

### 11.7 Part A step 10 (qa-tester)

52. **Results (2026-11-16, reported by qa-tester):** `dotnet test` 4,224 passed, 12 skipped (the live evals); Vitest 1,149 passed; `test:tz` 937 × 2 passed; Playwright (fake API) 133 passed, 3 skipped; full-stack 11/11 passed against CI-style containers with the fake model.
53. New: `web/e2e-fullstack/goals.spec.ts` (demo goal and example plan, accept, generate with the fake model and the replace confirmation), Api.Tests `GoalPlanIsolationTests` and `GeneratePlanOtherGoalRulesTests`.
54. `web/e2e-fullstack/insights-summary.spec.ts` now follows `client-config.ai.summaryGeneration`: with `Ai__Provider=Fake` in the CI full-stack env (needed for plans) summary generation became available and the old "not configured" assertion failed.
55. **Not run:** the manual checks (VoiceOver over a cell with suggestions, phone Safari, one real generation reviewed for plausibility) and the live eval; they belong to gates G-P2…G-P4.

### 11.8 Follow-up "plan reliability" (ai-insights, 2026-11-16, branch `m9-plan-reliability`)

56. **G-P3 live eval** (gpt-5.4-mini Global Standard, prompt v1, fact sheet v1): first run **12/18**. With the new failure diagnostics (commit "M9: plan generation failure diagnostics for the live eval": per-attempt rule codes, locations, tokens and finish reason; `RD_AI_LIVE_DUMP_DIR` for raw answers, test code only), a second run gave **14/18, 3 of them saved by the retry**. Nothing was truncated (at most ~4.5k output tokens per attempt, cap 8,000). Every failure was `invalidOutput` on both attempts.
57. **Root causes** (from the dumped answers, re-validated offline):
    - `weeklyVolume` on the partial first week (2026-11-16, cap 28,286 m, nothing fixed) in 7 of 12 failed attempts: the model wrote round sessions adding up to 30–38 km. The retry note gave only the date, so attempt 2 often overshot again.
    - `schema` at `sessions[n].paceMinSecondsPerKm` (trail case, up to 12 per attempt). These were **long trail runs with pace ranges 95–135 s wide** (e.g. 390–485, 390–525), above the existing 90 s limit. The limit was already 90 s, not 60 s as first assumed. The location named the faster bound although the slower bound or the width was the problem.
    - Occasional genuine `pace`, `phase`, `taperIntensity` and `longRun` violations: the retry handles them, and the rules stay.
58. **Changes** (one commit "M9: plan reliability — weekly caps the model can hit, precise retry notes, pace spread"):
    - The envelope rounds `maxDistanceMeters`, `availableDistanceMeters` and `maxLongRunMeters` **down to 500 m** (`PlanEnvelopeRules.CapStepMeters`, `FloorToStep`). The fact sheet sends these values and the validator checks the same ones. The +5 % tolerance is unchanged. Envelope v2, **plan fact sheet v2** (same shape).
    - **Plan prompt v2** (`prompts/training-plan/v2.md`, `current: 2`; v1 stays listed and loadable). It tells the model to add up each week's `distanceMeters` and stay at or below `availableDistanceMeters`, that the first week is partial, that caps are multiples of 500 m, the pace widths with examples (90 s: 390 and 480; 135 s for long/trail runs and, on a trail goal, easy/recovery runs: 390 and 520), and to fix the numbers a rejection note lists.
    - **Retry note with numbers**: `PlanViolation` gains optional validator-computed integers `Value`/`Limit`. These are set for `weeklyVolume` (the suggested sum vs `availableDistanceMeters`), `longRun` (distance vs `maxLongRunMeters`) and a too-wide pace range (width vs 90). The note adds "Numbers to fix: week 2026-11-16: your running sessions add up to 33000 m, its availableDistanceMeters is 28000 m; …". It still contains only codes, safe locations and integers, never model text.
    - **Pace range** (second commit "M9: plan reliability — wider pace range for long and trail runs", coordinator decision): the width may be **135 s for `long` and `trailTraining`, and for `easy`/`recovery` when the goal surface is Trail**, and 90 s otherwise. One rule, `TrainingSuggestion.MaxPaceSpreadSeconds(runTypeId, trailGoal)`, is used by the validator and by `TrainingSuggestion.Create(…, trailGoal)`; the plan service and the demo plan pass the goal surface. Folded into prompt v2, which was not yet released. A missing or too-slow faster bound is reported at `paceMinSecondsPerKm`. A missing slower bound, a slower bound above 900, faster > slower, or a range wider than 90 s is reported at **`paceMaxSecondsPerKm`**, which is now an allowed retry-note location.
59. Not changed: the API contract (no OpenAPI change), the ledger, quotas and the data-residency position. Only app-computed numbers are added to what the model sees. The live eval was not re-run; expect fewer first-week `weeklyVolume` failures and more precise retries. The trail case's 95–135 s ranges on long runs now pass.

## 12. Review (reviewer, 2026-11-16)

**Verdict: approve with follow-ups; no blocker, no high finding.** Fixes landed in three commits.

| # | Finding | Fix | Commit |
|---|---|---|---|
| M1 | Editing a goal whose linked race run had since been completed or moved failed with 400 on `raceRunId`, although the user did not touch the link | `GoalRules.Validate(..., previousRaceRunId)`: an **unchanged** link only has to exist and be the caller's; the Race-category / Planned / dated-on-`targetDate` rules apply when the link is set or changed (ADR 0039 amendment) | `f73be6c` |
| L1 | Concurrent goal delete vs accept / plan commit could insert rows for a goal being deleted (FK error → 500) | Per-user **`GoalWriteLock`** (`sp_getapplock` on `user-goals:<id>`, exclusive, transaction-owned, 15 s timeout) taken by goal delete, suggestion accept and plan commit; FK 547 safety nets map to **404** (accept) and **409 `concurrency-conflict`** (plan commit); dismiss of a concurrently removed suggestion → **404** | `f73be6c` |
| nit | Goals whose suggestions were replaced by `replaceOtherGoals` still showed a plan with nothing pending | Replaced goals are marked **`plan.outdated`** | `f73be6c` |
| L2 | A plan could hold both replica slots (summaries `ai-busy`), and guests' plans could use up the whole guest-global budget | `Ai:Plan:MaxConcurrentGenerations` (default 1; validated ≤ `Ai:MaxConcurrentGenerations` − 1 when that is ≥ 2; a plan needs a plan slot **and** a shared slot) and `Ai:Plan:GuestGlobalPer24h` (default 20 weighted attempts inside the guest budget of 60; → 429 `ai-quota`, `scope: global`, `limit: 20`) | `f44f8d0` |
| L3 | A session could pass with distance and duration each in range but an impossible pace (30 km in 10 min) and become an impossible planned run on accept | Implied pace **150–900 s/km** when both distance and duration are set: validator rule `schema` at `sessions[n].durationSeconds`, mirrored in `TrainingSuggestion.Create`'s own validation (`HasPlausibleImpliedPace`) | `f44f8d0` |
| L4 | Cancel gave no hint that the generation still counts, and a plan committed after the abort did not show | Cancel hint ("still counts toward your plan limit"), "Cancelled. If the plan was already finished, it will appear shortly." and a refresh of the goal and suggestions after cancel | `aaf266f` |
| L5 | Goal caps are count-then-insert (two concurrent creates can exceed the cap by one) | **Not fixed (accepted)**: same pattern and risk as the run caps; bounded overshoot, no security impact | — |

Notes (no change): the suggestions toggle is per browser (approved default §10.8); the agent options (slots, timeouts) are read once at startup, so changing them needs a restart (as for summaries). One flaky M8 Playwright test (`races.spec.ts` "the details dialog", axe) was seen once and passed on re-run; unrelated to M9.

## 13. Follow-ups

1. **Goal location field** in the goal form (the API already supports `location`).
2. **Polish strings** for `goals.json` and the new `calendar.json` keys (keys are ready).
3. A **delayed second refresh after cancel** (a plan committed later than the first refresh still appears only on the next navigation or the 1-minute stale time).
4. **Deterministic tests for `GoalWriteLock`** (today the concurrency is covered by racing requests, not by a held lock).
5. Optional **hard goal cap** (lock or filtered count in the insert) if L5 ever matters.
6. **G-P3 live eval** to confirm the 8,000-token output cap (record tokens, pass rate and cost in §11). Run twice (12/18, then 14/18; no truncation, §11.8); re-run after the plan reliability changes.
7. ~~**Pace range width for long trail runs**~~ **Resolved (2026-11-16):** 135 s for `long`/`trailTraining`, and for `easy`/`recovery` on a trail goal; 90 s otherwise (§11.8, ADR 0040 amendment "plan reliability" item 4).

## Assumptions

1. Suggestions are advice, not a coaching or medical service; the UI shows the existing "not medical advice" line on the plan section.
2. One plan language (`en`); the prompt and fact sheet carry `language` so `pl` is a prompt line later.
3. The M5 metrics v1 are suitable inputs (weekly distance, load, ACWR); no new metric definition is needed (`metricsVersion` stays 1).
4. "Running distance" for volume caps uses the calendar rule (Race + Training categories); cross-training counts only through load (ACWR) in the fact sheet.
5. Elevation is handled by km-effort only; no separate trail/terrain factor and no altitude.
6. Users accept that a regenerate forgets earlier dismissals (they are hints for one generation).
7. The general rate limiter is enough for CRUD, estimate, list, accept and dismiss; only generation has its own policy.
8. No new CI job; existing jobs pick up the new tests; `deploy-artifacts` checks the nginx config.
9. The Global Standard deployment and its data-residency position (ADR 0026 amendment) are unchanged; the plan fact sheet is metrics-only.
