# 0035 – Race catalogue sources, import strategy and the legal/crawling policy

**Status:** Accepted (M8 plan approved 2026-11-13; amended 2026-11-13 after the importer implementation, see the end)
**Date:** 2026-11-13
**Related:** [M8 plan](../plans/M8-race-proposals.md), [0036](0036-race-catalogue-model-and-race-runs.md) (data model), [0038](0038-race-import-job.md) (the job), [0033](0033-production-maps-and-azure-maps.md) (geocoding budget)

## Context

M8 proposes upcoming races to a runner, based on their interests. No public, documented, free race-calendar API covers Poland. The source research (2026-11-13: live requests with an identifying User-Agent, plus web search) found the following:

| Source | Coverage | Access | Terms / robots | Verdict |
|---|---|---|---|---|
| **running.life** (same operator: gotrail.run, walking.life) | ≈ 600 upcoming Polish events (≈ 30 listing pages × 20); śląskie ≈ 70; road, trail and mountain; 45k events worldwide | Server-rendered HTML. Each listing page has a schema.org **JSON-LD `ItemList` of `SportsEvent`**: name, url, `startDate` (date only), locality, region (the English voivodeship name), country and **geo lat/lng**. The detail page adds distances, a type (city / trail / mountain), the organiser's website and the registration URL. | robots.txt disallows only `/<lang>/map/` and `/demo-*` (re-checked 2026-11-13). **No terms page**; the footer says "© running.life – All Rights Reserved". Content is community-edited. | **Primary** |
| **kalendarzbiegowy.pl** (bieganie.pl / Fundacja "Maraton Warszawski") | **2,202 events**, all 16 voivodeships (śląskie 308) | Public WordPress REST `/wp-json/wp/v2/ajde_events` (`modified_after`, `_fields`, `X-WP-Total`), with taxonomies for distance, kind and voivodeship. **The date is not in REST**: it is only in each event page's JSON-LD. There are no coordinates, only a free-text address. | robots.txt allows everything. No terms, only a privacy policy. | **Secondary, phase 2** (needs 2.2k page fetches once, plus geocoding) |
| AIMS calendar (`aims-worldrunning.org/events.ics`) | 426 world events, 4 in Poland | Official public iCal offered for subscription | © AIMS. `ORGANIZER` is a person's name (never stored). | Optional international source (Q6) |
| treningbiegacza.pl | 1,157 races | Headless WordPress REST on an `api.` host | robots `Disallow: /api/`; no terms; `Content-Signal ai-train=no` | Not used (internal API of a commercial site) |
| elektronicznezapisy.pl | 66 small events | HTML table | Terms: all rights to the operator, use only with consent | Not used (low coverage) |
| ahotu.com | 58k events, 1,156 in Poland | Commercial GraphQL API, contact-only pricing; site behind a bot challenge | Commercial | Not in M8 (Q6: ask for partner access only if international races are wanted) |

**Excluded, and the importer must never request them:**
- **maratonypolskie.pl**: robots.txt is `User-agent: * / Disallow: /`, and its terms forbid copying.
- **enduhub.com**: its terms forbid using its databases.
- **dostartu.pl**: undocumented API; robots disallows it.
- **b4sportonline.pl, datasport.pl**: no API.
- **World Athletics** (hidden GraphQL), **ITRA** (bot protection) and **UTMB** (no API).
- **finishers.com, trainerday, hdsports, intervals.icu**: same or weaker legal footing, and no benefit.

**Legal background (not legal advice):**
- Polish law gives databases a sui generis right (Act of 27 July 2001 on the protection of databases), and descriptions are protected by copyright. Facts as such (a date, a place, a distance) are not protected, but repeated, systematic extraction of a substantial part of a database can infringe the maker's right.
- GDPR covers personal data such as an organiser's personal name, e-mail or phone number.

## Decision

1. **Sources in M8.**
   - **One real source: running.life**, Poland only (the Polish listing), behind its own enable flag. It is **off until the operator has answered our permission request** (step 3).
   - **A synthetic `Demo` source** (`seed/races/demo-races.json`, dates relative to today) for local development, tests and CI. It is never enabled in Production.
   - kalendarzbiegowy.pl is the planned second source. The adapter interface and the dedup rule ([0036](0036-race-catalogue-model-and-race-runs.md) §4) are ready for it, but it is **not built in M8**.
   - AIMS iCal and ahotu are decided by the user (plan Q6).
