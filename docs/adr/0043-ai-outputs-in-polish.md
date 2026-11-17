# 0043 – AI outputs in Polish: request language, prompt v3, per-language validation rules, templates and demo fixtures

**Status:** Accepted 2026-11-17 (M10 plan approved with defaults; implemented in M10 2026-11-17; the §3 "As implemented" note records C1; amended 2026-11-17 after the review and the G-L1 live eval (new rules `genderedForm` and `wrongLanguage`), see the end; **§3 "only the request language's set, never the union" and §5 "an English draft for `pl` is not a validator rule" are refined by the amendment**)
**Date:** 2026-11-17
**Related:** [0026](0026-training-insights-agent-and-grounding.md) (fact sheet, placeholders, `literalNumber`, `forbiddenTopic`, templates), [0027](0027-ai-model-provider-foundry-and-local-dev.md), [0028](0028-summary-cache-quotas-and-demo-summaries.md) (cache key with language, demo fixtures), [0040](0040-training-plan-generation.md) (plan prompt, validator, fact sheet `language`), [0041](0041-training-suggestions-storage-and-calendar.md) (demo plan), [0042](0042-ui-localisation-and-language-preference.md), [M10 plan](../plans/M10-polish-localisation.md)

## Context

The summary pipeline was built language-ready. Its pieces:
- `lang` is part of the request and of the cache key `(UserId, PeriodFrom, PeriodTo, Compare, Language)`;
- `manifest.json` lists `languages`, and the API accepts only those;
- templates (`templates/<lang>.json`) and demo fixtures (`demo/<lang>/…`) are per language and checked at startup;
- prompt v2 already says "Write in the language named in the user message";
- numbers are placeholders `{key}` that the SPA formats in the UI language, so a Polish summary shows "12,4 km" without the model writing a digit.

The plan pipeline has a `language` in its fact sheet, but `TrainingPlanService.Language` is the constant `"en"`, and prompt v2 says "Write plain, encouraging English".

Several parts are English-only:
1. `SummaryValidator.literalNumber` bans English number words ("two" … "thousand", "first" … "twentieth", "twice"). A Polish text could write "trzy tygodnie" and pass.
2. The `forbiddenTopic` deny list ("injur*", "pain*", "doctor", …) is English. The matcher lower-cases but does not fold diacritics.
3. Polish grammar with placeholders. A placeholder renders a nominative noun phrase (run type "Długie wybieganie", band "optymalny") or a number with unit. Polish sentences often need another case ("z {type}" → instrumental). Second-person past tense is gendered ("zrobiłeś/zrobiłaś"), and the app does not know the user's gender.
4. Polish text is about 10–30 % longer, while the limits stay the same: headline 160, item 280, plan notes 600, description 300 characters.

## Decision

### 1. Language of AI texts = UI language, explicit per request

- **Summary:** the SPA sends `lang = i18n.language` on `GET` and `POST /api/insights/summary`. This is the existing parameter, so the contract does not change. A Polish summary is a separate cache row; switching back to English shows the cached English one. Each language's generation counts against the normal quota (ADR 0028, unchanged).
- **Plan:** `GenerateTrainingPlanRequest` gains `lang?: "en" | "pl"` (default `en`), validated against `prompts/training-plan/manifest.json` `languages` (400 `errors.lang` otherwise). The fact sheet's `language` carries it. Suggestions and plan notes are stored in the language they were generated in. Switching the UI language later does not translate them; regenerating does. The plan read model gains `plan.language` so the SPA can show "Generated in English. Regenerate in Polish?" as a hint (no automatic regeneration).
- **Demo:** demo summary fixtures and the demo plan exist per language. The guest's demo plan uses the guest's seed language (ADR 0042 §3, §7).

### 2. Prompt v3 (both features), immutable versions kept

