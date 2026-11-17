# M10 – Polish localisation: plan

**Status:** Approved 2026-11-17 (outline and all §9 defaults approved by the user: "Approve with defaults"). Implementation in progress on `m10-polish-localisation`.
**Builds on:**
- M1/M3: i18next with typed keys (`web/src/i18n`), `Intl`-based plain-date and unit formatters (ADR 0017), the run form's server-error mapping (ADR 0018), bundle budget, fake API, Playwright `chromium` + `phone-la`.
- M2: `Users` profile, `GET`/`PUT /api/me`, guest creation and `DemoDataSeeder`.
- M6/M9: the AI pipelines (summary `lang` + per-language templates and demo fixtures; plan fact sheet `language`), prompt manifests, validators, fake model, live eval.

**Related ADRs:** [0003](../adr/0003-frontend-stack-and-ui-kit.md) · [0011](../adr/0011-guest-accounts-lifecycle.md) · [0017](../adr/0017-client-dates-without-a-date-library.md) · [0018](../adr/0018-run-forms-and-server-errors.md) · [0019](../adr/0019-frontend-test-strategy.md) · [0026](../adr/0026-training-insights-agent-and-grounding.md) · [0028](../adr/0028-summary-cache-quotas-and-demo-summaries.md) · [0029](../adr/0029-web-tier-nginx-container.md) · [0040](../adr/0040-training-plan-generation.md).
**New (accepted with the plan):**
- [0042](../adr/0042-ui-localisation-and-language-preference.md): languages, resolution, persistence, bundles, switcher, formatting, translated content, CI checks;
- [0043](../adr/0043-ai-outputs-in-polish.md): AI language = UI language, prompt v3, per-language validator rules, templates and demo fixtures, evals.
- Living guide with tone rules and the **glossary draft**: [`docs/i18n.md`](../i18n.md).

M10 has three offline parts (A: mechanism, B: translations, C: AI in Polish) and user-confirmed gates (§5.2). **No new Azure resource, no new env flag, no new npm/NuGet package.**

## 1. Goal & acceptance criteria

**Goal:** the whole app is available in **Polish (`pl`)** next to English. The language comes from the browser on the first visit, can be switched in the header at any time, and is remembered per account (users and guests) and per browser. Dates, numbers and plurals follow Polish rules. A Polish guest gets a Polish demo diary. AI summaries and plan suggestions are written in the UI language.

**Out of scope:** other languages (the mechanism is generic); imperial units; translating user-entered data (titles, notes) and the race catalogue (already Polish); localised server problem texts; e-mail; re-translating already generated AI texts.

