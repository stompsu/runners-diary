# 0033 – Production map provider, Azure Maps geocoding and the SAS display option

**Status:** Accepted (M7 plan approved 2026-11-04; amended 2026-11-04 after Part A implementation, see the end; **§2 "per replica, in memory" is superseded by the amendment**)
**Date:** 2026-11-04
**Amends:** [ADR 0022](0022-map-provider-geocoding-and-azure-maps-tokens.md) §3, §6 and §7. Its decision that the production provider is chosen in M7 with cost data is made here.

## Context

The brief asks for Azure Maps (Gen2): the Web SDK for display and Search for geocoding, with no key in the browser and a `MapView` abstraction so the app can switch to MapLibre + OpenFreeMap if costs or limits become an issue. M4 built the seam with MapLibre + OpenFreeMap as the only provider and the `None` geocoder. M7 builds the deferred adapters: `AzureMapsGeocoder`, the `listSas` issuer with `GET /api/maps/token`, and `AzureMapsMap.tsx`.

Verified on 2026-11-04:
- **Gen2 free tier per month** ([transactions](https://learn.microsoft.com/azure/azure-maps/understanding-azure-maps-transactions), updated 2026-09-15; retail prices API, `westeurope`, meters effective 2023-06):
  - **Base Map Tiles:** 5,000 transactions, where **1 transaction = 15 tiles**, so about 75,000 tiles. After that, $4.50 per 1,000 transactions.
  - **Location Insights Search:** 5,000 transactions, 1 request = 1 transaction. After that, $4.50 per 1,000.
  - 429, 401, 403, 408, 5xx and CORS preflights are **not billed**.
- **SAS tokens** ([Maps auth](https://learn.microsoft.com/azure/azure-maps/azure-maps-authentication)):
  - `maxRatePerSecond` is roughly 1–500 per token, **per Azure location**; requests above the cap get 429 and are not billed.
  - At most 24 h lifetime; immutable.
  - Signed with the account keys through a UAMI.
  - **`disableLocalAuth: true` disables SAS.**
  - Use `eu.atlas.microsoft.com` together with `regions` to keep usage in Europe.
- **The cost bound of a SAS rate cap is weak.**
  - A token is shared by every caller, and any guest can get one, because guest creation is anonymous.
  - At even 10 requests/s sustained, the cap allows about 26 M tile requests a month, which is about 1.75 M transactions or **about $7,900 a month**.
  - The cap limits bursts. It does not limit a hobby budget.

## Decision

1. **Production display provider: MapLibre + OpenFreeMap** (`Maps__Provider=MapLibre`, unchanged from M4).
   - It costs €0, needs no token in the browser and has no billing risk.
   - It has no SLA. The fallback panel and the track tables already cover an outage.
   - **Privacy note** (UI + docs): the map's tile requests reveal the viewed area to OpenFreeMap (no cookies, no user id). The note is shown once under the map in the track viewer and the location picker as an i18n string, and it is listed in the docs.
2. **Production geocoding: Azure Maps Search via the API** (`Maps__Geocoding__Provider=AzureMaps`), as ADR 0022 §4 designed:
   - managed-identity Entra token (`https://atlas.microsoft.com/.default`) + `x-ms-client-id`;
   - `https://eu.atlas.microsoft.com/geocode?api-version=2026-01-01`, `top=5`.

   **New:** a **global daily geocode budget per replica**, `Maps:Geocoding:GlobalDailyLimit` (default **75**, fixed window reset at 00:00 UTC). It sits on top of the per-caller `geocode` policy.
   - With `api` max 2 replicas, at most about 4,560 calls a month, which is **below the free 5,000**.
   - When exhausted: 429 `urn:runnersdiary:problem:rate-limited` with `Retry-After` until reset.
   - Counters are in-memory, the same accepted semantics as the other limiters (0011). A restart resets them, but restarts happen only after idle.
3. **Azure Maps display is built but off** (Bicep `enableAzureMapsDisplay=false`, `Maps__Provider=MapLibre`).
   - Turning it on is a confirmation gate (plan G9). It creates the SAS UAMI, the custom roles and Maps CORS, and sets `Maps__Provider=AzureMaps`.
   - **SAS minting:**
     - `GET /api/maps/token` (Auth, rate policy `maps-token`: users 30 per hour, guests 10 per hour) returns `{ token, expiresAt }` with `private, no-store`.
     - **One shared, cached token** (lifetime 2 h, refreshed when < 30 min remain), `signingKey: primaryKey`, `principalId` = the SAS UAMI, `regions: ["westeurope", "northeurope"]`, `maxRatePerSecond` = `Maps:AzureMaps:SasMaxRatePerSecond` (default **10**).
   - **Client:**
     - `AzureMapsMap.tsx` (`azure-maps-control` 3.x, lazy chunk), `authType: "sas"`, `domain` from `client-config.maps.azureMapsDomain` (`eu.atlas.microsoft.com`).
     - The SPA falls back to MapLibre for the session if the token endpoint fails (0022 §5).
   - **Guardrail:** a metric alert on the Maps account (`Usage`, > 4,000 transactions per 24 h) plus the RG budget. The kill switch is `Maps__Provider=MapLibre` (an env change → new revision).
4. **`client-config.maps`** gains `azureMapsDomain: string | null` (null unless the provider is `AzureMaps`). The OpenAPI document and the TS schema are regenerated. Startup validation: `Maps:Provider=AzureMaps` requires the account client id, the account resource id, the SAS principal id and the domain; otherwise startup fails (0022 amendment pattern).
5. **CSP** (ADR 0029). Map hosts are inserted per environment:
   - MapLibre/OpenFreeMap: `connect-src` and `img-src` `https://tiles.openfreemap.org`, which serves tiles, sprites and glyphs for the `liberty` style (⚠ confirmed by the CI CSP run, not by docs); `worker-src 'self'`.
   - Azure Maps (only when enabled): `connect-src`, `img-src` and `font-src` `https://eu.atlas.microsoft.com https://atlas.microsoft.com`; `style-src` stays `'self'`, with the SDK CSS bundled.
   - Geocoding never needs a CSP entry: it is server-side.

## Consequences

- The default production configuration has a **hard €0 bound for maps**: OpenFreeMap is free, and geocoding is capped below the free tier. The brief's "switch to MapLibre if costs become an issue" is the starting point; Azure Maps display is one gated configuration change away.
- Place search works in production for the first time. Reverse geocoding stays post-MVP.
- A shared geocode budget means one abusive caller can exhaust search for everybody for the rest of the day. The impact is low: picking on the map and typing a name still work.
- The Maps account keeps local auth enabled only when display is on (ADR 0031 §3).

## Alternatives considered

- **Azure Maps for display by default (the brief's literal reading):** an unbounded worst case, because SAS caps are per second and tokens reach any guest. Rejected as a default and kept as an option.
- **Per-user SAS tokens with a low rate:** many `listSas` calls (management-plane throttling), and guest farming still multiplies the cap. Rejected.
- **Entra token for the browser:** no rate cap at all (0022). Rejected.
- **A self-hosted tile server or PMTiles on Blob:** €0 at scale-to-zero storage, but a new data pipeline (an OSM extract) and maintenance. Deferred, a good post-MVP option if OpenFreeMap ever fails.

## Amendment (2026-11-04, M7 Part A implemented)

1. **Global geocode budget is durable, not per replica (corrects §2; review H2).** An in-memory counter per replica would let restarts and a second replica multiply the cap. As built (`Infrastructure/Maps/GeocodeBudget.cs`):
   - table **`GeocodeUsage`** (`Day date` PK, `Count int`; migration `20261104153828_GeocodeUsage`; [data model](../data-model.md#geocodeusage-m7-system-counter));
   - one round trip per search: insert today's row if missing under `UPDLOCK, HOLDLOCK`, then a conditional `UPDATE … SET Count = Count + 1 OUTPUT inserted.Count WHERE Day = @day AND Count < @limit`; no row back = spent. Atomic and race-safe, **global across replicas and restarts**;
   - default `Maps:Geocoding:GlobalDailyLimit` **75** per UTC day; startup validation requires ≥ 1 when the provider is `AzureMaps`;
   - the budget is taken **after** request validation, so a malformed request never spends it;
   - exhausted: **429 `urn:runnersdiary:problem:rate-limited` with extension `code: "geocode-budget"`** and `Retry-After` = seconds until 00:00 UTC (the per-caller limiter's 429 has no `code`, so the SPA can tell them apart);
   - rows older than 35 days are purged by the guest cleanup;
   - like the AI ledger, `GeocodeUsage` is a **system table without an owner filter** (documented exception, ADR 0010 §6): it holds no user data and no user id.
   - **Worst case now:** 75 × 31 = **2,325 searches a month**, regardless of replicas and restarts (the "≈ 4,560 with 2 replicas" figure in §2 is obsolete). Still well under the free 5,000. The cost of the guarantee is one tiny SQL statement per search, on a request that already uses SQL.
2. **`AzureMapsGeocoder`** (`Maps__Geocoding__Provider=AzureMaps`):
   - Geocoding API `GET https://<Domain>/geocode?api-version=2026-01-01&query=…&top=5` (+ `coordinates` bias when `lat`/`lon` are given);
   - Entra token for `https://atlas.microsoft.com/.default` from the API's managed identity, plus `x-ms-client-id` = the account's `uniqueId`;
   - **domain allow-list:** `Maps:AzureMaps:Domain` must be `atlas.microsoft.com` or a subdomain of it (default `eu.atlas.microsoft.com`), so the bearer token can never be sent elsewhere;
   - a plain `HttpClient`: **no redirects, 6 s timeout (`Maps:AzureMaps:GeocodeTimeout`), no retry** (a retry would spend the budget twice);
   - no logging of the query text or the token; provider failures become 503 `feature-unavailable` without details.
3. **`AzureMapsSasTokenIssuer`** (`IMapsTokenIssuer`, only with `Maps__Provider=AzureMaps`):
   - ARM `POST {accountResourceId}/listSas?api-version=2023-06-01` with a token for `https://management.azure.com/.default` (API identity); plain HTTP, no `Azure.ResourceManager.Maps` package;
   - body: `signingKey: primaryKey`, `principalId` = `id-rd-maps-sas`, `maxRatePerSecond`, `regions`, `start` backdated 5 min for clock skew, `expiry` = now + `SasLifetime` (default 2 h);
   - **one shared token**, renewed when less than `SasRefreshBefore` (default 30 min) remains, **single-flight** (one refresh, others wait);
   - on a failed refresh: keep serving the previous token while it has > 5 min left; otherwise fail; then **back off 30 s** before the next ARM call, so an outage cannot become a stream of ARM requests;
   - tokens are never logged; failures log the exception type only.
   - `id-rd-api` additionally needs **Managed Identity Operator** on `id-rd-maps-sas` (the token's principal), see [ADR 0031 amendment](0031-identities-secrets-and-data-access.md) §3.
4. **`GET /api/maps/token`** as specified in §3, plus: 503 `feature-unavailable` both when display is off (`Maps__Provider` ≠ `AzureMaps`) and on any issuing failure; `Cache-Control: private, no-store` on every response. Config keys: `Maps__AzureMaps__{AccountClientId, ManagedIdentityClientId, Domain, AccountResourceId, SasPrincipalId, SasRegions__N, SasMaxRatePerSecond}` and the optional `SasLifetime`, `SasRefreshBefore`, `GeocodeTimeout`.
5. **Client (§3).** `azure-maps-control` **3.7.4** (justified: the brief's Azure Maps Web SDK), lazily loaded: vendor chunk `azure-maps-vendor` ≈ 440 kB gzip, provider chunk `AzureMapsMap` ≈ 1.8 kB; the initial bundle is unchanged within the budget. `authType: "sas"`, token kept **in memory only**, `domain` from `client-config.maps.azureMapsDomain`, SDK telemetry disabled. Any token or SDK failure falls back to MapLibre **for the session**. No cooperative gestures; wheel zoom is disabled on view-only maps instead. The privacy note names the tile host actually in use (OpenFreeMap or Azure Maps).
6. **Guardrail (changes §3).** The Maps usage alert is **unconditional** (geocoding always calls the account): `Usage` (Count) **> 200 per 24 h**, sized for the 75/day geocode cap, not > 4,000 with display only ([ADR 0034 amendment](0034-cost-guardrails-and-observability.md)).
7. **CSP (§5).** `font-src` also carries the map hosts. The image's default `CSP_MAP_HOSTS` is OpenFreeMap only; Bicep adds the atlas hosts only with G9 ([ADR 0029 amendment](0029-web-tier-nginx-container.md) §6).

**Still unverified (⚠, G9 only):** the narrow `listSas` custom role versus the documented "Contributor"; the Azure Maps Web SDK under the real CSP; whether OpenFreeMap serves every `liberty` asset from one host (CI uses a stubbed style).

## References (checked 2026-11-04)

- Azure Maps transactions (15 tiles = 1 transaction, free 5,000 base map tile transactions, search = 1 per request, non-billed status codes): https://learn.microsoft.com/azure/azure-maps/understanding-azure-maps-transactions
- Azure Maps authentication (SAS, `disableLocalAuth`, rate caps per location, `eu.atlas.microsoft.com`, CORS): https://learn.microsoft.com/azure/azure-maps/azure-maps-authentication
- Retail prices API (Gen2 meters "Maps Base Map Tiles" and "Location Insights Search": 0 for the first 5 K, then $4.50 per 1 K, `westeurope`): `https://prices.azure.com/api/retail/prices?$filter=serviceName eq 'Azure Maps' and armRegionName eq 'westeurope'`
- Azure Maps geographic scope (Europe = West/North Europe, `eu.atlas.microsoft.com`): https://learn.microsoft.com/azure/azure-maps/geographic-scope
