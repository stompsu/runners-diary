# 0042 – UI localisation: supported languages, language resolution and persistence, bundles, formatting, translated content

**Status:** Accepted 2026-11-17 (M10 plan approved with defaults)
**Date:** 2026-11-17
**Related:** [0003](0003-frontend-stack-and-ui-kit.md) (i18next, "Polish added later"), [0011](0011-guest-accounts-lifecycle.md) (guest seeding), [0013](0013-persistence-model-and-migrations.md), [0015](0015-spa-runtime-configuration.md), [0017](0017-client-dates-without-a-date-library.md) (`Intl` labels), [0018](0018-run-forms-and-server-errors.md) (server errors in forms), [0019](0019-frontend-test-strategy.md), [0022](0022-map-provider-geocoding-and-azure-maps-tokens.md) / [0033](0033-production-maps-and-azure-maps.md) (geocoder), [0029](0029-web-tier-nginx-container.md) (CSP), [0043](0043-ai-outputs-in-polish.md) (AI language), [M10 plan](../plans/M10-polish-localisation.md), [i18n guide](../i18n.md)

## Context

Brief §1.2: "English first, structure strings for i18n (Polish next)". Since M1 every user-facing string is a typed key in `web/src/i18n/en/*.json` (11 namespaces, ~1,400 lines). Six are eager (`common`, `auth`, `calendar`, `runs`, `runTypes`, `errors`). Five are registered by the side-effect module `i18n/lazyBundles.ts` inside lazy chunks (`maps`, `tracks`, `insights`, `races`, `goals`). `lng: 'en'` is hard-coded.

What the code already does well (verified 2026-11-17):
- every date, number and unit goes through `Intl` with `i18n.language` (`lib/dates/plainDate.ts`, `lib/format/units.ts`, `insightsFormat.ts`, `summaryFormat.ts`, `raceFormat.ts`, chart formatters). There is no date library ([ADR 0017](0017-client-dates-without-a-date-library.md)). Weeks are ISO and Monday-first, which is also the Polish convention;
- run types, race kinds, regions, problem types and zod validation messages are i18n keys looked up by code, never server text;
- the AI summary already carries a `lang` (cache key, templates and demo fixtures per language; [ADR 0028](0028-summary-cache-quotas-and-demo-summaries.md) §1).

Gaps:
- `lng`, the summary `lang: 'en'` (`summaryQueries.ts`), the plan language (`TrainingPlanService.Language = "en"`) and `<html lang="en">` are hard-coded;
- 14 plural pairs use English `_one`/`_other` only;
- `formatKmInput` writes a `.` decimal into inputs (parsing already accepts `,`);
- server 400 field messages are English and are shown verbatim when the client did not catch the error first (ADR 0018);
- the guest demo diary (`seed/demo/runs.json` notes) and the demo plan (`seed/demo/training-plan.json`) are English;
- the geocoder sends no language.

## Decision

### 1. Languages

`SUPPORTED_LANGUAGES = ['en', 'pl']`; English is the fallback for every missing key (`fallbackLng: 'en'`). Language tags are bare ISO 639-1 codes (`en`, `pl`). They are passed to `Intl` as is: `pl` formats like `pl-PL`, and `en` keeps today's US-style output, so English users see no change. A language is **released** (offered by detection and the switcher) only when its parity test passes. During development `pl` is supported but not released (§8).

### 2. Resolving the language (SPA)

The SPA resolves the language **synchronously, before the first render**, in this order:
1. `localStorage['rd.lang']`, if it holds a released language. It is written on every explicit choice and every profile sync.
2. `navigator.languages`: the first entry whose primary subtag is a released language (`pl-PL` → `pl`; `['de-DE', 'pl']` → `pl`).
3. `en`.

Then `<html lang>` is set and the eager bundle of that language is ensured (§4). Only then does `main.tsx` call `createRoot().render()`, so the first paint is already in the right language. Inline scripts are not possible under the CSP `script-src 'self'` (ADR 0029), so this runs in the entry module and needs no CSP change.

After sign-in, `GET /api/me` returns `language`. If it is non-null and differs from the current language, the SPA switches and updates `rd.lang`. **The profile wins** because it is the latest explicit choice of that account. A visible switch can therefore happen once, on a new device.

### 3. Persistence