| # | Criterion | How verified |
|---|---|---|
| AC1 | **Resolution, no flash:** `rd.lang` → `navigator.languages` (first released match) → `en`; `<html lang>` and the eager bundle are set **before the first render**. A signed-in profile language that differs wins once and updates `rd.lang`. | Vitest (resolver matrix) + Playwright (`pl-PL` context shows Polish shell while `/api/me` is delayed) |
| AC2 | **Switcher** in the header (every page, users and guests) and on the sign-in page. Options are "English"/"Polski" with `lang` attributes. Keyboard and screen-reader usable. Switching re-renders without reload, updates `<html lang>` and `rd.lang`, and calls `PUT /api/me/language` when there is a session. | Vitest + Playwright + axe |
| AC3 | **API:** migration `Localisation` (additive): `Users.Language`, `Goals.PlanLanguage`. `MeResponse.language`; `PUT /api/me/language` (Auth, 204, 400 for unsupported values, only the caller's row); `POST /api/guest-sessions?lang=` (400 for unsupported values; the guest is seeded in it). Inventory test and 401 sweep updated; OpenAPI + `schema.d.ts` regenerated. | Api.Tests (Testcontainers) + api-contract CI |
| AC4 | **Bundles:** the Polish eager namespaces are a lazy chunk (`i18n-pl-eager`, in `LAZY_ONLY`), and English users never load it. The initial load grows by ≤ 2 kB gzip. Lazy namespaces carry `en` + `pl`. | CI `bundle-budget` |
| AC5 | **Translations complete:** `web/src/i18n/pl/*.json` for all 11 namespaces. The parity test is green: same keys, same interpolation variables, `pl` plural groups `_one/_few/_many/_other` wherever `en` has `_one/_other`, no empty values, no "same as English" outside the allowlist. **Every namespace reviewed by the user.** | Vitest + user review (§5.1 B-steps) |
| AC6 | **Formatting in `pl`:** month/day headings capitalised ("Listopad 2026"); decimal comma in displayed values **and** in form inputs; thousands separator from 10 000; ISO Monday-first weeks unchanged; plural forms correct for 0, 1, 2, 5, 12, 22, 25; label-sorted lists use `Intl.Collator`. | Vitest incl. `test:tz` (LA, Kiritimati) |
| AC7 | **Content:** a guest created in `pl` gets Polish demo notes, demo goal title and demo plan texts (seed map complete, startup check). Proper names untouched. Unmapped server field errors show a localised generic message in `pl`. The geocoder forwards the UI language. MSAL sends `ui_locales`. | Api.Tests + Vitest |
| AC8 | **AI summary in `pl` (fake model):** `lang=pl` accepted; Polish templates and demo fixture valid at startup; validator applies Polish number words and the en ∪ pl deny list with diacritic folding; `en` and `pl` cached separately; English behaviour unchanged. | Domain.Tests + Ai.Tests + Api.Tests |
| AC9 | **AI plan in `pl` (fake model):** `lang` on generate; stored `plan.language`; demo plan in the guest's language; the SPA shows a "generated in English, regenerate?" hint when languages differ. | Ai.Tests + Api.Tests + Vitest |
| AC10 | **Layout:** in `pl`, no horizontal scroll at 360 px on Diary, Day panel, run form, Insights, Goals, race panels and the header; truncated cell labels keep full accessible names; axe (serious/critical) clean in `pl`. | Playwright `desktop-pl` + `phone-pl` |
| AC11 | `dotnet build` warning-free; all CI jobs green; the full-stack suite has one Polish guest journey. G-L1 live eval passed (§5.2). | CI + gate log |

## 2. Files / projects by owning agent

### 2.1 frontend-react
```
web/src/main.tsx                                  # await initLanguage() before createRoot().render()
web/src/i18n/index.ts                             # init with the resolved language; supported/released lists; test-only missingKeyHandler
web/src/i18n/language.ts (new)                    # resolveInitialLanguage, ensureLanguage, setLanguage, applyProfileLanguage, <html lang>
web/src/i18n/i18n-pl-eager.ts (new)               # registers pl common/auth/calendar/runs/runTypes/errors (dynamic import target)
web/src/i18n/lazyBundles.ts                       # + pl for maps/tracks/insights/races/goals
web/src/i18n/pl/*.json (new, 11 files)            # Part B
web/src/i18n/en/{common,errors,goals}.json        # language.* keys, problem.fieldInvalid, plan.languageHint
web/src/components/layout/{AppShell.tsx, LanguageSwitcher.tsx (new)}
web/src/features/auth/SignInPage.tsx              # switcher; POST /api/guest-sessions?lang=
web/src/auth/useLanguageSync.ts (new)             # apply me.language once per session
web/src/auth/entra/msalClient.ts                  # extraQueryParameters.ui_locales
web/src/api/client.ts                             # Accept-Language: <ui language> on /api calls
web/src/lib/dates/plainDate.ts                    # capitalizeFirst(text, locale)
web/src/lib/format/units.ts                       # formatKmInput(meters, locale) with the locale decimal separator
web/src/features/calendar/MonthHeader.tsx, runs/DayPanel.tsx, …   # capitalised headings
web/src/features/runs/RunForm.tsx, goals/GoalFormPage.tsx, races/RaceSignupForm.tsx  # generic field message for non-en
web/src/features/insights/summary/summaryQueries.ts   # lang = AI language (Part C)
web/src/features/goals/{goalQueries.ts, PlanSection.tsx}  # lang on generate, language hint (Part C)
web/scripts/bundle-budget.mjs                     # LAZY_ONLY += 'i18n-pl-eager'
web/src/api/generated/schema.d.ts                 # regenerated
```

### 2.2 backend-dotnet
```
src/RunnersDiary.Domain/Localization/SupportedLanguages.cs (new)   # ["en","pl"], IsSupported
src/RunnersDiary.Domain/Users/User.cs                              # Language (nullable), SetLanguage
src/RunnersDiary.Domain/Goals/Goal.cs                              # RecordPlan(..., language) → PlanLanguage
src/RunnersDiary.Infrastructure/Persistence/Configurations/{User,Goal}Configuration.cs
src/RunnersDiary.Infrastructure/Persistence/Migrations/<ts>_Localisation.cs
src/RunnersDiary.Infrastructure/Guests/{DemoDataSeeder.cs, DemoTextLocalizer.cs (new)}  # seed/demo/i18n/<lang>.json map, startup check
src/RunnersDiary.Infrastructure/Goals/DemoTrainingPlan.cs          # texts + fact sheet in the guest's language
src/RunnersDiary.Api/Features/Me/{MeContracts.cs, GetMe.cs, UpdateMyLanguage.cs (new)}
src/RunnersDiary.Api/Features/GuestSessions/CreateGuestSession.cs  # ?lang
src/RunnersDiary.Api/Features/Goals/GoalContracts.cs               # plan.language
src/RunnersDiary.Api/openapi/runnersdiary-api.json                 # regenerated
seed/demo/i18n/pl.json (new)                                       # Part B content
```

### 2.3 ai-insights
```
src/RunnersDiary.Domain/Insights/Narrative/Languages/{EnglishNumberWords.cs, PolishNumberWords.cs} (new)
src/RunnersDiary.Domain/Insights/Narrative/SummaryValidator.cs     # policy.Language; folding for pl deny matching
src/RunnersDiary.Domain/Goals/Planning/{TrainingPlanValidator.cs, PlanValidationPolicy.cs}
src/RunnersDiary.Infrastructure/Ai/{PromptCatalog.cs, PlanPromptCatalog.cs}   # denyList per language (v3), languages
src/RunnersDiary.Infrastructure/Ai/{TrainingPlanService.cs, RuleBasedFakeChatClient.cs}  # lang; Polish fake drafts
src/RunnersDiary.Api/Features/Goals/GenerateTrainingPlan.cs        # lang validation (400 errors.lang)
prompts/training-insights/{v3.md, manifest.json, templates/pl.json, demo/pl/12w-previous.v1.json, README.md}
prompts/training-plan/{v3.md, manifest.json, README.md}
tests/RunnersDiary.Ai.Tests/**                                     # pl validator, templates, fixtures, fake, eval cases
```

### 2.4 maps-gpx
```
src/RunnersDiary.Infrastructure/Maps/AzureMapsGeocoder.cs  # Accept-Language (en|pl) from the caller
src/RunnersDiary.Api/Features/Geocoding/*                  # pass the reduced Accept-Language through
web/src/components/map/azure/AzureMapsMap.tsx              # map option language
```

### 2.5 azure-infra
None. No Bicep, nginx or workflow change. The CSP is unaffected because no inline script is added.

### 2.6 qa-tester
```
web/src/i18n/parity.test.ts (new)          # ADR 0042 §9
web/src/i18n/language.test.ts (new)        # resolver matrix, profile override, html lang
web/src/lib/**/*.test.ts                   # pl cases (run under test:tz)
web/playwright.config.ts                   # projects desktop-pl (*.pl.spec.ts, locale pl-PL, Europe/Warsaw), phone-pl (*.pl.phone.spec.ts, 360×640)
web/e2e/{language.pl.spec.ts, smoke.pl.spec.ts, layout.pl.phone.spec.ts} (new)
web/e2e-fullstack/polish-guest.spec.ts (new)
web/src/test/fakeApi/*                     # me.language, PUT /api/me/language, guest ?lang, summary/plan lang=pl
tests/RunnersDiary.Api.Tests/**            # §8
```

### 2.7 reviewer (checklist)
- `lang` and `language` values are allowlisted before they reach SQL, prompts or the seed map. `Accept-Language` is parsed, reduced to en/pl and never echoed.
- `PUT /api/me/language` writes only the caller's row and is in the inventory and the 401 sweep.
- No Polish string merged without the user's review. Interpolations follow the nominative rule.
- Bundle: the Polish eager chunk stays lazy.
- Validator: per-language sets are not unioned (except the deny list), and folding applies only to deny matching.

### 2.8 architect
After approval: "Added in M10" in `api-contract.md` and `data-model.md`, the architecture components, the i18n guide status, and the ADR statuses. Deviations go in §10. The main agent updates CLAUDE.md (status line; "Frontend" convention: every new key in `en` **and** `pl`).

## 3. Contracts

### 3.1 API (all additive)
```ts
MeResponse += { language: "en" | "pl" | null }             // null = never chosen
// PUT /api/me/language  (operationId updateMyLanguage, Auth, general limiter)
UpdateMyLanguageRequest = { language: "en" | "pl" }        // → 204; 400 errors.language; 401
// POST /api/guest-sessions?lang=en|pl                     // optional; default en; other → 400 errors.lang
// GET|POST /api/insights/summary  lang: "en" | "pl"       // existing parameter; allowed values from the manifest
GenerateTrainingPlanRequest = { replaceOtherGoals?: boolean; lang?: "en" | "pl" }   // default en; 400 errors.lang
GoalDto.plan += { language: "en" | "pl" }                  // PlanLanguage null (pre-M10 plans) → "en"
```
`Accept-Language` on `GET /api/geocoding/search` (`searchPlaces`) is read and reduced to `en`/`pl` (default `en`).

### 3.2 Schema delta (migration `Localisation`, additive, nullable)
| Table | Column | Type | Notes |
|---|---|---|---|
| `Users` | `Language` | `varchar(8) NULL` | Supported tag or null |
| `Goals` | `PlanLanguage` | `varchar(8) NULL` | Set by `RecordPlan`; null = English (pre-M10) |

Older images ignore both columns, so the image rollback stays safe.

### 3.3 Seed translation map
`seed/demo/i18n/pl.json`: `{ "version": 1, "texts": { "<exact English text>": "<Polish text>" } }`. It covers `runs.json` notes and titles plus the goal title, plan notes and descriptions in `training-plan.json`. Proper names are not keys. A test lists every seeded English text without an entry; the list must be empty once `pl` is released, and the startup check enforces the same.

### 3.4 Prompt manifests (v3)
```json
"3": { "file": "v3.md", "sha256": "…", "denyList": { "en": [ …today's list… ], "pl": [ …ADR 0043 §3… ] } }
"languages": ["en", "pl"]
```
v1/v2 entries are unchanged; a flat `denyList` is read as `{ "en": … }`.

### 3.5 SPA language module (sketch)
```ts
export const SUPPORTED_LANGUAGES = ['en', 'pl'] as const
export type Language = (typeof SUPPORTED_LANGUAGES)[number]
export function releasedLanguages(): readonly Language[]          // pl released with B5 (RELEASED_LANGUAGES = ['en','pl'])
export function resolveInitialLanguage(stored: string | null, navigatorLanguages: readonly string[]): Language
export function initLanguage(): Promise<void>                      // resolve → ensure bundle → changeLanguage → <html lang>
export function setLanguage(lng: Language, opts: { persistToProfile: boolean }): Promise<void>
export function applyProfileLanguage(profile: Language | null): Promise<void>
export const AI_LANGUAGES: readonly Language[]                     // ['en'] until Part C, then ['en','pl']; else 'en' is sent
```

## 4. UI
- Header: a compact select (globe icon + "EN"/"PL" on phones, full name from `sm` up) between the nav and `ApiStatus`/`UserMenu`. The header already wraps (`flex-wrap`).
- Sign-in page: the same control above "Try as guest".
- Goals page plan section: when `plan.language` ≠ the UI language, a muted line "This plan was generated in English." with the existing Regenerate button.
- Calendar and bubble labels: existing truncation; Polish weekday short names ("pon.", "wt.", …) from `Intl`. The phone grid keeps `narrow` names with full accessible names.

## 5. Implementation steps and confirmation gates

### 5.1 Offline (branch `m10-polish-localisation`, one reviewable commit per step)

**Part A – mechanism (no visible change in production: `pl` not released)**
1. [ ] **backend-dotnet:** `SupportedLanguages`, migration `Localisation`, `MeResponse.language`, `PUT /api/me/language`, guest `?lang`, `DemoTextLocalizer` (missing entries fall back to English and are reported), OpenAPI regenerated, Api.Tests.
2. [ ] **maps-gpx:** geocoder `Accept-Language`, Azure Maps display `language`.
3. [ ] **frontend-react (+ qa-tester):** language module, `initLanguage` before render, switcher (header + sign-in), profile sync, `Accept-Language`, `ui_locales`, guest `?lang`, `capitalizeFirst`, localised km input, generic field message, `Intl.Collator` where labels are sorted, bundle budget, missing-key handler, parity test (strict for every existing `pl` file; requires all 11 once `pl` is released), fake API updates, `pl/common.json`.

**Part B – translations (Claude drafts with the glossary; the user reviews each commit before the next one)**
4. [x] **B1 frontend-react (user approved 2026-11-17):** `common`, `auth`, `errors`, `runTypes`, `calendar`, `runs` (eager; ~360 lines) + plural audit (every `count` string has a plural group; no number-in-sentence without one).
5. [x] **B2 frontend-react (user approved 2026-11-17):** `maps`, `tracks`, `insights` (~540 lines).
6. [x] **B3 frontend-react (user approved 2026-11-17):** `races`, `goals` (~520 lines).
7. [x] **B4 backend-dotnet:** `seed/demo/i18n/pl.json` (demo notes, goal, plan texts), map completeness test strict.
8. [x] **B5 frontend-react + qa-tester:** release `pl` (`RELEASED_LANGUAGES`), layout fixes found at 360 px, Playwright `desktop-pl`/`phone-pl`, full-stack Polish guest journey.

**Part C – AI in Polish (ai-insights)**
9. [x] **C1:** Polish number words, per-language policies, folded deny matching; Domain.Tests (adversarial drafts per rule).
10. [ ] **C2:** prompt v3 (both features), manifests, `templates/pl.json`, `demo/pl/12w-previous.v1.json` (user reviews the Polish), Polish fake drafts, eval cases (CI mode).
11. [ ] **C3 (+ backend-dotnet, frontend-react):** plan `lang` + `PlanLanguage`, demo plan per language, SPA `AI_LANGUAGES` += `pl`, summary/plan `lang`, language hint; Api.Tests/Vitest.
12. [ ] **reviewer**, then **architect** (docs, deviations). The main agent updates CLAUDE.md, commits, and stops for approval.

If the user prefers, Part C can follow in a separate PR. Until then `AI_LANGUAGES = ['en']`, so a Polish UI shows English AI texts.

### 5.2 User-confirmed gates (each needs the user's explicit "yes")

| Gate | What | Cloud change |
|---|---|---|
| **G-L1** (costs ≈ $1–2) | Live eval **before merge**, with the user's `az login` and `RD_AI_LIVE_EVAL=1`: summary and plan cases × 3, in `en` and `pl`, prompt v3. Pass: every case ≥ 2/3 and ≥ 90 % per language; output tokens vs the 8,000 plan cap recorded; the user reads 3 Polish outputs per feature. On failure, fix and re-run, or merge with `pl` removed from `AI_LANGUAGES` and the manifests. | none (model calls only) |
| **G-L2** | Merge, then approve `deploy.yml`: the migration job applies `Localisation` (additive), new images. Live check: a `pl-PL` browser shows Polish at the first paint; a guest demo in Polish; switch to English and back; a Polish AI summary (demo preset, then one generation); an English browser unchanged. | schema; images |

**Rollback:** the normal automatic and manual image rollback; the nullable columns are harmless to older images. There is no kill switch: the language is code and content, costs nothing to run, and `AI_ENABLED=false` still stops all AI.

## 6. Cost
| Item | Estimate |
|---|---|
| Azure resources | None new |
| Live eval (G-L1) | ≈ $1–2 (plan cases dominate; ≈ $0.35 per 21 plan runs in M9, Polish output ≈ 1.3–1.6× tokens) |
| Production AI | Unchanged quotas and global budget. A Polish summary is its own cached generation; Polish plan outputs use more tokens (≈ +$0.01 per generation) |
| SQL | Two nullable columns; negligible |

## 7. Risks
| Risk | Impact | Mitigation |
|---|---|---|
| Translation volume (~1,400 lines + ~80 seed texts + AI fixtures) and **the user's review time** | Slow Part B; unnatural Polish | Split into 4 review commits; glossary first; the user approves the glossary before B1 |
| **Gendered Polish and case agreement** in interpolations (UI and AI) | Awkward or wrong forms ("Zapisałeś się", "z Długie wybieganie") | Style rules in the guide; nominative-slot rule; prompt v3 section; human read in G-L1 |
| **Plan output tokens in Polish** approach the 8,000 cap | Truncated JSON → 502 | G-L1 records tokens; raise `Ai:Plan:MaxOutputTokens` by configuration (ADR 0040), adjust the weight if needed |
| Polish number-word list incomplete or too broad | A number in words slips through, or false rejections | Exact inflected forms, not stems; adversarial tests; eval rejection codes |
| Layout overflow at 360 px (Polish ≈ +20–30 %) | Broken header, buttons or cells | `phone-pl` no-horizontal-scroll checks; shorten Polish instead of the layout |
| ICU differences (Node vs Chromium; NBSP, `formatRange`) | Flaky tests | Assert with normalised whitespace; compare against `Intl` output computed in the test, not literals, where possible |
| Future PRs forget Polish | English leaks into the Polish UI | Parity test fails CI; CLAUDE.md convention; reviewer checklist |
| Mixed language: user data, seeded notes, older plans | Mild inconsistency | Documented; plan language hint; no automatic translation |
| One-time language switch on a new device (profile ≠ browser) | A brief re-render after `/api/me` | Acceptable; `rd.lang` is updated, so it happens once per browser |

## 8. Test plan outline (qa-tester)

**Vitest:**
- `parity.test.ts`: keys, variables, plural groups (`_one/_few/_many/_other`), non-empty, "same as English" allowlist, all 11 files present once released.
- `language.test.ts`: `rd.lang` valid or invalid; `navigator.languages` `['pl-PL']`, `['de','pl']`, `['en-GB']`, `[]`; unreleased `pl` ignored in production mode; profile override; `<html lang>`; switch persists and calls `PUT` only with a session.
- Formatting in `pl` (also under `test:tz`): `monthLabel`/`dayLabel`/`rangeLabel`/`weekdayLabels`, `capitalizeFirst`, distance/elevation/delta/percent, km input round-trip with a comma, plural renders for 0/1/2/5/12/22/25 on calendar cell names and goals strings.
- Components rendered in `pl` with the missing-key handler: header + switcher, month grid, run form (server field error → generic message), summary card (`lang=pl` request), plan section (language hint), sign-in (guest `?lang=pl`).

**Playwright (fake API):**
- `language.pl.spec.ts` (`desktop-pl`): Polish at the first paint with `/api/me` delayed; switch to English and back, reload keeps the choice; `<html lang>`; axe on Diary, run form, Insights, Goals, race panels.
- `smoke.pl.spec.ts`: add a run with "12,5" km, see "12,5 km" in the cell and week totals; Insights KPIs with Polish formatting; AI summary demo in Polish.
- `layout.pl.phone.spec.ts` (`phone-pl`, 360×640): no horizontal scroll on the main pages and the header; Day panel; truncated bubble names keep full accessible names.

**Full-stack (`polish-guest.spec.ts`, fake model):** `pl-PL` context → guest → Polish demo notes in the Day panel → Polish demo summary → generate a plan (fake, Polish) → switch to English → hint "generated in Polish" shown.

**Api.Tests:** `PUT /api/me/language` (valid, invalid, isolation, 401), `MeResponse.language`, guest `?lang` (valid, invalid, seeded Polish notes, demo plan Polish), summary `lang=pl` accepted and cached separately, plan `lang` validation and `plan.language`, geocode `Accept-Language` reduction, inventory and 401 sweep.

**Domain.Tests / Ai.Tests:** Polish number words (each family, several cases; "ten tydzień", "piątek", "jeden", "pół", "półmaraton" allowed), deny list with and without diacritics, en ∪ pl deny on `pl`, union not applied to number words; templates `pl` valid for 0/1/2 sessions; demo fixture `pl` valid; demo plan every-today test per language; fake Polish drafts pass the validators; manifest v3 hashes; eval cases (CI mode) per language.

**Manual (G-L2):** VoiceOver reads Polish with the Polish voice (`<html lang>`); phone Safari header; one real Polish summary and plan read by the user.

## 9. Open questions (recommended defaults in bold; the plan proceeds with them unless the user objects)

1. **First visit:** **browser languages (first of en/pl), else English**, or always English until chosen.
2. **Switcher location:** **header on every page + sign-in page**, or a new Settings → General section (Settings is hidden while races are off).
3. **Persistence:** **profile field for every account (users and guests) + a `localStorage` cache, profile wins**, or `localStorage` only (no migration, no cross-device sync).
4. **Tone:** **informal "ty", gender-neutral phrasing**, or formal "Pan/Pani".
5. **Glossary:** **the draft in [`docs/i18n.md`](../i18n.md)**, which the user edits before B1. Three entries to check: Insights = "Analiza", a diary entry = "trening", Tempo run = "Bieg tempowy" (because "tempo" also means pace).
6. **Who translates:** **Claude drafts, the user reviews each Part B/C commit**, or an external translator.
7. **AI language:** **follows the UI language for summaries and plans**, or AI stays English.
8. **Server field errors in `pl`:** **localised generic message**, or the English server text.
9. **Guest demo language:** **seeded in the language at guest creation, no re-seed on switch**, or always English demo texts.
10. **Older plans after a switch:** **keep them and show a "generated in English" hint with Regenerate**, or show nothing.
11. **Delivery:** **one PR (A+B+C), live eval G-L1 before merge, one deploy G-L2**, or ship the UI (A+B) first and AI (C) later.
12. **Kill switch:** **none** (normal deploy and rollback), or a runtime flag in `client-config`.

**Rough size:** Part A medium (about one agent day); Part B large in content (~1,400 strings + ~80 seed texts) but mechanical, with the user's review (2–4 h over 4 commits) as the critical path; Part C medium. Overall smaller than M9; no cloud work beyond one deploy.

## 10. Implementation notes & deviations

(Filled in during and after the offline parts.)

### Step 1 (backend-dotnet), implemented 2026-11-17

Done: `SupportedLanguages`, migration `Localisation` (both nullable `varchar(8)` columns), `MeResponse.language`, `PUT /api/me/language`, `POST /api/guest-sessions?lang=`, `DemoTextLocalizer` + language-aware `DemoDataSeeder`/`DemoTrainingPlan`, `GoalDto.plan.language`, OpenAPI + `schema.d.ts` regenerated, Domain.Tests and Api.Tests. Deviations and decisions:

1. **File location:** the Me endpoints live in the existing `Features/Account/MeEndpoints.cs` (not `Features/Me/*`); the new route and `UpdateMyLanguageRequest` are in that file.
2. **Types:** `language` is a plain `string` in the OpenAPI document (`string` / `string | null` in TS), not an enum, because the API's enums serialise as PascalCase names; the allowed values are enforced by validation (`en`, `pl`) and documented. The SPA narrows to its `Language` type.
3. **Errors:** a bad `PUT /api/me/language` body is a validation problem with `errors.language`; a bad guest `?lang` is a validation problem with `errors.lang` (checked before the guest limiters and any database work; the value is never echoed). A missing or null `language` in the body is also 400.
4. **`GoalDto.plan.language`** (a `required string`, `PlanLanguage ?? "en"`) and `GoalPlanRecord.Language` (optional trailing parameter, validated against the list) are added now, because the column exists; `TrainingPlanService` does not pass a language yet (stays null = English until step C3). The demo plan sets it from the guest's language.
5. **Demo seed map:** required texts are run `notes`, `gear`, `weather`, rating `comment`, and the goal title, plan notes and session descriptions. Run **titles** are mostly race names (proper names), so they are translated only when the map has an entry and are not required. The demo plan fact sheet is built with the guest's language tag.
6. **Fallback and reporting:** a missing map file or text keeps English; seeding logs one warning per guest ("N demo texts have no translation") and never fails. `DemoTrainingPlanStartupCheck` validates the translated plan for every supported language, fails the host on a malformed `i18n/<lang>.json`, logs incomplete maps, and fails the host for incomplete maps only when the new option `Guests:RequireCompleteDemoTranslations` is true (default false). **Step B4 must turn it on** (set the default to true) in the commit that adds `seed/demo/i18n/pl.json`.
7. **Packaging (B4):** `src/RunnersDiary.Api/RunnersDiary.Api.csproj` was not touched (it has a local `UserSecretsId` change). Step B4 must add `<Content Include="..\..\seed\demo\i18n\*.json" Link="seed\demo\i18n\%(Filename)%(Extension)" CopyToOutputDirectory="PreserveNewest" CopyToPublishDirectory="PreserveNewest" />` so the map is copied next to the binaries (and the API image).
8. **Test fixture:** the Polish seeding test uses a generated map (`"PL " + text`) in a temp directory; no Polish translations are committed.

### Step 7 / B4 (backend-dotnet), implemented 2026-11-17

Done: `seed/demo/i18n/pl.json` (48 required texts: notes, gear, weather, rating comments, goal title, plan notes, session descriptions; plus the optional title "Labedy Half Marathon"; the other race titles are proper names and stay English). `Guests:RequireCompleteDemoTranslations` now defaults to true (`GuestOptions`), so an incomplete map fails the host at startup. The csproj gets the `seed\demo\i18n\*.json` Content line (the Dockerfile already copies all of `seed/demo`, no change). Tests: `Guests/DemoPolishMapTests` (committed map is complete, every text translated, numbers equal to the English source) and the guest endpoint test in `GuestLanguageTests` now checks the committed Polish texts end to end. Deviations: none; no test relied on the old default (false).

### Step 9 / C1 (ai-insights), implemented 2026-11-17

Done: `Insights/Narrative/Languages/{EnglishNumberWords, PolishNumberWords, NarrativeLanguageRules}.cs`; `SummaryValidationPolicy` and `PlanValidationPolicy` gain `Language` (default `en`), `LanguageDenyLists` (default: the built-in Polish list) and `ForLanguage(lang)`; the summary and plan validators use one shared `DenyMatcher`; Domain.Tests `Insights/Narrative/PolishNarrativeRulesTests.cs` (112 cases). Nothing calls `ForLanguage("pl")` yet (C2/C3 wire the manifests and the request language). Decisions and deviations:

1. **Stems vs exact forms, read from ADR 0043 §3:** the ADR asks for exact inflected forms for **number words** and a stem-style **deny list** (`bol*` …). No contradiction: the "exact forms, not stems" sentence was about number words. Implemented as specified, with one change: deny stems that collide with ordinary or running Polish became exact forms or phrases (item 3).
2. **Number words (`literalNumber`) for `pl`:** words are folded (lower case, diacritics removed, `ł` → `l`, via `Races/TextFolding`; also catches NFD-decomposed text) and compared with exact forms: cardinals 2+ in all cases (incl. `-naście`, `-dzieści`/`-dziesiąt`, hundreds, tysiąc/milion/miliard/tuzin); kilkanaście/kilkadziesiąt/kilkaset (English bans "dozens"/"hundreds"); półtora/półtorej; collectives (dwoje, czworga…); number nouns (dwójka…dziesiątka, setka, dycha; for piątka only the forms that are not also the plural of "piątek"); ordinals pierwszy…dwudziesty, the tens ordinals, setny, tysięczny in every gender and case; podwójnie/potrójnie/poczwórnie. **Number compounds** (not in the ADR): a numeral combining form (dwu, trzy, cztero, pięcio … stu, tysiąc, półtora) directly followed by a closed list of stems (krotn, dniow, tygodniow, miesięczn, roczn, letni, kilometrow, metrow, minutow, godzinn, sekundow, procentow, seryjn, etapow) catches "dwukrotnie", "trzytygodniowy", "dziesięciokilometrowy"; the closed list keeps "trzymaj" and "dwutlenek" allowed. Allowed: jeden/jedna/jedno, pół, połowa, półmaraton, kilka, oba, raz, piątek/piątki, czwartek, ten. Digits, `%`, Roman numerals: unchanged for every language.
3. **Deny list `pl`** (`NarrativeLanguages.DefaultPolishDenyList`; the C2 v3 manifests must mirror it as `denyList.pl`): ADR entries kept: `kontuzj*`, `uraz*`, `przetrenow*`, `diagnoz*`, `medyczn*`, `chorob*`, `chory`, `chora`, `sercow*`, `lekarz*`, `lekark*`, `fizjoterap*`, `bolesn*`, `naciagn*`, `nadwyrez*`, `sciegn*`, `peknieci*`, `ryzyko`, `ryzyka`. **Narrowed** (stem false positives): `bol*` → the forms of ból/boleć (`bol, bolu, bolem, bole, bolow, bolom, bolami, bolach, boli, bola, bolal, bolala, bolalo, bolaly, bolec`), because `bol*` hits "bolid", "Bolesławiec"; `zawal*` → the forms of zawał (`zawal, zawalu, zawalem, zawaly, zawalow, zawalom, zawalami, zawalach`), because the stem hits "zawalony tydzień"; `zapaleni*` → the forms of zapalenie (`zapalenie, zapalenia, zapaleniu, zapaleniem, zapalen, zapaleniom, zapaleniami, zapaleniach`), because the stem hits "zapaleni biegacze"/"zapaleniec"; `zlaman*` → the phrases `zlaman* zmeczeniow*`, `zlaman* kosc*`, because "złamanie {x}" means breaking a time barrier. **Added:** `zachorow*`, `chore`, `chorego`, `chorej`, `chorzy`, `lekarsk*`, `obolal*`, `sciegien`, `achilles*`, `pekniet*`, `ryzyku`, `ryzykiem`. To support the phrases, a `*` may now end any word of a deny entry (before: only the last); the English entries parse exactly as before. `uraz*` and `chory` were kept: their only other readings ("urazić", slang "chore tempo") do not appear in training narratives. The user reviews the list in Part C (C2).
4. **Deviation: number words for `pl` = the Polish forms ∪ the English words except "ten"** (ADR 0043 §3 says "only the request language's set, never the union"). The ADR's only reason against the union is the homograph "ten" (Polish "this"); no other English number word is a Polish word. Keeping the rest catches an English "two"/"third" inside a Polish draft (code-switching, the same reason the deny list is a union), which is a grounding defect in any language. The homograph list is `EnglishNumberWords.PolishHomographs`. English drafts get no Polish words.
5. **English byte-for-byte:** for `Language = en` (every existing caller and the v1/v2 manifests) the words, number-word sets and deny matching are unchanged and nothing is folded; all existing Domain.Tests and Ai.Tests pass unchanged.
6. **Language checks:** `NarrativeLanguages.English/Polish` are `SupportedLanguages` constants; `ForLanguage` and the validators throw for a language without rules. A policy built by the current catalogues gets the default Polish list, so a `pl` validation never runs without Polish terms even before the v3 manifests exist.

### Step 10 / C2 (ai-insights), implemented 2026-11-17 (Polish texts waiting for the user's review)

Done: prompt v3 for both features (`prompts/training-insights/v3.md`, `prompts/training-plan/v3.md`, `current: 3`), manifests with `denyList: { en, pl }` and `languages: ["en", "pl"]`, `templates/pl.json`, `demo/pl/12w-previous.v1.json`, Polish fake drafts (`FakeSummaryTexts`, `FakePlanTexts`), Polish eval cases in CI mode, live evals prepared per language (not run: gate G-L1). Review table: `.i18n-review/C2-en-pl.md` (git-excluded). Decisions and deviations:

1. **Minimal seam (the only wiring in C2):** the validators now use `Prompt.Policy.ForLanguage(sheet.Language)` in `SummaryGenerator`, `TrainingPlanGenerator`, `AiStartupCheck` and `InsightsSummaryService` (demo fixtures). Because the summary manifest lists `pl`, the API accepts `lang=pl` for summaries from this step on (ADR 0043 §4: the language goes into the manifest in the same commit as the templates and fixtures), and a Polish summary is validated with the Polish rules. Nothing else changes in production: the SPA still sends `en` (`AI_LANGUAGES = ['en']`) and `TrainingPlanService.Language` is still `en` (both C3).
2. **Manifest format:** a v3 `denyList` is an object per language; a flat array (v1/v2) stays the English list with the built-in Polish list for `pl`. Loading fails when the object has no `en`, names a language without validator rules, when `languages` is empty, has a duplicate or a language without rules, or when the current version has no deny list for a listed non-English language. `denyList.pl` equals C1's `DefaultPolishDenyList` (test), and the plan and summary manifests carry the same lists (test).
3. **English equivalence:** v3 changes the English prompt only in wording that names the language ("Address the runner directly", "Write every text in the language named in the user message (`en` English, `pl` Polish)…", the `display` strings are for the model only, "Example … in English"; plan: "plain text in the plan's language" instead of "plain English"). Tests check that every rule line of v2 is still in v3's English part. All English eval cases, templates, the English demo fixture and the English fake are unchanged and pass. Cached summaries written with v2 turn stale after deploy (ADR 0028 §2, as with every prompt version).
4. **Polish rules beyond ADR 0043 §2** (found while writing the fixtures): never a counted noun right after a count placeholder ("liczba treningów: {x}", not "{x} treningów", because the noun form depends on the number); no noun-like placeholder at the start of a sentence (race distances render in lower case); signed changes (`+12%`) after a colon; no number placeholder set off by commas ("wskaźnik, 1,56 w dniu …," collides with the decimal comma). Run types and bands render capitalised ("Bieg trailowy", "Wysoki"), so they go after a colon, in parentheses or in quotes.
5. **Templates:** the same variants and predicates as English; the counts are 1 or 2, so "1 wykonany trening" / "2 wykonane treningi" are fixed per variant (no new predicate).
6. **Eval cases:** Polish drafts live in `tests/RunnersDiary.Ai.Tests/Evals/cases/<case>/pl/` (a good draft per model case; adversarial `literalNumber` drafts: cardinal, genitive, ordinal, multiplicative, compound, without diacritics, code-switched English; `forbiddenTopic`: ból, without diacritics, lekarz, ryzyko, ścięgna, code-switched English; the known limit "przed {week.6.raceType}" that the validator accepts; an English draft for `pl`). The Polish fact sheets are the English golden files with `language: pl` (test). A test shows that the Polish adversarial drafts pass the English rules, so only the Polish rules catch them. **Language check:** `EvalAssertions` fails a `pl` case whose texts contain at least 2 distinct English function words (`Support/LanguageCheck.cs`): a prompt-quality metric for the live eval, not a validator rule.
7. **Live evals (G-L1, not run):** `LiveEvalTests` and `PlanLiveEvalTests` run every case in `en` and `pl` (`RD_AI_LIVE_LANGUAGES=pl` limits it); the plan live eval sets the fact sheet's `language`, prints the notes and a few descriptions for the human read, and fails a Polish run whose texts read as English. The pass assertion is unchanged (every run of a case passes), stricter than G-L1's "≥ 2/3 per case"; the printed counts give the gate's numbers.
8. **Tests changed outside Ai.Tests:** Api.Tests `InsightsSummaryEndpointTests` (`lang=pl` is valid now, so the invalid-value cases use `de`; prompt version 3) and `GenerateTrainingPlanTests` (prompt version 3). No API code changed; OpenAPI unchanged.
9. **Left for C3:** `DemoTrainingPlan` validates the demo plan with `PlanValidationPolicy.Default` (English rules) for every language; it should use `.ForLanguage(sheet.Language)`. The OpenAPI description of `GET /api/insights/summary` still says "lang (en)". `TrainingPlanService.Language`, the plan `lang` and the SPA side are C3 as planned.

### Step 3 (frontend-react), implemented 2026-11-17

Done: language module (`web/src/i18n/language.ts`), `initLanguage` before the first render, header and sign-in switcher, profile sync, `Accept-Language`, MSAL `ui_locales`, guest `?lang`, `capitalizeFirst`, localised km input, generic field message, bundle budget, test-only missing-key detection, parity test, fake API (`me.language`, `PUT /api/me/language`, guest `?lang`, `GoalDto.plan.language`), `pl/common.json`. `pl` is **not released** (`RELEASED_LANGUAGES = ['en']`; `releasedLanguages()` adds `pl` in dev and tests only), so the production build shows no switcher and detects English only. Initial load: 188,934 B before, 190,617 B after (+1.7 kB gzip); `i18n-pl-eager` is a lazy chunk and is in `LAZY_ONLY`. Deviations and decisions:

1. **`setLanguage` has no `persistToProfile` option.** `language.ts` must not depend on the API client, so `setLanguage(lng)` is local only (bundle, `changeLanguage`, `<html lang>`, `rd.lang`). The profile save is `useChangeLanguage()` in `web/src/auth/useLanguage.ts` (fire-and-forget `PUT /api/me/language` only with a guest or Entra session, then `/api/me` is invalidated). `useLanguageSync()` (same file, called in `AppShell`) applies `me.language` once per account and page load, so a later switch whose save failed is not undone by a refetch. A null profile language changes nothing and is not written.
2. **No top-level await in `main.tsx`.** TLA in the entry changed Rolldown's chunking and cost about 7 kB gzip of initial load. `main.tsx` uses `async function start()` instead; behaviour is the same (render after `initLanguage`, English on failure).
3. **Switcher shape:** a native `<select>` (shadcn `NativeSelect`) with a globe icon, in the header and at the top of the sign-in page (not directly above "Try as guest"). It shows the full names ("English", "Polski") on every width: the "EN/PL on phones" variant is not done (a native select shows the option text; to be revisited with the 360 px layout work in B5). It renders nothing while only one language is released, so production is unchanged. Option names are keys (`common:language.names.*`, endonyms, in the parity allowlist).
4. **Eager and lazy Polish bundles use `import.meta.glob`** (`i18n-pl-eager.ts`, `lazyBundles.ts`) over the expected file names, so Part B only adds JSON files and changes no code. `ensureLanguage` loads `i18n-pl-eager` on demand.
5. **Missing-key detection (test only, `web/src/test/missingKeys.ts`, installed in `setup.ts`):** i18next never calls `missingKeyHandler` when the fallback language supplies the key, so tests set `fallbackLng` to false at runtime and record misses; `setup.ts` fails the test afterwards. A namespace without a `pl` file is ignored (Part B work still to come; its keys render as the key in a Polish test). Once `pl/<ns>.json` exists, every gap in it fails. The language is reset to `en` before each test.
6. **`Accept-Language`** is set to `i18n.language` on every `/api` call (anonymous ones too) in the auth middleware.
7. **Generic field message:** `mapFieldErrors` takes `genericMessage`; `genericFieldMessage()` (`lib/api/fieldMessage.ts`) returns it for any non-English UI. Applied to the run form, goal form and race interests form (the race signup form has no free-text server field errors).
8. **`formatKmInput(meters, locale = 'en')`** uses `Intl.NumberFormat` without grouping (max 3 decimals). The run form's "untouched number keeps the loaded value" check now compares with `,` and `.` normalised, so switching the language while a form is open does not change a stored value.
9. **`capitalizeFirst`** is applied to the month title and picker trigger/month buttons, the Day panel title and the proposals day headings. Running text (announcements, `aria-label`s, list titles) keeps the lower case. `Intl.Collator`: no list in the SPA is sorted by a translated label today (zones are IANA ids, the rest sort by date or number), so nothing needed changing.
10. **`AI_LANGUAGES = ['en']`** and `aiLanguage()` exist but are not wired into `summaryQueries`/`PlanSection` (Part C3). `plan.languageHint` is also C3.
11. **E2E:** the `desktop-pl`/`phone-pl` projects are B5 (they need a released `pl` in the preview build). The only e2e change: the guest-session mock in `smoke.spec.ts` matches `?lang=` and asserts `lang=en`.
12. `web/src/components/map/` was not touched. `AzureMapsMap.test.tsx` renders `pl` with an untranslated `maps` namespace; it passes because of item 5.

### Step 8 / B5 (frontend-react), implemented 2026-11-17

Done: `pl` released (`RELEASED_LANGUAGES = ['en', 'pl']`, so detection and the switcher work in the production build; `releasedLanguages(dev)` still returns every supported language in dev and tests, today the same list; `AI_LANGUAGES` stays `['en']` until C3). Compact switcher on phones. Playwright projects `desktop-pl` and `phone-pl` with `e2e/polish.spec.ts`. Full-stack `e2e-fullstack/polish.spec.ts`. Deviations and decisions:

1. **Switcher on phones:** still the native select (keyboard and screen readers unchanged). Below `sm` its own text is transparent and an `aria-hidden` "EN"/"PL" sits on top (width 4.75 rem, 44 px high); the accessible name, the option names and the open list stay "Language" / "English" / "Polski". In forced-colors mode the code is hidden and the full name shown. Not done: a custom listbox (more code, worse a11y for no gain).
2. **New spec instead of re-running the English specs:** the existing specs assert English strings, so `desktop-pl` and `phone-pl` (locale `pl-PL`, zone Europe/Warsaw; phone = 360x640 touch, the same width as `phone-la`) run only `e2e/polish.spec.ts` (the `chromium` project ignores it). It covers: sign-in and guest start (`?lang=pl`), switching en/pl without reload, diary (grid, month list, run form), insights, race interests, goals (list, detail with a generated plan, new goal), diary with race proposals and suggestions plus race details. Each screen asserts `<html lang="pl">`, no raw keys (namespace prefix or dotted camelCase path), none of a short list of English UI words, and no horizontal overflow. The production build has no missing-key handler (English is the silent fallback), so a gap is caught only if it is one of those words; the Vitest missing-key detection (step 3) is the strict check.
3. **Layout at 360 px:** no overflow or truncation was found on any of those screens (checked with screenshots of the header, sign-in, diary, insights, goals form). No CSS change besides the switcher.
4. **Full-stack journey** (`e2e-fullstack/polish.spec.ts`, runs on `main`): Polish guest (`POST /api/guest-sessions?lang=pl`), `/api/me.language = pl`, the seeded race's rating comment and the demo goal title and plan text are the Polish demo texts, switching to English saves `PUT /api/me/language` (204) and survives a reload. Written against the existing full-stack specs but **not run locally** (no stack was up); the first CI run on `main` is the check.

## Assumptions
1. The user is a Polish speaker and will review the Polish texts. Claude's drafts are not merged unreviewed.
2. `pl` and `en` only; tags without region (`pl` formats like `pl-PL`, `en` stays US-style as today).
3. Monday-first ISO weeks and metric units are already right for Polish; no calendar logic changes.
4. The running.life catalogue stays as imported (Polish names); race kinds and regions are already i18n keys.
5. Entra (G8) is not live yet. `ui_locales` is wired now, and the tenant's Polish page customisation becomes a G8 checklist item for the user.
6. Node 26 (Vitest) and Chromium ship full ICU with `pl` data.
7. i18next stays on 26.x; an upgrade to ≥ 27.1 would only soften the missing-plural fallback, and the parity test makes it irrelevant.

### Step 2 (maps-gpx), implemented 2026-11-17

Done: geocoder language, Azure Maps display language. Deviations and decisions:

1. **Provider call:** Azure Maps Get Geocoding (`2026-01-01`) takes the language in the `Accept-Language` request header (no `language` query parameter); verified against the current REST reference. `AzureMapsGeocoder` sends only `en` or `pl` (value passed through `SupportedLanguages.OrDefault`), never client text.
2. **Interface:** `IGeocoder.SearchAsync` gained a required `string language` parameter (before the cancellation token). `FakeGeocoder` records `LastLanguage`.
3. **Parsing:** new pure `Domain/Localization/AcceptLanguage.Reduce` (bounded to 256 chars and 16 entries, honours `q`, ignores `q=0`, region subtags dropped, default `en`). It lives in Domain, not `Features/Geocoding`, so other endpoints can reuse it. `SearchPlaces` reads the header, reduces it and answers with `Vary: Accept-Language`. The header is not declared as an OpenAPI parameter (the SPA's `Accept-Language` is added globally by the client); only the operation summary changed, so `openapi/runnersdiary-api.json` and `schema.d.ts` were regenerated (comment-only diff).
4. **Caching:** there is no server-side geocoder result cache today (only the daily `GeocodeBudget` counter), so nothing to key. Any future cache must key on the reduced language (noted here).
5. **Azure Maps display:** `AzureMapsMap` passes `language` (`pl-PL` for `pl`, else `en-US`, helper `azure/azureLanguage.ts`) read from `i18n.language` (the language module in `web/src/i18n/language.ts` has no hook; i18next is the source of truth). The map is rebuilt on a language switch (the option is read at construction).
6. **MapLibre/OpenFreeMap:** no label-language change, as ADR 0042 section 7 says (labels are local names already). Its control strings already use the `maps` namespace, so Polish comes with step B2.