2. **Facts only.** The importer stores only facts:
   - name, date, optional start time;
   - place (locality and region), coordinates, country;
   - distances, type/surface, elevation gain;
   - price and currency, signup deadline;
   - the organiser's **organisation name**;
   - three URLs: signup, event/organiser website, and the source page.

   **It never stores:**
   - descriptions or any other prose;
   - images or logos;
   - reviews or results;
   - personal names, e-mail addresses or phone numbers.

   The parser drops these fields before anything is persisted. Domain validation rejects an `OrganizerName` that looks like an e-mail address or a phone number (null instead).
3. **Permission and attribution.**
   - Before the Production flag is turned on, the user e-mails each source operator through **the site's contact page**. The plan gives the text outline: who we are, what we take (facts listed above), how often (weekly, ≤ 1 req/s, about 30–100 requests), attribution and link-back, and how to opt out.
   - A refusal, or no answer after 30 days, means the source stays off (plan Q1). Permission is recorded in the plan's deviations section, never with private addresses.
   - **Attribution:**
     - every race shows "Source: <site>", linking to the race's `sourceUrl`;
     - the Race interests page lists the sources with links;
     - `client-config.races.sources` carries the names and URLs.
4. **Polite crawling** (enforced in code, `Infrastructure/RaceSources/PoliteHttpClient`, unit-tested):
   - **Identification:** User-Agent `RunnersDiaryRaceImport/1.0 (+https://github.com/stompsu/runners-diary)`; the project page serves as the contact URL.
   - **robots.txt** (RFC 9309 subset):
     - it is fetched at the start of every run;
     - groups for `*` and for our product token are honoured, with Allow/Disallow resolved by longest match;
     - a 4xx answer means "allow all", per the RFC;
     - 5xx, a timeout or an unparsable file **aborts that source for the run**;
     - a newly disallowed path aborts the run with an error log.
   - **`Content-Signal`** headers and robots lines are logged. `ai-train=no` and `ai-input=no` are recorded per source; race facts never feed the AI in any case (plan Q8).
   - **Request policy:**
     - one request at a time, at most one per `MinInterval` (default **1.5 s**; configuration may not go below 1 s);
     - only GET, HTTPS only, timeout 20 s, response body cap 2 MB;
     - redirects only to the same host;
     - `Accept-Encoding: gzip, br`.
   - **Host allow-list per source.** Detail URLs taken from listing JSON-LD must be `https` on that host, otherwise they are skipped (no SSRF via source content). Signup and event URLs are stored, never fetched.
   - **Conditional requests:** detail pages send the stored `ETag` / `Last-Modified` (`If-None-Match` / `If-Modified-Since`); a 304 keeps the stored facts.
   - **Fetch budget per run:**
     - listing pages: every run, capped at `MaxListingPages` (default 40);
     - detail pages: only for new races, races whose listing facts changed, and races not refreshed in 28 days, capped at `MaxDetailPages` (default 150, so a backfill spreads over runs).
   - **Stop rules:**
     - HTTP 403 or 429: stop the source for this run (no retry; `Retry-After` is logged);
     - 3 consecutive 5xx or timeouts: stop;
     - a 200 listing page that yields **zero** events is treated as a layout change: stop, and exit code 1 so the failure alert fires.
5. **Kill switches** (configuration, no redeploy; each change is a new job revision):
   - `RaceImport:Enabled` (global, default `false`);
   - `RaceImport:Sources:<Id>:Enabled` (default `false`).

   When either is off, the job exits 0 **without opening a SQL connection**. A source switched off later keeps its rows, which are soft-removed by the normal disappearance rule only when it runs again. The user can purge them on request (runbook).
6. **Parsing.**
   - Listing and detail pages are parsed with **AngleSharp** (one new NuGet package in `RunnersDiary.Infrastructure` only).
   - **Justification:** a maintained, MIT-licensed HTML5-spec parser with no native code and no network access by default. Untrusted HTML must not be parsed with regular expressions, and the detail facts (distance chips, type, registration link) are not all in JSON-LD.
   - JSON-LD blocks are read from `script[type="application/ld+json"]` and deserialized with `System.Text.Json`, depth ≤ 32, as untrusted input. AngleSharp is used **without** loaders (no scripts, no CSS, no resource fetching).
   - Normalisation is pure Domain code: distance text → metres, type text → `RaceKind`, English voivodeship name → ISO 3166-2 code.
7. **Test fixtures are synthetic.** The HTML/JSON-LD fixtures under `tests/fixtures/races/` imitate the structure of the source pages but contain invented events. They are never copies of the sites' pages, because the repository is public.