- **Every account (users and guests):** a nullable `Users.Language varchar(8)` (null = never chosen; values from the supported list), returned in `MeResponse.language`. It is set by `PUT /api/me/language { language }` → 204 (Auth, general limiter). This is a separate endpoint so that `PUT /api/me`, a full replace with required `timeZoneId`/`unitSystem` that the zone sync already calls, keeps its contract and old clients keep working.
- **Guests:** `POST /api/guest-sessions?lang=pl` (optional query; values from the supported list, anything else → 400) stores the language on the new guest **before seeding**, so the demo diary and the demo plan are seeded in it (§7). Switching later does not re-seed: seeded notes are the guest's data.
- **Signed-out pages** (sign-in) use `rd.lang` only.
- **Choosing a language in the switcher:** `rd.lang` is written, `i18n.changeLanguage` runs (after loading the bundle), `<html lang>` is updated, then `PUT /api/me/language` is called when there is a session. That call is fire-and-forget: a failure is logged and the local choice still applies.

### 4. Bundles and size

- **Eager namespaces:** English stays in the entry chunk. Polish eager namespaces live in one dynamic-import module `i18n/i18n-pl-eager.ts`, a lazy chunk loaded only when `pl` is resolved or chosen. English users download no Polish. The bundle budget's `LAZY_ONLY` gains `i18n-pl-eager`. Initial-load growth is limited to the resolver and the switcher, at most 2 kB gzip.
- **Lazy namespaces:** `lazyBundles.ts` registers **both** `en` and `pl` for `maps`, `tracks`, `insights`, `races` and `goals`. This keeps the synchronous side-effect pattern that the lazy UI relies on (48 files import `lazyBundles.ts`, tests included) (no Suspense, no async `t`). The price is that lazy chunks carry both languages (roughly +1 to 5 kB gzip each), which is acceptable because they are not part of the initial load.
- **Types:** `CustomTypeOptions.resources` stays `AllResources['en']`. Polish files are checked by the parity test (§9), not by TypeScript.

### 5. Switcher

A compact language select in the **header** (`AppShell`, next to `UserMenu`, visible to guests and users on every page) and on the **sign-in page**. Each option is labelled in its own language ("English", "Polski") with `lang` set on the option. The accessible name is `common:language.label`. Settings is not the place for it: Settings holds only race interests today, and it is hidden when races are off.

### 6. Formatting

- Every formatter keeps taking `i18n.language`. No new library.
- Headings built from `Intl` month or day names (month header, panel titles) go through a `capitalizeFirst(text, locale)` helper, because Polish `Intl` output is lower case ("listopad 2026", "sobota, 14 listopada 2026"). Running text keeps the lower case.
- Numbers in inputs: `formatKmInput` (and any numeric input value) uses the language's decimal separator. Parsers already accept `,` and `.`.
- Pace (`5:30 /km`), durations (`h:mm:ss`) and unit symbols (`km`, `m`, `AU`, `s/km`) stay language-neutral. This is ADR 0017 plus the existing `units.ts` rule.
- Collation: any list sorted by a translated label uses `Intl.Collator(i18n.language)`.
- Plurals: i18next v4 JSON suffixes from `Intl.PluralRules`. Polish needs `_one`, `_few`, `_many` and `_other`; `_other` is used for fractions. i18next 26 does **not** fall back to `_other` within a language (that arrived in v27.1), so a missing Polish form would show English. The parity test therefore requires all four.

### 7. Content beyond UI strings

