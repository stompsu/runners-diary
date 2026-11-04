# 0026 – Training Insights agent: fact-sheet input, placeholder-only numbers, server-side validation

**Status:** Accepted (M6 plan approved 2026-11-04; verified against Microsoft Learn) · Amended 2026-11-04 (implementation, see the end) · Amended 2026-11-04 (fact sheet as the data-residency safeguard, see the end)
**Date:** 2026-11-04

## Context

Brief §1.1.8 asks for an **AI summary** of a selected period (week, month, training block before a race): what went well, risks (e.g. load spikes) and suggestions. Numbers are always computed deterministically in code; the LLM only interprets and narrates them and must not invent data. Brief §2 asks for one "Training Insights" agent built with Microsoft Agent Framework (.NET 1.x), with read-only function tools over pre-computed metrics, no raw DB access and no write tools.

Already decided:
- ADR 0024: metric definitions v1 (`metricsVersion` 1), with notes for M6. Describe ACWR as a load-change indicator, never as an injury predictor. Mention estimated RPE when its share is large. The demo's default-view ACWR peak is driven by the trail race and must be narrated as race-driven, not as a training spike. (**Corrected 2026-11-04, review M1:** the peak is **1.79 on Fri 2026-10-23, week −2** (`acwr.peak.week` 10), 5 days after the Sunday trail race of week −3 (`week.9`, whose `acwrAtEnd` is 1.59); the earlier text said "1.59, the trail-race week". See the ADR 0024 amendment.)
- ADR 0025 §4: the agent reads `IInsightsReportService` in-process under the `Owner` filter and never recomputes. A payload may project the report but must not add numbers. For a wholly future period, `period.asOf` is before `period.from` and `load.atAsOf` lies outside the period.
- ADR 0010: every read is scoped to the caller.

A model that writes numbers in free text can hallucinate or mis-round them. A post-check that pulls numbers out of prose and matches them back to the metrics is fragile: units, rounding, "12 weeks" vs "84 days", locales. Notes, titles, race names, gear and weather are free text the user typed. Sending them to the model leaks personal data and opens a prompt-injection path.

## Decision

### 1. Input: one pre-serialized fact sheet, no tool calls in v1

- The endpoint computes the report with `IInsightsReportService` (request scope, `Owner` filter). It then projects the report into a **fact sheet** with the pure `FactSheetBuilder` in `RunnersDiary.Domain/Insights/Narrative/`, and sends that as the only data in a **single** model call.
- The model gets no function tools in v1:
  - **Cost:** tool calling adds a round trip that re-sends the instructions (about 25–40 % more input tokens, and twice the latency).
  - **Determinism:** with one fixed payload, the model sees exactly what is hashed, cached and evaluated.
  - **Grounding:** it is identical either way, because tools would return the same projection.
- The brief's "function tools that read pre-computed metrics" is kept as an upgrade path, not dropped. The fact sheet is the tool payload. A read-only, argument-less `get_training_facts` tool can wrap the same builder later. The period is bound server-side, so no tool ever accepts a user id or a period from the model. Plan §9 Q1 asks the user to confirm this.
- **What the fact sheet contains** (canonical JSON, `factSheetVersion` 1), a flat list of `{ key, value, unit, display }`:
  - `display` is the English metric formatting, so the model understands magnitudes.
  - Keys use the grammar `segment(.segment)*` with segments `[a-z][A-Za-z0-9]*` or a 1-based index, e.g. `totals.distance`, `comparison.delta.load.percent`, `acwr.peak.value`, `week.9.load`, `race.2.resultTime`, `dq.estimatedLoadShare`.
  - Period facts: from, to, asOf, isPartial, days, elapsedDays, comparison window.
  - Totals and deltas.
  - Planned (upcoming/missed).
  - ACWR at `asOf` and at the peak: value, band, acute, chronic, or `unavailable` with its reason.
  - Band-day counts.
  - **Buckets:** weekly buckets when the period has ≤ 16 weeks (load, distance, duration, acwrAtEnd + its band, monotony, `complete`). Otherwise monthly buckets.
  - Type distribution (codes).
  - Easy-pace trend: slope, first/last point, point count.
  - Races: date, status, type code, distance category, result/target time, pace, PB flag, rating stars.
  - Data quality.
  - The thresholds.