`prompts/training-insights/v3.md` and `prompts/training-plan/v3.md` become `current` (v1/v2 stay, hashed, ADR 0026 §6):
- language-neutral wording ("Write in the language named in the user message", replacing "plain English"); `display` strings stay English metric because they are magnitudes for the model only;
- a **Polish section**, applied when the language is `pl`:
  - informal singular "ty", gender-neutral: no second-person past-tense verbs; use present tense or noun phrases ("Masz za sobą {x}", "Łącznie: {x}"), not "Przebiegłeś {x}";
  - a placeholder stands where a **nominative noun phrase or a number with its unit** fits: after a colon, as the subject, or in quotes. Never after a preposition that needs another case;
  - numbers only as placeholders; number words forbidden (rule below);
  - terms from the glossary in [`docs/i18n.md`](../i18n.md) (e.g. "długie wybieganie", "bieg tempowy", "obciążenie", "ACWR");
- for the plan: `description` in the target language, ≤ 120 characters as before (the stored limit is 300, so Polish fits); digits stay allowed in plan descriptions (ADR 0040).

`factSheetVersion`/`planFactSheetVersion` do not change. The fact sheets already carry `language`.

### 3. Per-language validation rules (Domain, no infra)

- `SummaryValidationPolicy`/`PlanValidationPolicy` gain `Language`. Number-word and ordinal sets move to `Insights/Narrative/Languages/{English,Polish}NumberWords.cs`:
  - **en:** unchanged (today's lists);
  - **pl:** exact inflected forms, not prefixes, because Polish stems collide ("piąt*" would match "piątek", "dwór" starts with "dw"). They cover cardinals from 2 upward in all cases ("dwa, dwie, dwóch, dwoma, dwu, trzy, trzech, trzem, trzema, …, dziesięć … dziewięćdziesiąt, sto, setki, tysiąc, tysiące, tysięcy, milion…, tuzin…"), the "-naście"/"-dzieści"/"-set" families, ordinals "drugi … dwudziesty" in all genders and cases plus "pierwszy" forms, and multiplicatives ("dwukrotnie, trzykrotnie, podwójnie, potrójnie, dwa razy"). "jeden/jedna/jedno", "pół", "połowa" and "półmaraton" stay allowed (like "one"/"half" in en).
  - Only the request language's set applies, never the union: English "ten" is Polish "this".
- **Deny list per language** in the manifest version entry: `"denyList": { "en": [...], "pl": [...] }` for v3 (v1/v2 keep the flat English list, read as `en`). For `pl` the validator applies **en ∪ pl**, because models code-switch and the English stems do not collide with Polish words. Matching for `pl` folds diacritics on both sides (reusing `Domain/Races/TextFolding`), so "ból", "bol" and "bolesny" all match `bol*`. Proposed `pl` list (the user reviews it in M10 Part C): `kontuzj*`, `uraz*`, `przetrenow*`, `diagnoz*`, `medyczn*`, `chorob*`, `chory`, `chora`, `sercow*`, `zawal*`, `lekarz*`, `lekark*`, `fizjoterap*`, `bol*`, `bolesn*`, `naciagn*`, `nadwyrez*`, `sciegn*`, `zapaleni*`, `zlaman*`, `peknieci*`, `ryzyko`, `ryzyka`.
- **As implemented (M10 C1, 2026-11-17; details and the full lists in the [M10 plan §10](../plans/M10-polish-localisation.md#10-implementation-notes--deviations), step 9):**
  - Number words for `pl` are folded exact forms as above, plus number compounds (a numeral combining form + a closed list of unit/`-krotn-` stems: "dwukrotnie", "trzytygodniowy"), "kilkanaście/kilkadziesiąt/kilkaset" and "półtora". The sets live in `Insights/Narrative/Languages/`, the shared rules in `NarrativeLanguageRules.cs`.
  - **Deviation from "never the union":** for `pl` the English number words also apply, **except the Polish homograph "ten"**. "ten" was the only collision, and an English number word in a Polish draft is still an invented number. English drafts get no Polish words.
  - The proposed `pl` deny stems that collide with ordinary running Polish became exact forms or phrases: `bol*` (bolid, Bolesławiec) → the forms of ból/boleć; `zawal*` (zawalony tydzień) → the forms of zawał; `zapaleni*` (zapaleni biegacze) → the forms of zapalenie; `zlaman*` ("złamanie {x}", breaking a time barrier) → `zlaman* zmeczeniow*`, `zlaman* kosc*`. A `*` may end any word of a phrase entry. A few forms were added (`chore`, `chorzy`, `lekarsk*`, `obolal*`, `sciegien`, `achilles*`, `pekniet*`, `ryzyku`, `ryzykiem`, `zachorow*`).
  - `en` is unchanged byte-for-byte (no folding, same lists).
- Length limits are unchanged. If the G-L1 live eval shows `schema`/length violations above 1 in 20 for `pl`, the limits become per-language manifest values in a later prompt version. Nothing is pre-emptively raised.

### 4. Templates and demo fixtures

- `prompts/training-insights/templates/pl.json` holds the three deterministic templates. They are used for < 3 completed sessions, so the counts are 0/1/2 and the existing equality predicates give the Polish forms ("0 treningów", "1 trening", "2 treningi"). No new predicate is needed.
- `prompts/training-insights/demo/pl/12w-previous.v1.json` is hand-written by Claude and reviewed by the user. It is validated at startup like the English one (AiStartupCheck already iterates languages).
- The demo plan's Polish texts come from the seed translation map (ADR 0042 §7). The demo plan fixture's every-"today" test runs for both languages.
- `manifest.json` `languages` becomes `["en", "pl"]` for both features in the same commit as the templates and fixtures. The startup check fails otherwise.

### 5. Fake model and evals

- `RuleBasedFakeChatClient` returns Polish canned drafts for `Language: pl` (summary and plan), so CI, full-stack and dev cover the Polish path.
- Eval cases (CI mode, scripted drafts) gain Polish good and bad drafts: number words in several cases, a forbidden topic without diacritics, a placeholder after a preposition (allowed by the validator but listed as a known limit), and an English draft for `pl`. The last one is a prompt-quality metric, not a validator rule.
- The **live eval** (`RD_AI_LIVE_EVAL=1`) runs v3 for `en` and `pl`: summary cases × 3 and plan cases × 3 per language. Pass gate: every case ≥ 2/3 and overall ≥ 90 % per language, plus a human read of 3 Polish outputs per feature by the user (grammar, tone, gender neutrality). Cost about $1–2, user-confirmed (M10 gate G-L1, run before merge).

## Consequences

- Polish users get Polish summaries and plans with the same grounding guarantees: placeholders only, number words banned, forbidden topics in both languages.
- A Polish summary and an English one of the same period are separate cached generations (quota per generation, unchanged).
- Prompt v3 changes the English prompt slightly as well, so English is re-evaluated in the same gate.
- The Polish number-word list cannot be exhaustive (rare forms, typos). The residual risk is a model-written number in words, which is the same class of risk as in English, mitigated by the prompt and the eval.
- Placeholder case agreement is enforced by the prompt, not the validator. A wrong case ("z Długie wybieganie") is a style defect, not a grounding defect. The eval's human read tracks it.

## Alternatives considered

- **Translate English outputs with a second model call:** double cost and latency, and the translation step could alter facts.
- **Inflect placeholders client-side (case-aware formatting):** needs case metadata per placeholder and noun declension tables for run types and bands. Too heavy for one language; the prompt rule is enough.
- **Per-language prompt files (`v3.pl.md`) with a per-language `current`:** changes the manifest schema and doubles the hashed artefacts. One prompt with a language section is enough for two languages.
- **Union of number-word lists across languages:** false positives ("ten" = "this").
- **Plan language from the profile (`Users.Language`) instead of the request:** implicit, and differs from the summary contract. The SPA knows the UI language.
- **Leave AI English in a Polish UI:** simplest, but a jarring mixed-language page. This is acceptable as an interim state only (M10 Part B ships before Part C if needed).

## Amendment (2026-11-17, implementation, review and the G-L1 live eval)

The decision stands. Refinements as built (details: [M10 plan §10](../plans/M10-polish-localisation.md#10-implementation-notes--deviations), steps 9–11, "Review fixes", "G-L1 follow-up"; gate numbers: plan §5.2 gate log):
1. **Number words (§3).** Besides the C1 note in §3 (union with English except "ten"), every numeral combining form is a number word on its own, because folding turns "pięcio-kilometrowy" into two words; number nouns of 11–19 and the tens, and the hundreds/thousands ordinals were added (review L1). Ordinal idioms ("po pierwsze", "z drugiej strony") are not whitelisted; prompt v3 asks for "natomiast", "z kolei" instead (review L2).
2. **Deny list `pl` (§3).** Added after the review (M1): `zlaman* przeciazeniow*`, `zlaman* nog*`, `naderw*`, `zapaln*`, `rehabilitac*`, `ortoped*`, `szpital*`, `dolegliwos*`. The built-in `DefaultPolishDenyList` and `denyList.pl` of both v3 manifests are equal (test). v3 was not deployed yet, so both v3 prompts were edited in place and re-hashed.
3. **Request language (§1).** The plan `lang` defaults to `en`, not to the profile language; the SPA always sends the UI language (`aiLanguage()`), so Regenerate uses the current UI language. Summary quotas are not per language.
4. **New rule `genderedForm` (Polish only; summary and plan validators).** The G-L1 human read found masculine second-person forms in 3 of 18 Polish summaries despite the prompt. A text is rejected for a word ending in `łeś`/`łaś` (past tense, with diacritics), a folded `lbys`/`labys` ending (conditional) or "powinieneś"/"powinnaś". Not covered (no word-level test without false positives; left to the prompt): the past tense without diacritics, gendered adjectives and participles, a past form after a separate "byś"/"żebyś"/"gdybyś".
5. **New rule `wrongLanguage` (Polish only; summary and plan validators; refines §5).** The G-L1 re-run had one Polish summary written entirely in English that every rule accepted. A field is rejected when it holds ≥ 2 distinct English **function words** from a fixed stop list (outside `{placeholders}`, whole lower-case words; Polish homographs, loanwords and acronyms excluded); when the whole draft holds ≥ 3 distinct ones, every field with at least one is rejected too. The eval's language check stays as a stricter, eval-only metric. The **guest demo plan** ignores `wrongLanguage` (our reviewed texts; an untranslated seed entry stays English by design, ADR 0042 §7).
6. **Retry notes.** When `genderedForm` or `wrongLanguage` is among the violations, the retry note appends a fixed hint (how to rephrase gender-neutrally; write every field in Polish). Fixed text only, never model text, so the injection and data-residency position of ADR 0026/0040 is unchanged. Both rule codes are appended to their enums.
7. **G-L1 result (§5).** Passed and accepted by the user 2026-11-17: Polish summaries 51/54 (94 %) over three runs, every case ≥ 2/3; plans en 20/21, pl 21/21; summaries en 18/18; spend ≈ $1.42. Max plan output tokens: en 4,653, pl 5,228 of 8,000 (65 %), none truncated, so the 8,000 cap stays. Length limits stay global: none of the recorded Polish failures was a length violation (§3 last bullet; one malformed-JSON retry is the only `schema`-class failure, 1 in 54).
8. **Known residuals.** The English deny entry `strain*` also rejects "strained" in plan prose (pre-existing, M9); count-before-noun and placeholder-as-word phrasings still occur in Polish; the extra retries slightly raise the chance of a failed generation. These are follow-ups for the next prompt version (plan §10 "Follow-ups").

## References

- ADR 0026 §3 (validator rules), §4 (templates), §6 (manifest immutability); ADR 0028 §1 (cache key), §5 (demo fixtures); ADR 0040 §3–§5.
- i18next plurals (for the SPA side of templates): https://www.i18next.com/translation-function/plurals
