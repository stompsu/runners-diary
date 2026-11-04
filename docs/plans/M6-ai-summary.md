# M6 – AI summary: plan

**Status:** Approved 2026-11-04 (all §9 defaults; claims verified against Microsoft Learn). Implemented on branch `m6-ai-summary`, awaiting CI + approval; deviations in [§12](#12-implementation-notes--deviations-2026-11-04).
**Builds on:**
- M2: the `Owner` filter, guests, `AppClaims.Kind`, rate-limiting setup and problem types.
- M4: the precedent of "no cloud before M7" with an opt-in path (ADR 0022) and the `feature-unavailable` problem.
- M5: `IInsightsReportService`, the Domain `InsightsReport` with `metricsVersion` 1, the golden demo report and `DemoInsightsTests`, the lazy `/insights` route. Initial load **178.7 kB** gzip, limit 201.5 kB.

**Related ADRs:** [0010](../adr/0010-current-user-and-data-scoping.md) scoping · [0011](../adr/0011-guest-accounts-lifecycle.md) guests · [0024](../adr/0024-training-metrics-definitions-v1.md) metrics v1 (+ M6 notes) · [0025](../adr/0025-insights-report-computation-and-api.md) report + M6 contract.
**New (proposed):**
- [0026](../adr/0026-training-insights-agent-and-grounding.md) agent, fact sheet, placeholder-only numbers, validation, evals;
- [0027](../adr/0027-ai-model-provider-foundry-and-local-dev.md) provider, client stack, keyless auth, local dev, model and cost;
- [0028](../adr/0028-summary-cache-quotas-and-demo-summaries.md) cache, explicit generation, quotas, demo example summaries.

## 1. Goal & acceptance criteria

**Goal (brief §4 M6):** a Training Insights agent on Microsoft Foundry via Microsoft Agent Framework, grounded only in computed metrics, cached, with a "regenerate" button, token/cost guardrails and evaluation cases.

Also required by the brief:
- §1.1.8: period narrative (went well, risks, suggestions); the LLM must not invent data;
- §1.3: guests are limited to about 3 generations, and pre-generated demo summaries work without tokens.

**Constraint:** no cloud resource in M6. Everything below is accepted against a fake `IChatClient`. A real deployment is an opt-in local check (§8).

**Out of scope:**
- auto-generation on view or on data change (§9 Q3);
- function tools (§9 Q1);
- chat or follow-up questions;
- the Polish prompt and fixtures (the structure is ready);
- Bicep for Foundry (M7);
- an LLM judge in CI.

| # | Criterion | How verified |
|---|---|---|
| AC1 | `FactSheetBuilder` (Domain) projects `InsightsReport` per ADR 0026 §1:<br>• values verbatim; only allowed annotations;<br>• weeks ≤ 16 weeks, else months;<br>• no `acwr.asOf.*` when `asOf < from`;<br>• no free-text fields;<br>• canonical JSON → stable SHA-256 fingerprint.<br>Golden fact sheets per eval case. | Ai.Tests |
| AC2 | `SummaryValidator` (Domain) implements the 8 rule codes (`schema`, `unknownKey`, `literalNumber`, `missingCitation`, `forbiddenTopic`, `raceWeekPeak`, `estimatedLoad`, `markup`). Each code has passing and failing cases. | Ai.Tests |
| AC3 | Pipeline: template periods (empty, plannedOnly, fewSessions) **never call the client**. One retry on invalid output carries rule codes only. A second failure → 502 `summary-failed`, nothing stored. Per-attempt timeout → 502 `timeout`. Cancellation propagates. Tokens and outcome are written to the ledger; no prompt or response text is logged. | Ai.Tests + Api.Tests |
| AC4 | Agent: a `ChatClientAgent` with the v1 prompt as instructions, a JSON-schema response format, `MaxOutputTokens` 1 500 and reasoning effort low. The prompt manifest hash is checked at startup and in a test; editing `v1.md` without a new version fails CI. | Ai.Tests |
| AC5 | Eval cases run in CI against the scripted fake: load spike, taper before a race, empty month, insufficient history, estimated-RPE share, partial period, race-week peak (demo), wholly future period. A live mode (`RD_AI_LIVE_EVAL=1`) is skipped in CI and documented. | CI + manual (§8) |
| AC6 | `GET /api/insights/summary` and `POST /api/insights/summary` per §3.1–3.3: validation as the report (+ `lang`), `private, no-store`, states `none`/`fresh`/`stale`, `generation.availability`, problem types, `ai-summary` rate policy on POST, both **Auth** in the inventory, 401 sweep. | Api.Tests |
| AC7 | Cache: a POST stores a row. A GET then returns it `fresh`. Each of these makes it `stale`: a run edit, a different prompt version, a different deployment, a new `today` for a partial period. `regenerate: false` on a fresh row makes **no** model call and uses no quota. A stale summary is returned with its stored values. | Api.Tests |
| AC8 | Quotas from the ledger: user 10 per 24 h, guest 3 per account, global 200 and guest-global 60 per 24 h → 429 `ai-quota` with `scope` and `resetsAt`. Failed attempts count. Concurrent POST by the same user → 409. Slots full → 503 `ai-busy`. Kill switch and `Provider=None` → 503 `feature-unavailable` while reads still work. | Api.Tests (low limits via config) |
| AC9 | Isolation: summaries and quotas are per caller (two guests, two users, guest vs user). A's summary for the same period is never returned to B. **No user free text reaches the model:** canary strings in notes, titles, race names, gear and weather are absent from the captured request. | Api.Tests |
| AC10 | Demo: a fresh guest's default view (12 weeks vs previous) returns `source: demo` with **no AI configured**. The fixture's predicates hold and it validates for **every "today" across a year**. After the guest breaks a claim (e.g. deletes the spike week's long run), the fixture is no longer served. | Ai.Tests + Api.Tests |
| AC11 | UI: an "AI summary" card on `/insights`, below the KPI grid:<br>• headline, went well, watch-outs, suggestion and data notes, with values formatted by the existing unit helpers;<br>• a source badge (AI-generated / Example / Automatic);<br>• creation date, model and prompt version;<br>• the "not medical advice" line;<br>• Generate / Regenerate with remaining count;<br>• states: none, generating (cancelable), fresh, stale, quota exhausted, not configured, busy, error.<br>All strings are i18n keys. | Vitest + Playwright |
| AC12 | Accessibility: the card is a `<section aria-labelledby>`; status changes are announced through a polite live region; buttons have accessible names and state the reason when disabled; no information is conveyed by colour alone. axe (serious/critical = fail) on desktop and on `phone-la` for: demo, generated, stale, quota exhausted, not configured, error. | Playwright + axe |
| AC13 | Bundle: initial load +≤ 0.5 kB gzip (client-config type only). The card lives in the lazy Insights chunk with **no new npm dependency**. | CI `bundle-budget` |
| AC14 | `RunnersDiary.Domain` still has no package references. OpenAPI and TS schema are regenerated. `dotnet build` is warning-free with the new NuGet packages. All CI jobs are green. The full-stack journey shows the demo summary for a real seeded guest. | CI |

## 2. Files / projects by owning agent

### ai-insights
```
src/RunnersDiary.Domain/Insights/Narrative/
  FactSheet.cs, FactSheetBuilder.cs, FactSheetJson.cs (canonical writer + SHA-256), FactUnit.cs
  SummaryDraft.cs (model output shape), SummaryValidator.cs (+ ValidationRuleCode), SummaryValues.cs (resolve keys)
  SummaryTemplates.cs (empty / plannedOnly / fewSessions selection), DemoSummaryFixture.cs (+ predicate eval)
src/RunnersDiary.Infrastructure/Ai/
  AiOptions.cs (+ startup validation), PromptCatalog.cs (manifest, hash check), TemplateCatalog.cs, DemoSummaryCatalog.cs
  ChatClientFactory.cs (None | Foundry | Fake), RuleBasedFakeChatClient.cs (Development/Testing only)
  TrainingInsightsAgent.cs (ChatClientAgent + RunAsync<SummaryDraft>), SummaryGenerator.cs (attempts, retry, timeout, slots, in-flight guard)
  AiUsageLedger.cs (quotas, ledger writes, 30-day purge hook), InsightsSummaryService.cs (read precedence, upsert, cap)
prompts/training-insights/
  manifest.json, v1.md, templates/en.json, demo/en/12w-previous.v1.json, README.md (how to add v2, a language, a fixture)
tests/RunnersDiary.Ai.Tests/                    # new project (added to RunnersDiary.slnx)
  FactSheet/*, Validation/*, Pipeline/*, Prompts/PromptManifestTests.cs, Demo/DemoSummaryFixtureTests.cs
  Evals/{EvalCases.cs, ScriptedChatClient.cs, EvalRunner.cs, LiveEvalTests.cs}, Evals/cases/<case>/{fact-sheet.json, good-draft.json, bad-*.json}
```

### backend-dotnet
```
src/RunnersDiary.Domain/Ai/{AiSummary.cs (IUserOwned), AiGeneration.cs (ledger, not owned)}
src/RunnersDiary.Infrastructure/Persistence/Configurations/{AiSummaryConfiguration.cs, AiGenerationConfiguration.cs}
src/RunnersDiary.Infrastructure/Persistence/Migrations/<ts>_AiSummaries.cs
src/RunnersDiary.Infrastructure/Guests/GuestCleanup.cs          # + ledger purge (> 30 days) in the same run
src/RunnersDiary.Infrastructure/DependencyInjection.cs           # Ai services, options
src/RunnersDiary.Api/Features/Insights/{GetInsightsSummary.cs, GenerateInsightsSummary.cs, InsightsSummaryContracts.cs, InsightsSummaryMapping.cs}
src/RunnersDiary.Api/Features/ClientConfig/GetClientConfig.cs    # + ai
src/RunnersDiary.Api/Http/ProblemTypes.cs                        # ai-quota, ai-busy, summary-in-progress, summary-failed
src/RunnersDiary.Api/RateLimiting/{RateLimitingOptions.cs, RateLimitingSetup.cs}   # policy ai-summary
src/RunnersDiary.Api/appsettings.json, appsettings.Development.json             # Ai, RateLimiting:AiSummary
src/RunnersDiary.Api/RunnersDiary.Api.csproj                     # prompts/** copied as content (like seed/demo)
src/RunnersDiary.AppHost/AppHost.cs                              # Ai user-secrets → env (ADR 0027 §4)
src/RunnersDiary.Api/openapi/runnersdiary-api.json               # regenerated
Directory.Packages.props                                         # §7
```

### frontend-react
```
web/src/features/insights/summary/
  SummaryCard.tsx, SummaryBody.tsx, SummaryActions.tsx, SummaryStatus.tsx (live region)
  summaryText.ts (placeholder → formatted nodes), summaryFormat.ts (unit → existing formatters), summaryQueries.ts
web/src/features/insights/InsightsPage.tsx                       # card placement (§3.6)
web/src/features/runs/runQueries.ts                              # + invalidate getInsightsSummary
web/src/auth/useClientConfig.ts                                  # ai section
web/src/i18n/en/insights.json                                    # summary.* keys
web/src/api/generated/schema.d.ts                                # regenerated
```

### qa-tester
```
tests/RunnersDiary.Api.Tests/Insights/{InsightsSummaryEndpointTests.cs, InsightsSummaryQuotaTests.cs, InsightsSummaryCacheTests.cs}
tests/RunnersDiary.Api.Tests/Infrastructure/*                    # factory: Ai:Provider=Fake + DI override of IChatClient with ScriptedChatClient
tests/RunnersDiary.Api.Tests/Isolation/IsolationMatrixTests.cs, Security/EndpointAuthorizationTests.cs   # + summary routes
web/src/test/fakeApi/insightsSummary.ts + fixtures (demo, model, stale, template, quota, notConfigured, error)
web/src/features/insights/summary/*.test.ts(x)
web/e2e/{insights-summary.spec.ts, insights-summary.phone.spec.ts}, web/e2e-fullstack/insights.spec.ts (+ demo summary)
```

### azure-infra
**No change in M6.** The new test project is picked up by `dotnet test`; live eval never runs in CI. The M7 inputs are in ADR 0027 §7.

### architect
After implementation:
- `api-contract.md` (endpoints, problem types, limits);
- `data-model.md` (two tables; replace the "AiSummaries" deferred row);
- `architecture.md`;
- ADR status and deviations (§12).

## 3. Contracts

### 3.1 Endpoints (problem+json; no route parameter names a resource, so no 404)

| Method & path | operationId | Policy | Success | Errors |
|---|---|---|---|---|
| `GET /api/insights/summary?from&to&compare&lang` | `getInsightsSummary` | Auth, general limiter | 200 `InsightsSummaryResponse`, `Cache-Control: private, no-store` | 400 (`from`, `to`, `compare`, `lang`), 401, 429 |
| `POST /api/insights/summary` body `GenerateSummaryRequest` | `generateInsightsSummary` | Auth, rate policy **`ai-summary`** (replaces the general one) | 200 `InsightsSummaryResponse` (`state: fresh`) | 400, 401, 409 `summary-in-progress`, 413, 429 (`rate-limited`, `ai-quota`), 502 `summary-failed`, 503 (`feature-unavailable`, `ai-busy`) |

Validation is identical to `getInsightsReport` (dates 1950–2100, `to ≥ from`, span ≤ 366, `compare` case-sensitive, default `previous`), plus `lang ∈ { "en" }` (default `en`). The POST body rejects unknown members (ADR 0012).

**Order in POST:**
1. validate;
2. kill switch / provider (503);
3. build the report and fact sheet;
4. template period → return the template (no quota);
5. if `!regenerate` and a fresh row exists → return it;
6. in-flight guard (409);
7. quotas (429);
8. slot (503);
9. generate (≤ 2 attempts) → ledger row → validate → upsert → 200, or ledger row → 502.

### 3.2 Shapes
```ts
GenerateSummaryRequest = { from: string; to: string; compare?: "none" | "previous" | "previousYear"; lang?: "en"; regenerate?: boolean }

InsightsSummaryResponse = {
  period: { from: string; to: string; compare: string; lang: string };
  state: "none" | "fresh" | "stale";
  summary: InsightsSummaryDto | null;
  generation: {
    availability: "available" | "notConfigured" | "quotaExhausted" | "notNeeded";  // notNeeded = template period
    used: number | null; limit: number | null;          // account quota (null when notConfigured)
    resetsAt: string | null;                            // UTC; null for guests (account lifetime)
    scope: "rolling24h" | "account" | null
  }
}
InsightsSummaryDto = {
  source: "model" | "demo" | "template";
  createdAt: string | null;                             // UTC; model rows only
  metricsVersion: number; factSheetVersion: number; promptVersion: number | null; model: string | null;
  headline: SummaryText; wentWell: SummaryText[]; watchOuts: SummaryText[]; suggestion: SummaryText | null; dataNotes: SummaryText[];
  values: { [key: string]: SummaryValue }               // every key cited in any text
}
SummaryText  = { text: string /* "{key}" placeholders */; keys: string[] }
SummaryValue = { number: number | null; text: string | null;
                 unit: "meters" | "seconds" | "load" | "ratio" | "percent" | "share" | "secondsPerKm" | "secondsPerKmPerWeek"
                     | "count" | "days" | "date" | "band" | "runType" | "raceCategory" | "raceStatus" | "flag" | "stars" }
ClientConfigResponse.ai = { summaryGeneration: boolean }   // provider configured and enabled (anonymous endpoint: no quota info)
```

**New problem types:**

| `type` | Status | Extensions |
|---|---|---|
| `urn:runnersdiary:problem:ai-quota` | 429 + `Retry-After` when `resetsAt` is known | `scope: "account" \| "global"`, `limit`, `resetsAt` |
| `urn:runnersdiary:problem:ai-busy` | 503, `Retry-After: 10` | — |
| `urn:runnersdiary:problem:summary-in-progress` | 409 | — |
| `urn:runnersdiary:problem:summary-failed` | 502 | `reason: "invalidOutput" \| "timeout" \| "provider"` |

Not configured or kill switch: the existing 503 `feature-unavailable`.

### 3.3 Read precedence (GET)
1. template period → template;
2. fresh stored row;
3. guest + demo preset matched + all predicates hold → demo;
4. stale stored row;
5. none.

`availability` is computed independently of the summary returned: provider configured and enabled → quota check.

### 3.4 Schema delta — migration `AiSummaries`

**`AiSummaries`** (owned, `Owner` filter, FK `UserId` → `Users` CASCADE)

| Column | SQL type | Notes |
|---|---|---|
| `Id` | `uniqueidentifier` | PK nonclustered, v7 |
| `UserId` | `uniqueidentifier` | |
| `PeriodFrom`, `PeriodTo` | `date` | |
| `Compare` | `tinyint` | None 0, Previous 1, PreviousYear 2 |
| `Language` | `varchar(8)` | `en` |
| `MetricsVersion`, `FactSheetVersion`, `PromptVersion` | `smallint` | |
| `ModelDeployment` | `varchar(64)` | configured deployment (validity) |
| `ModelId` | `varchar(100)` | reported by the response (audit) |
| `Fingerprint` | `binary(32)` | SHA-256 of the canonical fact sheet |
| `ContentJson` | `nvarchar(max)` | validated draft + resolved `values`; ≤ 16 KB enforced in code |
| `InputTokens`, `OutputTokens` | `int` | |
| `CreatedAt` | `datetime2(0)` | UTC |

Keys: `UX_AiSummaries_Key (UserId, PeriodFrom, PeriodTo, Compare, Language)` unique **clustered**.

**`AiGenerations`** (system ledger: no `Owner` filter, no FK; ADR 0028 §4)

| Column | SQL type | Notes |
|---|---|---|
| `Id` | `uniqueidentifier` | PK nonclustered, v7 |
| `UserId` | `uniqueidentifier` | |
| `IsGuest` | `bit` | |
| `CreatedAt` | `datetime2(0)` | |
| `Outcome` | `tinyint` | Succeeded 0, InvalidOutput 1, ProviderError 2, Timeout 3, Cancelled 4 (implemented: + Throttled 5, §12) |
| `Attempts` | `tinyint` | |
| `InputTokens`, `OutputTokens`, `DurationMs` | `int` | |
| `PromptVersion` | `smallint` | |
| `ModelDeployment` | `varchar(64)` | |

Indexes:
- `CX_AiGenerations_User_Created (UserId, CreatedAt)` clustered: per-user quota;
- `IX_AiGenerations_Created (CreatedAt) INCLUDE (IsGuest)`: global quotas and the purge.

### 3.5 Fact sheet and prompt (ADR 0026)

The fact sheet (excerpt, demo 12-week view, values illustrative):
```json
{ "factSheetVersion": 1, "metricsVersion": 1, "language": "en",
  "facts": [
    { "key": "period.from", "value": "2026-08-17", "unit": "date", "display": "17 Aug 2026" },
    { "key": "period.isPartial", "value": true, "unit": "flag", "display": "yes" },
    { "key": "totals.distance", "value": 412300.5, "unit": "meters", "display": "412.3 km" },
    { "key": "comparison.delta.load.percent", "value": 6.4, "unit": "percent", "display": "+6.4 %" },
    { "key": "acwr.peak.date", "value": "2026-10-23", "unit": "date", "display": "23 Oct 2026" },
    { "key": "acwr.peak.value", "value": 1.79, "unit": "ratio", "display": "1.79" },
    { "key": "acwr.peak.band", "value": "high", "unit": "band", "display": "high" },
    { "key": "acwr.peak.week", "value": 10, "unit": "count", "display": "10" },
    { "key": "acwr.peak.isRaceWeek", "value": true, "unit": "flag", "display": "yes" },
    { "key": "acwr.peak.raceType", "value": "trailRace", "unit": "runType", "display": "trail race" },
    { "key": "week.1.acwrAtEnd", "value": 1.48, "unit": "ratio", "display": "1.48" },
    { "key": "week.1.acwrBand", "value": "high", "unit": "band", "display": "high" },
    { "key": "pace.slope", "value": -0.58, "unit": "secondsPerKmPerWeek", "display": "-0.58 s/km per week" },
    { "key": "dq.estimatedLoadShare", "value": 0, "unit": "share", "display": "0 %" } ] }
```

**Corrected 2026-11-04 (review M1):** the excerpt first showed the peak as 1.59 in week 9. The demo peak is **1.79 on Fri 2026-10-23, week −2 (`acwr.peak.week` 10)**, 5 days after the Sunday trail race of week −3 (`week.9`, `acwrAtEnd` 1.59). `acwr.peak.isRaceWeek` is true because the race lies within the 7 days ending on the peak date (ADR 0026 §1, amended).

`prompts/training-insights/v1.md`: front matter `version: 1`, then these sections:
1. **Role:** a supportive, factual running-diary assistant writing for the runner.
2. **Data:** only the facts in the JSON. Cite every number, date or category as `{key}`. Never write digits or number words. If something is not in the facts, do not say it.
3. **Interpretation:**
   - ACWR is a load change against the runner's recent norm, never an injury predictor;
   - a high peak in a race week is the race, not a training spike;
   - a low band in the week before a race is a taper;
   - partial periods are "so far";
   - when `acwr.asOf.unavailable` is present, explain that more history is needed;
   - mention estimated RPE when `dq.estimatedLoadShare` ≥ the threshold.
4. **Safety:** no medical, injury or diagnosis statements; no instructions to train through pain.
5. **Output:** the JSON schema, the item limits and the language.

### 3.6 UI

**Card placement:**
- desktop: the full-width row after the KPI grid and planned summary (§3.5 of the M5 plan, new position 3);
- phone: the same order, single column.

**States:**

| State | Content | Actions |
|---|---|---|
| none + available | "Get a short summary of this period" + note on what the model sees | **Generate** ("2 of 3 left" for guests) |
| generating | skeleton; live region "Generating summary…"; elapsed seconds | **Cancel** (aborts the request) |
| fresh | badge, headline (`h3`), lists, suggestion, footer (date, model, prompt v, "not medical advice") | **Regenerate** (+ remaining) |
| stale | as fresh + note "Newer data is available — this summary is from {date}" | **Regenerate** |
| demo | badge "Example summary for the demo data" | **Generate** (own summary, uses quota) |
| template | badge "Automatic note"; no model mention | none |
| quotaExhausted | last summary if any + "Daily limit reached, resets {time}" / guests: "Demo limit reached" | disabled button with `aria-describedby` |
| notConfigured | demo/template if any, else "AI summaries aren't enabled in this environment" | none |
| error (502/503/409/429) | `role="alert"` message per problem type | **Try again** (not for quota) |

- **Text rendering:** `summaryText(text, values)` splits on `{key}` and maps each value by `unit` to the existing formatters:
  - `units.ts` (km, h:mm:ss, min/km), load AU, ratio 2 dp, percent, `plainDate`;
  - `runTypes.<code>` and `insights.band.<band>` i18n keys.
- Unknown keys (impossible after validation) render "—".
- Lists are `<ul>` with visible labels; there are no tone colours without text.

**Queries:**
- `getInsightsSummary`: the same key params as the report, `staleTime` 60 s; invalidated by run create, update and delete.
- `generateInsightsSummary` (mutation, no retry): on success `setQueryData` for the matching key; `AbortController` wired to Cancel.

## 4. Implementation steps (each = one reviewable commit)

1. **ai-insights (+ qa-tester): Domain narrative.**
   - `FactSheetBuilder`, canonical JSON + fingerprint, `SummaryDraft`, `SummaryValidator`, templates selection, demo fixture predicates.
   - The `RunnersDiary.Ai.Tests` project with unit tests, eval-case builders and golden fact sheets.
   - The hand-written `12w-previous` fixture + the every-today-for-a-year test.
   - No packages, no API change.
2. **ai-insights: agent pipeline.**
   - **Spike first** (record the outcome in §12): `Microsoft.Agents.AI` 1.27.0 + `Microsoft.Extensions.AI.OpenAI` 10.11.0 + `OpenAI` 2.14.0 compile warning-free under `TreatWarningsAsErrors`, with no vulnerable transitive package. Confirm the exact APIs:
     - `ChatClientAgent` constructor/options;
     - `RunAsync<T>` with run options carrying `MaxOutputTokens` and `Reasoning`;
     - where token `Usage` is exposed on `AgentResponse`;
     - strict JSON-schema compatibility of `SummaryDraft` (all properties required, `additionalProperties: false`, nullable `suggestion`).
   - Then:
     - `prompts/training-insights/*` (manifest, `v1.md`, templates, demo fixture) copied as content;
     - `PromptCatalog` with the hash check;
     - `ChatClientFactory` (None/Foundry/Fake) with the `UseOpenTelemetry`/`UseLogging` pipeline;
     - `TrainingInsightsAgent`, `SummaryGenerator` (attempts, retry with codes, timeout, slots, in-flight guard), `AiUsageLedger`;
     - eval runner with `ScriptedChatClient`; `LiveEvalTests` (skipped unless `RD_AI_LIVE_EVAL=1`).
3. **backend-dotnet: persistence and endpoints.**
   - Entities, configurations, migration `AiSummaries`, ledger purge in cleanup.
   - The two endpoints, problem types, the `ai-summary` policy and `client-config.ai`.
   - `appsettings`; AppHost forwarding per ADR 0027 §4; `InsightsSummaryService` wiring.
   - OpenAPI + TS regenerated.
   - Api.Tests (§6) with the factory's scripted client.
4. **frontend-react: summary card.**
   - `SummaryCard` and its parts, `summaryText`/`summaryFormat`, queries, cancel, invalidation, `client-config.ai`, i18n.
   - qa-tester extends the fake API with summary fixtures.
5. **qa-tester: E2E.**
   - `insights-summary.spec.ts` + phone spec with axe;
   - the full-stack insights spec asserts the demo summary for a seeded guest;
   - Vitest gaps.
6. **(Only if §9 Q9 = yes) user + ai-insights: live check.**
   - The user creates the resources in §8.3 and sets two user-secrets.
   - Run the live eval three times and one manual generation in the AppHost; record the pass rate, tokens and cost in §12.
   - Otherwise this moves to M7.
7. **reviewer, then architect.**
   - reviewer: the ledger exception to the `Owner` filter, quota races and the cost ceiling, what reaches the model (canaries), validator coverage, logging (no content), the problem-type mapping, a11y.
   - architect: docs and deviations.
   - The main agent updates CLAUDE.md, commits "M6: AI summary" and stops for approval.

## 5. Risks

| Risk | Impact | Mitigation |
|---|---|---|
| Agent Framework / M.E.AI APIs shift between minor versions (weekly releases) | Build breaks, docs mismatch | Exact pins; the step-2 spike records the APIs used; the agent is a thin layer over `IChatClient`. |
| Heavy transitive packages from `Microsoft.Agents.AI` (tokenizers, vector data, evaluation) | Analyzer warnings, CVE pins, image size | Spike gate; security pins per the existing convention. Fallback: plain `IChatClient` without the agent, recorded as a deviation and an open question for the user, since the brief names the framework. |
| The model fails the placeholder contract (writes digits, cites wrong keys) | 502s, quota burnt | Strict JSON schema + one retry with rule codes; the live eval measures the pass rate before go-live; prompt v2 if below 90 % on the first attempt. Failed attempts count (Q7), bounded by quotas. |
| Over-strict validator (number words, deny-list false positives such as "physio") | Good answers rejected | The rule list is versioned in the manifest; eval cases cover legitimate phrasings; deny-list hits are logged as codes. |
| Narrative misreads (taper called a risk, race peak called a spike) | Misleading advice | Facts annotate race weeks; `raceWeekPeak` is a hard rule; the taper case is a live-eval assertion; the UI disclaimer. |
| Current-period summaries go stale daily | Users see "newer data" often | Wording; stale summaries stay visible; regeneration is cheap and explicit. |
| Guest farming (many IPs) | Spend | Durable global caps (60 guest / 200 total per day ≈ $2.5/day ceiling) + per-account caps + guest-creation limits. |
| Ledger without the `Owner` filter | Accidental cross-user read | One service, explicit filters, reviewer checklist, isolation tests on quota answers. |
| Fixture predicates drift from the demo data | The demo summary vanishes or lies | Every-today-for-a-year test + startup validation; the predicates make lying impossible (worst case: hidden). |
| Model retirement / new-customer block (12 months) | M7 cannot deploy the chosen model | Deployment name is config; re-check in M7; candidates listed in ADR 0027. |
| Long synchronous request (≤ ~65 s) behind the proxies | Proxy timeout | 30 s per attempt; Vite and ACA ingress allow ≥ 240 s ⚠ (verify the ACA web tier in M7); Cancel button. |
| Personal data sent to a model provider | Privacy | No free text, no HR (Q6), no ids; Data Zone EU (Q8); the card's "what the model sees" note. |

## 6. Test plan outline (qa-tester)

**Ai.Tests (no DB, no network):**
- **FactSheet:**
  - verbatim values;
  - annotations (peak week index, race-week flag, band of `acwrAtEnd`);
  - weeks vs months at 16/17 weeks;
  - a future period has no `acwr.asOf.*`;
  - `comparison: null` → no comparison keys;
  - canonical JSON stable across runs and cultures (`de-DE`, `pl-PL`);
  - a fingerprint change for a single run change.
- **Validator:** one failing and one passing fixture per rule code. Specific cases:
  - "half marathon" and "one week" are allowed; "two weeks", "12 km" and "40%" are rejected;
  - a `{unknown}` key and unbalanced `{`;
  - a watch-out about the race-week peak without the race cited;
  - `estimatedLoad` at 0.24 / 0.25;
  - Markdown and links.
- **Pipeline (`ScriptedChatClient`):**
  - good on the first attempt;
  - bad then good (retry message lists codes and no echoed text);
  - bad twice → failure;
  - timeout;
  - cancellation;
  - a template case → zero client calls;
  - the request contains the v1 instructions, the JSON schema, `MaxOutputTokens` 1 500 and reasoning low, and no `temperature`.
- **Prompts:** manifest hash = file hash; startup fails on a mismatch.
- **Demo:** for every today in a year, the fixture's predicates hold on the pristine demo and the fixture validates.
- **Evals (CI mode):** all 8 cases, against golden fact sheets and good drafts, with the case's required and forbidden keys asserted. Live mode skipped.

**Api.Tests (Testcontainers):**
- the 400 matrix (incl. `lang=pl` and `regenerate: "yes"`);
- GET states: none, fresh, stale, demo, template;
- POST paths per §3.1 order, incl. `regenerate: false` reuse with zero client calls;
- quotas: user, guest, global and guest-global with tiny limits; failed attempts counted; `resetsAt` and `Retry-After`;
- 409 concurrent, 503 busy (slots 0), 503 provider `None`, kill switch, 502 invalid/timeout;
- stale after a run edit, a prompt version change (test manifest) and a deployment change;
- the isolation matrix and the canary free-text check;
- guest cleanup removes `AiSummaries`; the ledger purge only removes rows older than 30 days;
- inventory: both routes Auth; `client-config.ai`.

**Vitest + RTL:**
- `summaryText` (placeholders, every unit, "—" fallback);
- each card state; the remaining count and the disabled reason;
- Cancel aborts; invalidation after `createRun`;
- i18n keys exist;
- `test:tz` for the `createdAt`/`resetsAt` display.

**Playwright (fake API):**
- `insights-summary.spec`:
  - the default view shows the demo summary;
  - switch to a month → Generate → generated card → Regenerate;
  - quota-exhausted, not-configured, error and stale fixtures;
  - keyboard-only flow;
  - axe on each state.
- `insights-summary.phone.spec` (360×640, LA zone): no horizontal scroll, buttons ≥ 44 px, axe.

**Full-stack (main only):** guest → `/insights` → the "Example summary" card is visible, and its distance value equals the distance KPI. No AI is configured in CI, so this also proves `notConfigured` for Generate.

**Manual (only with Q9):**
- live eval three times;
- one generation in the AppHost against the user's deployment;
- VoiceOver over the card;
- cost check against the ledger tokens.

## 7. Dependencies

### NuGet (central pins in `Directory.Packages.props`, new group "AI")

| Package | Version | Project | Why |
|---|---|---|---|
| `Microsoft.Agents.AI` | 1.27.0 | Infrastructure | Brief-mandated Agent Framework (`ChatClientAgent`, `RunAsync<T>` structured output). |
| `Microsoft.Extensions.AI` | 10.11.0 | Infrastructure | `ChatClientBuilder` middleware (`UseOpenTelemetry`, `UseLogging`); aligned with the OpenAI adapter. |
| `Microsoft.Extensions.AI.OpenAI` | 10.11.0 | Infrastructure | `AsIChatClient()` over the OpenAI SDK client. |
| `OpenAI` | 2.14.0 | Infrastructure | Official SDK for the Foundry v1 endpoint with `BearerTokenPolicy` (keyless). |
| `Azure.Identity` | 1.21.0 | Infrastructure | `DefaultAzureCredential` (local) and `ManagedIdentityCredential` (Azure). Already transitive through Aspire Blobs; a direct pin keeps one version. |

Tests: no new packages; `ScriptedChatClient` is about 60 lines over `IChatClient`.

**Rejected:**
- `Azure.AI.OpenAI` (prerelease);
- `Microsoft.Agents.AI.Foundry` (preview);
- `Microsoft.Agents.AI.OpenAI` (convenience only);
- `Aspire.Hosting.Foundry` (preview, provisions in run mode);
- `Aspire.OpenAI` (no managed identity);
- `Microsoft.ML.Tokenizers.Data.*` (a character cap suffices).

### npm
**None.**

## 8. What the user must do

1. **Nothing in the cloud for M6 acceptance.** CI and local development use the fake client; guests see the example summary with no AI configured.
2. Answer §9, above all Q1 (fact sheet vs tools), Q2 (model), Q3 (explicit generation) and Q9 (live check now or in M7).
3. **Only if Q9 = yes:** you create the following yourself, or explicitly confirm an agent may, after reviewing the exact commands given at step 6:
   - one **Microsoft Foundry resource** (kind `AIServices`, local auth disabled) in an EU region that offers gpt-5.4-mini as Data Zone Standard (e.g. Sweden Central);
   - one **gpt-5.4-mini** deployment (Data Zone Standard, a small capacity such as 10k TPM);
   - the role **Cognitive Services User** for your own account on that resource.

   Then run `dotnet user-secrets set "Ai:Endpoint" "https://<resource>.openai.azure.com/"` and `… "Ai:Deployment" "<deployment name>"` in the AppHost, and `az login`. **No keys.**

   **Cost:** no fixed fee for a pay-per-token deployment (⚠ to be re-checked in M7). About $0.012 per summary, so the live eval (8 cases × 3 runs, ≤ 2 attempts) is ≈ $0.30–0.60. Delete the deployment afterwards, or keep it for M7.

## 9. Open questions (recommended defaults in bold)

1. **Agent input:** **one pre-serialized fact sheet per call, no function tools in v1** (cheaper, deterministic; the tool wrapper is an upgrade path), or function tools as the brief words it.
2. **Model:** **gpt-5.4-mini** (GA until 2027-09-21, $0.75/$4.50 per 1M global), or gpt-5.4-nano (≈ 4× cheaper, weaker on strict contracts), or the newer gpt-6-luna (cheapest; lifecycle not yet published). Final check by the live eval in M7.
3. **When to generate:** **only on an explicit Generate/Regenerate click**; stale summaries stay visible with a note. Alternatively, auto-generate on view when none or stale exists, which spends tokens on views.
4. **Quotas:** **users 10 per rolling 24 h, guests 3 per account, global 200 and guest-global 60 per 24 h**, plus a burst limit of 6 (users) / 3 (guests) per 10 min.
5. **Guests generating at all:** **yes, up to 3 when AI is configured, plus the free example summary on the default view**, or the example summary only (zero guest spend).
6. **Heart rate to the model:** **excluded** (health data minimization), or include the weekly easy-run average HR.
7. **Failed or timed-out attempts count toward quota:** **yes** (tokens were spent), or only successful generations.
8. **Deployment type:** **Data Zone Standard EU** (+10 %, processing stays in the EU), or Global Standard.
9. **Live check:** **in M7 with the deployment**, the same precedent as Azure Maps in M4, or now in step 6 (you create the Foundry resource; ≈ $0.30–0.60).
10. **Demo fixtures:** **one hand-written fixture for the default view, labelled "Example summary"**, or more presets (this month, last week). More presets need more predicates and fail more often across "today".

**Answers (user, 2026-11-04):** all recommended defaults. In order:
- Q1: one fact sheet per call, no tools.
- Q2: gpt-5.4-mini.
- Q3: generate only on an explicit click.
- Q4: quotas as proposed: users 10 per 24 h, guests 3 per account, global 200 and guest-global 60 per 24 h; burst limit 6 per 10 min for users and 3 for guests.
- Q5: guests may generate 3 times, plus the example summary.
- Q6: heart rate excluded.
- Q7: failed attempts count.
- Q8: Data Zone Standard EU.
- Q9: live check in M7.
- Q10: one demo fixture for the default view.

## 10. Assumptions

1. A narrative with values as placeholders (rendered "412.3 km") is acceptable prose. The user asked for grounding, not stylistic freedom.
2. English only in M6; `lang` exists in the contract and cache key, and Polish needs a prompt line, a templates file and fixtures.
3. Generation latency of 5–20 s is acceptable synchronously with a Cancel button; there is no streaming of partial text, because structured output is validated as a whole.
4. Template thresholds: empty means 0 completed sessions, fewSessions means < 3. They are constants in Domain, versioned with `factSheetVersion`.
5. The stored `ContentJson` holds no user free text (the model never saw any), so storing it raises no new personal-data concern beyond the metrics.
6. Per-replica in-flight guards and slots are acceptable with 1–2 API replicas (ADR 0011). Quotas themselves are durable.
7. The "not medical advice" line and the "what the model sees" note are fixed UI strings, not model output.
8. `Microsoft.Agents.AI` 1.27.0 and its stated dependency floors are as listed on NuGet on 2026-11-04. The implementer re-checks for a newer patch at install and records it.

## 11. Docs checked (2026-11-04)

- Agent Framework structured outputs (updated 2026-11-03): https://learn.microsoft.com/agent-framework/agents/structured-outputs
- Agent Framework Azure OpenAI provider (updated 2026-11-02): https://learn.microsoft.com/agent-framework/integrations/by-component/model-providers/azure-openai
- NuGet `Microsoft.Agents.AI` 1.27.0 (2026-10-30): https://www.nuget.org/packages/Microsoft.Agents.AI/ · profile (stable vs preview): https://www.nuget.org/profiles/MicrosoftAgentFramework/ · `Microsoft.Agents.AI.OpenAI`: https://www.nuget.org/packages/Microsoft.Agents.AI.OpenAI/
- NuGet `Microsoft.Extensions.AI.OpenAI` 10.11.0: https://www.nuget.org/packages/Microsoft.Extensions.AI.OpenAI/ · `OpenAI` 2.14.0: https://www.nuget.org/packages/OpenAI/ · `Azure.Identity` 1.21.0: https://www.nuget.org/packages/Azure.Identity/ · `Azure.AI.OpenAI`: https://www.nuget.org/packages/Azure.AI.OpenAI/
- `ChatOptions` (10.10.1: `MaxOutputTokens`, `Reasoning`, `ResponseFormat`): https://learn.microsoft.com/dotnet/api/microsoft.extensions.ai.chatoptions · M.E.AI overview (2026-09-14): https://learn.microsoft.com/dotnet/ai/microsoft-extensions-ai
- Azure OpenAI v1 endpoint + keyless C# (2026-07-22): https://learn.microsoft.com/azure/foundry/openai/supported-languages
- Keyless auth / Cognitive Services User (2026-08-31): https://learn.microsoft.com/azure/foundry/foundry-models/how-to/configure-entra-id
- Reasoning models parameters (2026-10-30): https://learn.microsoft.com/azure/foundry/openai/how-to/reasoning
- Model lifecycle (2026-07-24): https://learn.microsoft.com/azure/foundry/openai/concepts/model-retirements · retirement schedule (2026-09-21): https://learn.microsoft.com/azure/foundry/openai/concepts/model-retirement-schedule
- Aspire Foundry hosting (13.6): https://aspire.dev/integrations/cloud/azure/azure-ai-foundry/azure-ai-foundry-host/ · connect: https://aspire.dev/integrations/cloud/azure/azure-ai-foundry/azure-ai-foundry-connect/
- Azure retail prices API (gpt-5.4-mini, gpt-5.4-nano, luna meters): https://prices.azure.com/api/retail/prices

**Verified against Microsoft Learn (MCP server, 2026-11-04):** see ADR 0027 §Verification.
- `ChatClientAgent(chatClient, instructions:)`, `RunAsync<T>`, `ChatResponseFormat.ForJsonSchema<T>()`, and `AgentResponse.Usage` (`UsageDetails?`).
- Nullable fields via a union with `null` / `anyOf` in strict schemas. `maxLength`, `minItems` and `maxItems` are unsupported, so the validator enforces lengths (ADR 0026 §2).
- gpt-5.4-mini Data Zone Standard EU, including swedencentral (not northeurope).
- Cognitive Services User role, the `https://ai.azure.com/.default` scope, and the reasoning-model parameters. `reasoning_effort: none` is not documented; `low` is used.
- The step-2 spike still compiles against 1.27.0 to confirm the exact signatures.

**Not verified:**
- gpt-6-luna lifecycle (absent from the retirement schedule). It is in the EU Data Zone table except northeurope;
- whether a pay-per-token Foundry deployment has any fixed monthly cost;
- the Azure pricing web page itself, whose prices render client-side (retail API figures used instead);
- the ACA ingress request timeout (M7).

## 12. Implementation notes & deviations (2026-11-04)

The decision-level changes are recorded as "Amended 2026-11-04" sections in ADRs [0026](../adr/0026-training-insights-agent-and-grounding.md), [0027](../adr/0027-ai-model-provider-foundry-and-local-dev.md) and [0028](../adr/0028-summary-cache-quotas-and-demo-summaries.md), and in a correction note in [0024](../adr/0024-training-metrics-definitions-v1.md). The contract is in [`api-contract.md`](../api-contract.md#added-in-m6-ai-summary); the tables are in [`data-model.md`](../data-model.md#aisummaries-m6).

**Verified (2026-11-04):** build 0 warnings, no OpenAPI/TS drift, no vulnerable packages (all 8 projects); .NET 2298 (2292 passed, 6 live evals skipped without `RD_AI_LIVE_EVAL=1`; `CI=true`, Testcontainers); Vitest 938/938; `test:tz` 765 per zone (twice; one earlier run under load failed 3 unrelated `RunForm`/`RunPanels` timing tests); Playwright 108/108; bundle initial 178.8 kB (183,054 B) / limit 201.5 kB; local replica of the `e2e-fullstack` job 9/9 (journey, 3 tracks, 3 insights, 2 insights-summary with Provider=None); lint, typecheck, Prettier clean. No live model call was made.

### 12.1 Step-2 spike outcome

- **Pins:** the 5 planned pins are the latest stable versions: `Microsoft.Agents.AI` 1.27.0, `Microsoft.Extensions.AI` 10.11.0, `Microsoft.Extensions.AI.OpenAI` 10.11.0, `OpenAI` 2.14.0, `Azure.Identity` 1.21.0. The build has 0 warnings under `TreatWarningsAsErrors`, no vulnerable package is reported, and no security pin was needed.
- **Notable transitive packages:** `Microsoft.Extensions.AI.Evaluation`, `Microsoft.Extensions.VectorData.Abstractions` and `Microsoft.Extensions.Compliance.Abstractions` 10.10.0; `Microsoft.ML.Tokenizers` 2.0.0; `System.ClientModel` 1.15.0; `Azure.Core` 1.62.0.
- **APIs used** (differences from §4 step 2 in bold):
  - `new ChatClientAgent(chatClient, new ChatClientAgentOptions { …, UseProvidedChatClientAsIs = true }, loggerFactory)`. The client already carries the OpenTelemetry/logging pipeline, so the agent must not wrap it again.
  - **`RunAsync(messages, null, new ChatClientAgentRunOptions(chatOptions), ct)` rather than `RunAsync<T>`.** The raw text goes to `SummaryValidator`, so a parse failure becomes rule `schema` and takes the normal retry path instead of an exception.
  - `ChatOptions.Reasoning = new ReasoningOptions { Effort = Low }`, `MaxOutputTokens = 1500`, the response format from the `SummaryDraft` schema with `$ref` inlined, and `AdditionalProperties["strict"] = true` (ADR 0026 amendment).
  - Token usage: `AgentResponse.Usage`.

### 12.2 Domain and pipeline (ai-insights)

- **Demo ACWR peak (review M1):** 1.79 on Fri 2026-10-23 (week −2, `acwr.peak.week` 10), not 1.59 in week 9. The excerpt in §3.5 and ADRs 0024/0026/0028 are corrected. `acwr.peak.isRaceWeek` is defined as "a completed race inside the period within the 7 days ending on the peak date". A peak in the first 6 days driven by a race before `from` gets `false` (accepted).
- **Validator:** stricter than the plan's list; full rules in ADR 0026's amendment.
  - `raceWeekPeak` also covers the headline, the suggestion, and `week.N.acwrAtEnd`/`acwrBand` in race weeks;
  - `literalNumber` adds ordinals, "twice"/"thrice", plural number words, other Unicode number characters, and upper-case Roman numerals of ≥ 2 letters ("MIX"/"XC" flagged, accepted);
  - `forbiddenTopic` adds `pain*`, `hurt*`, `strain*`, `tendon*`, `tendin*`, `fractur*`;
  - `markup` adds `://` and `www.`.
- **Fact sheet:** unavailable ACWR as flags; no `period.asOf` for a wholly future period; `week.N.future`; size trimming (no comparison totals, no weekly run counts, `complete` only when false, top 8 types, 6 most recent races). The cap is 24k characters; the demo default view is 16.2k.
- **Templates:** variants with conditions (`fewSessions`: one vs few sessions, "so far" only when partial; `empty`: "yet" only when partial).
- **Prompt:** v1 hash `dc1ed4aacc2d7540c2eb5015e7ef1bce0d5bbad56ae17b0350e78ad5370b9afc`, computed over LF-normalized text.
- **Wire test:** `FoundryWireTests` checks the real HTTP request (ADR 0027 amendment).
- `InsightsSummaryService` was written by ai-insights (planned under ai-insights in §2, wired by backend-dotnet).

### 12.3 Backend (backend-dotnet)

| # | Deviation | Why |
|---|---|---|
| 1 | The ledger purge runs from `GuestCleanup` through `EfAiGenerationStore` (30-day `AiUsageLedger.Retention`), once per cleanup run after the guest batch. A failure is logged and does not block guest removal. | The only code that touches `AiGenerations` stays the ledger's own store. |
| 2 | `AddInfrastructure` calls `AddTrainingInsights()` without the `runStartupChecks` argument, so the default (`true`) registers the `AiStartupCheck` hosted service in every host. The flag exists only for callers that build the services without the startup checks. | One registration path for the API and the tests. |
| 3 | Api.Tests use `Ai:Provider = Foundry` with a dummy endpoint and a DI-replaced scripted `IChatClient`, not `Provider = Fake` as §2 said. | Exercises the production configuration path (deployment in the validity tuple, availability) without network. |
| 4 | Rate policy `ai-summary` limits are raised in `appsettings.Development.json` (users 60, guests 30 per 10 min). | Local UI work; production defaults unchanged (6 / 3). |
| 5 | `AiSummaries.CreatedAt` and ledger `CreatedAt` are truncated to whole seconds before saving. | `datetime2(0)`: the POST response equals later reads. |
| 6 | `GenerateSummaryRequest.From`/`To` are nullable and validated by hand ("Required."). | A missing date gives a field error rather than a generic body error. |
| 7 | The 24 h quota window excludes its start boundary (`CreatedAt > now − 24 h`). | A row stops counting exactly 24 h after it was written; `resetsAt` matches. |
| 8 | Freshness ignores `ModelDeployment` when `Ai:Provider = None`. | Switching AI off does not turn every stored summary stale. |
| 9 | Demo and template summaries are returned with `state: fresh`. | They are computed from current data on every read. |
| 10 | Global and guest-global budgets count **attempts**; new outcome `Throttled = 5`; provider 429 → 503 `ai-busy` with the provider's Retry-After; credential failure → 503 `feature-unavailable` without a ledger row; the kill switch is read via `IOptionsMonitor`. | Review M2, M4, L6, L8 (ADRs 0027/0028 amendments). |
| 11 | `ModelId` truncated to 100 characters. | Review L7. |
| 12 | `AddMeter`/`AddSource("RunnersDiary.Ai")` in ServiceDefaults. | AI telemetry shows in the Aspire dashboard and, in M7, in App Insights. |
| 13 | Prompts are copied as content from the **Infrastructure** csproj (not the Api csproj, as §2 listed). | The files sit next to the code that loads them; projects that reference Infrastructure get them too. |

### 12.4 Frontend (frontend-react)

| # | Deviation | Why |
|---|---|---|
| 1 | An unknown placeholder renders "—" (as planned). | Defensive; impossible after validation. |
| 2 | The card is also shown in the empty-state layout of `/insights`, where it shows the template notes. | Empty periods still get the automatic note. |
| 3 | Elevation is formatted by **key name** (`…elevationGain…`), because the unit `meters` covers both distance and elevation. | No extra unit on the wire. |
| 4 | `comparison.delta.*.absolute` values are shown signed through the existing delta helpers. | Same wording as the KPI grid. |
| 5 | The card does not read `client-config.ai`. It relies on `generation.availability` from GET. | One source of truth per period, including quota. |
| 6 | A period change aborts a running generation. Cancel or abort refetches GET. | No stale result is written into another period's cache. |
| 7 | The polite live region is outside the `aria-busy` container. | Screen readers would otherwise suppress the announcement. |
| 8 | Error message wording was adjusted per problem type (i18n keys). | Clarity; strings remain i18n keys. |
| 9 | The SPA requests the summary after the report, not in parallel (review info). | Negligible latency; not changed. |

### 12.5 Review findings and resolution

| Finding | Resolution |
|---|---|
| **M1** Demo peak misdocumented (1.59, race week) | Docs corrected (1.79, week −2, race-driven); `isRaceWeek` defined by the acute window; edge case accepted. |
| **M2** Global budgets did not bound cost (rows, not calls) | Global and guest-global count attempts; ceiling recomputed (ADR 0028 amendment). |
| **M4** Provider 429 handling and TPM sizing | 503 `ai-busy` with the provider's Retry-After, no user quota; `Throttled` row only after a billed attempt; M7: ≥ 30k TPM (ADR 0027 amendment). |
| **L1** `raceWeekPeak` checked only watch-outs and the peak | Now covers the headline, the suggestion and race weeks' `acwrAtEnd`/`acwrBand`. |
| **L2** Ordinals and Roman numerals passed `literalNumber` | Added (ADR 0026 amendment). |
| **L6** Credential failure was a 502 with a ledger row | 503 `feature-unavailable`, no ledger row. |
| **L7** Long provider model id could fail the insert | Truncated to 100. |
| **L8** Kill switch needed a restart | `IOptionsMonitor`, applied per request. |
| **M3** The real provider request was never checked in CI | `FoundryWireTests` (capturing transport, fake credential, no network) assert path, Bearer + `ai.azure.com` scope, `max_completion_tokens`, `reasoning_effort`, `response_format.json_schema.strict: true`, no `temperature`/`max_tokens`/top-level `strict`. No code change was needed. |
| **M5** Live region inside an `aria-busy` container | `SummaryStatus` moved before the `aria-busy` box; a test asserts it has no `aria-busy="true"` ancestor while generating. |
| **L3** Unknown placeholders rendered as nothing (plan §3.6: "—") | Render "—"; braces still stripped, prototype keys still unresolved. |
| **L4** Card hidden for empty periods, so `empty`/`plannedOnly`/future templates never showed | The card is also rendered in the empty-state layout and shows the template note (no Generate button). |
| **L5** `GuestCleanup` deleted ledger rows directly | Purge goes through `EfAiGenerationStore.PurgeOlderThanAsync` over the cleanup's context; the DbContext comment is corrected. |
| **L9** Cancel did not refresh the quota display | Every POST error, including an abort, invalidates the summary GET. |
| Nits | `fewSessions`/`empty` template variants (singular/plural, "so far"/"yet" only for partial periods); `failedProvider` and `inProgress` messages reworded; POST OpenAPI metadata lists 409/429/502/503. |

Info items (no change): 3 ledger queries per GET; SPA requests the summary after the report.

### 12.6 Still open

- **Live eval and the real Foundry path (M7, §9 Q9):** first-attempt pass rate, real tokens and cost, the actual `ModelId`, and the real 429/Retry-After headers.
- **VoiceOver / real-device accessibility pass** over the card (only axe and keyboard so far).
- **Real Entra user quota** (`rolling24h`, `resetsAt`, `Retry-After`) is tested with minted tokens only, until the Entra tenant exists.
- **`acwr.peak.isRaceWeek` rename** (e.g. `raceInAcuteWindow`): a candidate for `factSheetVersion` 2.
- **Per-replica quota overshoot** (check-then-insert, in-flight guard and slots per replica): accepted; included in the 203-call ceiling.
- The M7 budget alert and the deployment's TPM are sized from ADRs 0027/0028 amendments.