## Consequences

- One source with coordinates means **no geocoding in M8**, and no load on the API's daily geocode budget (ADR 0033).
- Coverage is limited to what running.life lists (≈ 600 upcoming Polish events). kalendarzbiegowy roughly triples it later, at the cost of geocoding and an initial backfill.
- A layout change at the source stops the import until the parser is fixed. Proposals then go stale but do not break (`catalogueUpdatedUtc` is shown in the UI).
- The feature has a hard dependency on a third party's goodwill. The design keeps that cheap to lose: one flag off, and no other code depends on the source.
- AngleSharp adds about 1 MB to the API image, which the job reuses ([0038](0038-race-import-job.md)).

## Alternatives considered

- **kalendarzbiegowy.pl as primary:** better coverage and clean taxonomies, but no date in REST (2.2k page fetches for the first backfill), no coordinates (geocoding beyond the free budget in the first weeks), and the same "no terms" footing. Kept as phase 2.
- **ahotu commercial API:** a licensed path, but unknown cost, and it is international-first. Not for a near-zero-budget side project unless the user wants international races.
- **maratonypolskie.pl / enduhub:** excluded by robots.txt and terms; not negotiable without written permission.
- **Regex + `System.Text.Json` only (no HTML parser):** no dependency, but a regex over untrusted HTML is brittle and easy to get wrong. It is acceptable only for the `ld+json` blocks, and the detail facts need real parsing.
- **User-submitted races only (no import):** no legal question, but an empty catalogue. The user can still add any race by hand as a planned race run today.
- **Daily import:** fresher data, but ≈ 14–27 % of the SQL free allowance (ADR 0032) and seven times the load on a volunteer-run site. Rejected; weekly is the default, and the cron is configurable.

## Amendment (2026-11-13, implementation)

The decision stands. As built in `Infrastructure/RaceSources/` (commit `c4dbc61`; plan §12 D1–D6):
1. **Parser (§6).** **AngleSharp 1.10.2** (central pin, Infrastructure only), used without loaders.
2. **robots.txt (§4).** A **403 or 429** on robots.txt is treated as the normal stop rule (the source stops for this run, exit 0), not as RFC 9309's "4xx = allow all". This is deliberately more conservative than the RFC. Other 4xx still mean allow all; 5xx, a timeout or an unparsable file abort the source (exit 1).
3. **Detail layout guard (§4).** A 200 detail page with no recognisable fact (no distances, signup or website link, or type) is **ignored** for a race that already has stored facts, which are kept. This is the detail-page counterpart of the zero-events listing rule; for a new race the listing facts are used.
4. **Race kind.** The listing JSON-LD carries no type, so the kind comes from the **listing card's type anchor** (`.event-card-distances a[class^="label-"]`, the Polish label text first, the `label-*` class as a fallback). A detail-page `[data-race-type]` label overrides it; guessing from the name and distance chips is the last resort. Cards of non-running sports (**triathlon, duathlon, aquathlon, fitness, multisport**, incl. Polish spellings) are skipped and never imported.
5. **HTTP stack (§4).** `PoliteHttpClient` wraps a plain `HttpClient` (`AllowAutoRedirect=false`, gzip/brotli decompression), **not** an `IHttpClientFactory` client with the standard resilience handler: that handler would retry (the policy forbids retries) and the default client logging writes full URLs. Redirects are followed by hand (≤ 3 hops, https, same host); the 2 MB cap applies to the **decompressed** body; logs carry the path only, never the query string or the body.
6. **Spike (§7).** The selector spike (1 listing + 2 detail pages, fetched once with the project UA) pinned the selectors; the pages were **not committed**. The fixtures in `tests/fixtures/races/` are synthetic.

## References (checked 2026-11-13)

- Source research note (live checks 2026-11-13), summarised in [M8 plan §1](../plans/M8-race-proposals.md#1-source-research-summary).
- running.life robots.txt (re-fetched 2026-11-13: only `/<lang>/map/` and `/demo-*` disallowed): https://running.life/robots.txt
- RFC 9309 Robots Exclusion Protocol: https://www.rfc-editor.org/rfc/rfc9309
- Nominatim usage policy (1 req/s, identifying UA, periodic scripts ≤ 4 req/min, caching, attribution; read 2026-11-13): https://operations.osmfoundation.org/policies/nominatim/
- schema.org `SportsEvent`: https://schema.org/SportsEvent
- AngleSharp: https://github.com/AngleSharp/AngleSharp (⚠ the current version and its maintenance status are checked when the package is added)