- **Annotations allowed, arithmetic forbidden.** The builder may add:
  - list lengths (e.g. `races.completedCount`);
  - positional and categorical joins: `acwr.peak.week` (the index of the bucket containing the peak), `acwr.peak.isRaceWeek` and `acwr.peak.raceType`, `week.N.raceType`;
    - **Defined precisely (amended 2026-11-04):** `acwr.peak.isRaceWeek` is true when a **completed race inside the period lies within the 7 days ending on the peak date** (`peak.date − 6 … peak.date`, i.e. the peak's acute window), and `acwr.peak.raceType` is the type of the latest such race. `acwr.peak.week` is the bucket of the **peak date**, so it may differ from the race's own `week.N` (demo: peak in `week.10`, race in `week.9`). `week.N.raceType` is the earliest completed race inside that ISO week.
    - **Edge case (accepted):** a peak in the first 6 days of a period that is driven by a race **before `from`** gets `isRaceWeek = false`, because the report's race list covers the period only. The validator then does not demand a race citation; the prompt's general ACWR wording still applies.
  - the band of a bucket's `acwrAtEnd`, using the Domain's existing band rule (ADR 0024 §3).

  It never computes a new number from metric values: no sums, ratios, averages or differences. Everything else is copied verbatim from the Domain `InsightsReport`, with its rounding.
- **Excluded:** notes, titles, race names, bib numbers, results URLs, rating comments, gear, weather, location and its name, heart rate (Q6), display name, user id, time zone. **The model therefore never sees user free text**, which removes the injection path rather than mitigating it.
- **Wholly future period** (`asOf < from`): `acwr.asOf.*` is omitted (ADR 0025 amendment). Such a period, like an empty one, never reaches the model (§4).
- **Size cap:** `Ai:MaxFactSheetChars` (default 24 000 characters, about 6k tokens). A 366-day period falls back to monthly buckets, so it stays under the cap. The cap is a guard; exceeding it is a bug (logged, 502).

### 2. Output: structured JSON, numbers only as placeholders

- The model returns a JSON object that conforms to a strict JSON schema (structured outputs):
  ```json
  { "headline": "string",
    "wentWell":  [ { "text": "string" } ],
    "watchOuts": [ { "text": "string" } ],
    "suggestion": { "text": "string" } | null,
    "dataNotes": [ { "text": "string" } ] }
  ```
  These are the brief's "went well / risks / suggestions"; `dataNotes` holds data-quality caveats.
- Strict-schema rules (Microsoft Learn, structured outputs, verified 2026-11-04):
  - every property is `required` and every object has `additionalProperties: false`;
  - the nullable `suggestion` is a union with `null` (`anyOf`, allowed below the root);
  - at most 5 nesting levels and 100 properties.
  
  `maxLength`, `minItems`, `maxItems`, `pattern` and `format` are **not supported** in strict mode. Text length and item counts are therefore enforced by `SummaryValidator` (rule `schema`), not by the schema.
- **Numbers, dates and categorical values appear only as placeholders `{key}`** that name fact-sheet keys, e.g. `"Your load ratio peaked at {acwr.peak.value} on {acwr.peak.date}, after your {acwr.peak.raceType}."` (example corrected 2026-11-04: the original cited `week.9.*`, which is not the demo peak's bucket).
- The server resolves every placeholder from the fact sheet and returns `values: { key: { number | text, unit } }` with the summary. The **client formats them** with the existing unit helpers (SI in storage, formatting in the UI only). A narrated number is therefore always the stored metric, rounded and formatted exactly like the dashboard. This is grounding by construction, not by after-the-fact matching.
- Language: the requested language (`en` now). Placeholders are language-neutral, so Polish later needs only a prompt line, templates and demo fixtures (§4, ADR 0028).

### 3. Server-side validation (pure, `SummaryValidator`, Domain)

The validator runs on every model output before anything is stored or returned. Each rule has a stable code:

| Code | Rule |
|---|---|
| `schema` | Parses; `headline` 1–160 chars; each item 1–280 chars; `wentWell` ≤ 3, `watchOuts` ≤ 3, `dataNotes` ≤ 2; 1–9 items in total. |
| `unknownKey` | Every `{…}` is a key that exists in this fact sheet. Unbalanced braces fail. |
| `literalNumber` | Outside placeholders there are no digits (`\p{Nd}`), no `%`, `‰` or `°`, and no English cardinal number words from "two" upward, including "dozen", "hundred" and "thousand". "One" and "half" are allowed ("half marathon"). |
| `missingCitation` | Every `wentWell`, `watchOuts` and `dataNotes` item cites at least one key. `headline` and `suggestion` may cite none. |
| `forbiddenTopic` | Versioned deny-list (prompt manifest): `injur*`, `overtrain*`, `diagnos*`, `medical`, `illness`, `disease`, `cardiac`, `risk of`, `doctor`, `physio*`. The UI shows a fixed "not medical advice" line instead. |
| `raceWeekPeak` | When `acwr.peak.isRaceWeek` is true, a `watchOuts` item that cites `acwr.peak.*` must also cite `acwr.peak.raceType` or a `race.N.*` key. This enforces ADR 0024's note. |
| `estimatedLoad` | When `dq.estimatedLoadShare` ≥ the manifest threshold (default 0.25), at least one item cites `dq.estimatedLoadShare` or `dq.sessionsWithEstimatedRpe`. |
| `markup` | No URLs, HTML or Markdown syntax (`<`, `](`, `**`, `#` at line start), and no control characters. |

The `literalNumber`, `forbiddenTopic`, `raceWeekPeak` and `markup` rows were tightened during implementation: see *Amended 2026-11-04* at the end.

- On failure the pipeline makes **one retry**. It appends a message that lists only the violated rule codes and the offending keys, never echoing the model's text.
- A second failure answers **502 `summary-failed`** (`reason: invalidOutput`). Nothing is stored.
- The SPA renders the text as plain text: React text nodes with the placeholders replaced, never `dangerouslySetInnerHTML`.

### 4. Deterministic templates instead of the model

The model is **not called** in these cases. A server-side template (per language, `prompts/training-insights/templates/<lang>.json`, the same placeholder format) is returned instead, at no cost and without using quota:
- **empty:** no completed session in `[from, asOf]`;
- **plannedOnly:** a wholly future period (`elapsedDays = 0`);
- **fewSessions:** fewer than 3 completed sessions.

### 5. Agent construction

- One `ChatClientAgent` named `TrainingInsights` (Microsoft Agent Framework 1.x) over an `IChatClient` (Microsoft.Extensions.AI). The provider is in ADR 0027.
- **Instructions:** the versioned prompt `prompts/training-insights/v<N>.md`. **Response format:** a JSON schema from the `SummaryDraft` type. The run options set the token cap and reasoning effort.
- The user message is the fact sheet in a fenced JSON block plus the target language. There is no conversation thread or memory: each generation is stateless.

### 6. Versioned prompts

- `prompts/training-insights/manifest.json` lists `{ current, versions: { "1": { file: "v1.md", sha256, estimatedLoadThreshold, denyList } } }`. The files are copied to the API output as content (like `seed/demo/`) and verified at startup: a missing file or a hash mismatch fails startup.
- A released `vN.md` is **immutable**. A test fails if its SHA-256 differs from the manifest, so a wording change needs a new version file.
- `promptVersion` is stored with every summary and is part of its validity check (ADR 0028).

### 7. Evaluation

`tests/RunnersDiary.Ai.Tests` holds eval cases built with `InsightsReportBuilder` from small fact lists, so they always follow metrics v1:
- load spike; taper before a race; empty month; insufficient history; estimated-RPE share; partial period; race-week peak (demo); wholly future period.

**CI mode (default, no model):**
- golden fact sheets per case;
- a scripted fake `IChatClient` that returns one committed "good" draft per case plus adversarial drafts (each validator rule, malformed JSON);
- assertions on: validator verdicts, retry behaviour, template cases never calling the client, the request carrying the right prompt version and options, and no user free text in the request.

**Live mode (opt-in, off in CI):** `RD_AI_LIVE_EVAL=1` plus an endpoint and deployment. The same cases run three times against the real deployment. The pass criteria are:
- the output passes validation within two attempts;
- it covers the case's required keys (e.g. the spike case cites `week.N.acwrAtEnd` or `acwr.peak.value` in `watchOuts`);
- it avoids the case's forbidden keys (e.g. the taper week is not a watch-out).

Token usage and cost are printed per case.

## Consequences

- A narrated number cannot disagree with the dashboard: the model never writes one. Grounding is a deterministic check, testable without a model.
- Prose is a little stiffer ("{totals.distance}" rather than "about 400 km"). Small models handle placeholder contracts well when instructed. Live eval measures the first-attempt pass rate.
- No user free text reaches the provider: no injection surface and less personal data. Narratives cannot mention a race by name; they say "your trail race on {race.2.date}".
- Empty and future periods cost nothing and always answer.
- The fact sheet is a second, model-facing mapping of the report (ADR 0025 §4) with its own version. A metrics change bumps `metricsVersion`; a projection change bumps `factSheetVersion`. Either one makes cached summaries stale.

## Alternatives considered

- **Function tools that the model calls** (brief wording): extra round trips and nondeterministic coverage (the model may skip a tool) for the same data. Kept as an upgrade path.
- **Free-text numbers + a post-hoc number matcher:** fragile with units, rounding and locales, and it rejects valid paraphrases. Rejected.
- **The model writes numbers, the server rewrites them:** still needs number extraction. Rejected.
- **Free-form Markdown output:** cannot be validated or styled per section, and adds an XSS surface. Rejected.
- **Sending notes and titles "delimited as untrusted":** mitigates injection but does not remove it, and sends personal text. Not needed for a metrics narrative. Rejected.
- **An LLM judge in the request path** (a second call that checks the first): doubles cost and is nondeterministic. It is possible only in live eval, via `Microsoft.Extensions.AI.Evaluation`.

## References (checked 2026-11-04)

- Agent Framework structured outputs (`RunAsync<T>`, `ResponseFormat`, `ChatClientAgent`; updated 2026-11-03): https://learn.microsoft.com/agent-framework/agents/structured-outputs
- Agent Framework, Azure OpenAI provider (Chat Completions/Responses clients, function tools, "treat model output as untrusted input"; updated 2026-11-02): https://learn.microsoft.com/agent-framework/integrations/by-component/model-providers/azure-openai
- `ChatOptions` (`MaxOutputTokens`, `Reasoning`, `ResponseFormat`, `Seed`; Microsoft.Extensions.AI.Abstractions 10.10.1): https://learn.microsoft.com/dotnet/api/microsoft.extensions.ai.chatoptions
- Microsoft.Extensions.AI overview (`IChatClient`, middleware, testability; 2026-09-14): https://learn.microsoft.com/dotnet/ai/microsoft-extensions-ai
- Reasoning models (no `temperature` on GPT-5.x; `max_completion_tokens` includes reasoning tokens; `reasoning_effort` values; updated 2026-10-30): https://learn.microsoft.com/azure/foundry/openai/how-to/reasoning

## Amended 2026-11-04 (implementation)

What the implementation on branch `m6-ai-summary` changed or made precise. Details and review findings: [M6 plan §12](../plans/M6-ai-summary.md#12-implementation-notes--deviations-2026-11-04).

### Demo ACWR peak and `acwr.peak.isRaceWeek` (review finding M1)

- The demo default view's peak is **1.79 on Fri 2026-10-23** (week −2, `acwr.peak.week` 10), not 1.59 in the race week. The race is the trail race of Sun 2026-10-18 (week −3, `week.9`, `acwrAtEnd` 1.59).
- `acwr.peak.isRaceWeek` means "a completed race inside the period within the 7 days ending on the peak date" (§1, defined there). The name says "week", but the rule is the acute window, not the ISO week. Renaming it (e.g. `acwr.peak.raceInAcuteWindow`) is a candidate for `factSheetVersion` 2. It is not done now, because a rename changes the prompt, the fixtures and every cached fingerprint.
- Edge case: a peak in the first 6 days of a period driven by a race before `from` gets `isRaceWeek = false` (§1). Accepted.

### Validator (§3), replaces the rows named

| Code | Rule as implemented (`SummaryValidator`, Domain) |
|---|---|
| `literalNumber` | Outside placeholders:<br>• any Unicode digit, letter-number or other-number character (`Nd`, `Nl`, `No`, so `Ⅻ` and `²` too), `%`, `‰`, `°`;<br>• cardinal words from "two" up, **plurals** included ("hundreds", "thousands", "millions", "dozens", "tens");<br>• **ordinals** "first" … "twentieth", the tens ordinals ("thirtieth" … "ninetieth"), "hundredth", "thousandth", and "twice"/"thrice" (whole words, case-insensitive);<br>• **ASCII Roman numerals**: a standalone letter run of **≥ 2 letters, all upper case**, that is a well-formed numeral (`II`, `IV`, `XII`). "I", single capitals and lower-case words ("mix", "civil") never match.<br>An all-capitals word that happens to be a valid numeral ("MIX", "XC") is flagged. **Accepted:** the prompt asks for plain prose, and a false rejection costs one retry. "One" and "half" stay allowed. |
| `forbiddenTopic` | Deny-list v1 extended with `pain*`, `hurt*`, `strain*`, `tendon*`, `tendin*`, `fractur*` (manifest v1 and `SummaryValidationPolicy.DefaultDenyList`). `strain*` forbids the bare word in prose (the injury sense). The strain metric could only ever be cited as a `{key}`, and placeholders are removed before the scan. |
| `raceWeekPeak` | Applies to the **headline, `watchOuts` items and the suggestion** (`wentWell` and `dataNotes` are exempt: they cannot frame a ratio as a spike). Two cases:<br>• a text citing any `acwr.peak.*` key while `acwr.peak.isRaceWeek` is true must also cite `acwr.peak.raceType`, the peak bucket's `week.N.raceType`, or any `race.*` key;<br>• a text citing `week.N.acwrAtEnd` or `week.N.acwrBand` while `week.N.raceType` exists (a completed race in that week, so inside the acute window of its end-of-week ratio) must also cite `week.N.raceType` or any `race.*` key. |
| `markup` | Also rejects `://` and `www.` (case-insensitive) anywhere in the text. |

### Strict schema and the wire request (§2, §5)

- The JSON exporter reuses the item schema through `$ref`. Strict mode documents only `$defs`, so `TrainingInsightsAgent` **inlines** the `$ref`s before sending.
- The strict flag is sent with `ChatOptions.AdditionalProperties["strict"] = true`. Microsoft.Extensions.AI.OpenAI maps it to `response_format.json_schema.strict: true`.
- `FoundryWireTests` (Ai.Tests) checks the real HTTP request of the Foundry client against a local handler:
  - path `/openai/v1/chat/completions`, a `Bearer` token for the scope `https://ai.azure.com/.default`;
  - `max_completion_tokens: 1500`, `reasoning_effort: "low"`, `response_format.json_schema.strict: true`;
  - **no** `temperature`, no `max_tokens`, no top-level `strict`.

### Prompt v1 and templates (§4, §6)

- The v1 prompt hash is `dc1ed4aacc2d7540c2eb5015e7ef1bce0d5bbad56ae17b0350e78ad5370b9afc`. The hash is computed over the **LF-normalized** text, so a Windows checkout does not fail it.
- Templates have **variants with conditions**:
  - `fewSessions` has a "one session" and a "few sessions" variant, and says "so far" only when the period is partial;
  - `empty` says "yet" only when the period is partial.

### Fact sheet (§1): differences from the plan's step 2

- **Unavailable ACWR as flags:** `acwr.asOf.unavailable` plus `acwr.asOf.insufficientHistory` or `acwr.asOf.noChronicLoad`. There is no reason code string.
- `period.asOf` is **omitted** for a wholly future period, like `acwr.asOf.*`.
- A wholly future week or month carries `week.N.future` / `month.N.future` instead of completed values.
- **Size trimming** keeps the sheet well under the cap:
  - no comparison totals (the comparison window dates and the deltas stay);
  - no run counts per week (months keep theirs);
  - `week.N.complete` only when false;
  - the top 8 run types (`type.N.*`, by duration);
  - the 6 most recent races (`race.N.*`, `race.1` is the oldest listed). `races.*Count` still covers all races.
- The cap stays 24 000 characters. The demo default view is about 16.2k characters.

## Amended 2026-11-04 (fact sheet as the data-residency safeguard)

- The production model deployment is **Global Standard**, not Data Zone EU ([ADR 0027 amendment "deployment type"](0027-ai-model-provider-foundry-and-local-dev.md#amended-2026-11-04-deployment-type)): the user's subscription has no EU Data Zone quota for gpt-5.4-mini. Data at rest stays in `swedencentral`, but **prompts and responses may be processed in any Azure region worldwide**.
- The §1 rule "no free text, names, notes, location, heart rate or ids reach the model" is therefore **also the data-residency safeguard**, not only the injection and minimization rule. What leaves the EU is the fact sheet: numeric training metrics, dates and run-type codes.
- **Any future fact-sheet addition must stay metrics-only** (a `factSheetVersion` bump is the review point). The CI eval assertion "no user free text in the request" (§7) guards it; anything that would add personal or free-text data needs a new ADR and, while the deployment is Global Standard, the user's explicit consent.
- The SPA's "what the model sees" note stays accurate; it does not name a processing location.