| Content | Decision |
|---|---|
| Run type, race kind, region, intensity, phase and status names | Already i18n by code; translated in `pl/*.json`. |
| Problem-detail `title`/`detail` | Never shown; the SPA maps `type`/`code` to keys (unchanged). |
| Server 400 field messages (fallback when client validation missed) | Stay English on the server (no `Accept-Language` localisation of the API). In a non-English UI the SPA shows a localized generic field message (`errors:problem.fieldInvalid`) instead of the English text. |
| Demo diary notes, demo goal and plan texts | Translation map `seed/demo/i18n/pl.json` (`{ "<en text>": "<pl text>" }`), applied by `DemoDataSeeder`/`DemoTrainingPlan` when the guest's language is `pl`. A startup check and a test require every seeded English text to have an entry. Proper names (places, races, "Body Bar") are not translated. |
| Race catalogue | Facts from running.life are already Polish (names, places); unchanged and not translated. |
| OpenAPI enums | Codes, never displayed; unchanged. |
| Geocoder | The API forwards the caller's `Accept-Language` (reduced to a supported language) to Azure Maps Search, which supports `pl` ([Azure Maps localization](https://learn.microsoft.com/azure/azure-maps/supported-languages)). The SPA client sets `Accept-Language` to the UI language on `/api` calls. |
| Azure Maps display (optional SAS provider) | The `language` map option follows the UI language. MapLibre/OpenFreeMap labels are local names already. |
| Entra hosted pages (after G8) | MSAL login requests pass `extraQueryParameters: { ui_locales }`. Tenant-side Polish customisation is a user step in the G8 checklist ([Entra External ID: customize languages](https://learn.microsoft.com/entra/external-id/customers/how-to-customize-languages-customers)). |
| E-mail | None exists. |

### 8. Release switch

The released list is a constant in the SPA: `RELEASED_LANGUAGES`, plus `pl` when `import.meta.env.DEV` or in tests. `pl` is added to it in the commit that completes the translations (M10 Part B). This is not a runtime kill switch. Rollback is the normal image rollback, because a language is a code+content change, not a cloud feature, and costs nothing to run.

### 9. Quality gates in CI (Vitest, frontend job)

For every namespace, `en` and `pl`:
- identical key sets, after normalising plural groups;
- identical interpolation variable sets per key;
- a plural group in `en` (`_one`/`_other`) ⇔ `_one`/`_few`/`_many`/`_other` in `pl`;
- no empty values;
- no value identical to English, except an allowlist (brand, units, `ACWR`, `RPE`, `Body Bar`, `Fartlek`, `Ultra`, …).

A test-only i18next `missingKeyHandler` fails component tests that render `pl`. New features must ship `en` and `pl` keys in the same PR. The parity test enforces this, and the [i18n guide](../i18n.md) gives the process and glossary.

## Consequences

- A Polish speaker sees Polish from the first paint, on the sign-in page, in the demo diary and (with ADR 0043) in AI texts. English users see no change and download no Polish in the initial load.
- One additive migration (`Users.Language`) and two small contract additions (`MeResponse.language`, `PUT /api/me/language`, `lang` on guest creation). No new Azure resource and no new npm/NuGet package.
- Every future PR that adds strings needs Polish too. This costs some drafting and review on each PR, and the parity test makes it impossible to forget.
- Lazy chunks grow by their Polish strings for everyone.
- Server validation texts stay English. They are rare, because client validation mirrors the server, and in a Polish UI they are replaced by a generic message.

## Alternatives considered

- **`i18next-browser-languagedetector` plugin:** a new dependency for about 20 lines of logic. Its cookie/query sources are not wanted.
- **`i18next-http-backend`, JSON fetched at runtime:** async `t` everywhere, Suspense boundaries across the lazy UI, a network round trip before the first paint, and untyped JSON paths. Rejected.
- **Language only in `localStorage`, no profile field:** smallest, but a signed-in user would have to choose again on every device. The guest seed language would also need its own column or an extra seeding parameter. The nullable column costs one migration.
- **Language in `PUT /api/me`:** "absent = keep" is ambiguous with `null` in a full-replace PUT, and the zone sync would need to send it. Rejected for a dedicated endpoint.
- **Localising server messages via `Accept-Language`:** doubles the server's message catalogue for rare fallback texts.
- **Switcher only in Settings:** hidden while races are off, and needs a route visit. The header is always visible.
- **Lazy namespaces loaded per language with dynamic imports:** saves a few kB per lazy chunk for English users, at the cost of async registration in every lazy module. Can be revisited if chunk sizes matter.

## References

- i18next plurals (v4 suffixes from `Intl.PluralRules`; "since v27.1.0" same-language `_other` fallback): https://www.i18next.com/translation-function/plurals
- Azure Maps supported languages (`pl` for Search and Render, latest services): https://learn.microsoft.com/azure/azure-maps/supported-languages
- Entra External ID, language customisation for external tenants (`ui_locales` + `mkt`): https://learn.microsoft.com/entra/external-id/customers/how-to-customize-languages-customers
