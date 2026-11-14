# Demo race catalogue

`demo-races.json` is the synthetic race catalogue used in Development and Testing (M8, ADR 0036). `DemoRaceCatalogue` upserts it as source `Demo` at startup when `Races:DemoCatalogue:SeedOnStartup=true` (set by the AppHost and the test host). The Production configuration validator rejects that setting, so these rows never reach Azure.

## Provenance

- Every race is **invented**. Names start with "Demo", the organiser is "Demo Running Club", there is no `sourceUrl` and every `signupUrl` is `https://example.org/demo-signup`. No real event, organiser, price list or third-party text is used.
- Towns and their centre coordinates are real places (a town centre is a fact, not a copyrighted work); the races do not take place there.

## Shape

- `timeZoneId`: the zone in which the relative dates are resolved ("today" and the ISO week come from it).
- `races[]`: `id` (becomes `ExternalId`, unique, <= 100 characters), `name`, `date` as `{ "week": n, "day": "Mon".."Sun" }`, optional `startTime` (`HH:mm`), `locationName`, `latitude`/`longitude`, `countryCode`, `regionCode` (ISO 3166-2), `kind` (`Road`, `Trail`, `Mountain`, `Ultra`, `CrossCountry`, `Ocr`, `Other`), `surface` (`Road`, `Trail`, `Track`, `Treadmill`, `Mixed`), `distancesMeters[]`, `elevationGainMeters`, `priceAmount` + `priceCurrency`, `signupUrl`, `sourceUrl` (null), `organizerName`.
- `week` is an offset from the current ISO week (Monday-first): -2 to +24 here, so the catalogue always has past races and a half-year of future ones.
- Coverage: 25 races in the Silesian voivodeship (`PL-24`: Gliwice, Katowice, Bielsko-Biala, Rybnik, Zabrze, Tychy, Ustron, Czestochowa), 15 elsewhere; all kinds; one race with three distances; one ultra (50 km, 70 km); two OCR races.
- Because the dates are relative, a restart in a later week moves every race (the seeder records it as an ordinary date change). That is acceptable for Development data.

## Validation

At startup `DemoRaceCatalogue` fails the host when the file is missing or malformed, a race does not normalise through `RaceFacts`, or fewer than 10 future `PL-24` races remain for "today" (the guest demo profile needs them).
