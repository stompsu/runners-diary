# 0022 – Map provider abstraction, geocoding and the Azure Maps token flow

**Status:** Accepted (M4 plan approved 2026-11-03) · Amended 2026-11-03 (implementation, see the end)
**Date:** 2026-11-03

## Context

Brief §2 "Maps":
- Use Azure Maps (Gen2) with the Web SDK for display and Azure Maps Search for geocoding.
- Never put a subscription key in the browser; use Entra ID or SAS tokens issued by the backend.
- Wrap the map in a `MapView` so the app can switch to MapLibre GL + OpenFreeMap (free, no key).

Constraints for M4:
- **No Azure resources exist until M7.** Creating an Azure Maps account is a cloud action that needs the user's explicit confirmation, so M4 acceptance cannot depend on one.
- CI must not depend on external tile servers.
- The initial JS bundle (≈ 195 kB gzip after M3) must not grow materially.
- The API never has public ingress (ADR 0004). Only the web origin is public.

Current facts (verified 2026-11-03):
- **Azure Maps Web SDK**: v3 is current (npm `azure-maps-control`, CDN `…/mapcontrol/3/`). v1 and v2 were retired in September 2026. `authOptions.authType` supports `subscriptionKey`, `aad`, `anonymous` and `sas`; `anonymous`/`sas` use a `getToken(resolve, reject, map)` callback.
- **SAS tokens** are JWTs signed for a **user-assigned managed identity** attached to the Maps account. They are created with the management-plane `listSas` operation (`signingKey`, `principalId`, `regions`, `maxRatePerSecond`, `start`, `expiry` ≤ 24 h). They are sent as `Authorization: jwt-sas <token>`. Requests over `maxRatePerSecond` are **not billed** (429). Tokens are immutable until expiry.
- **Azure Maps Search Get Geocoding** is `GET https://atlas.microsoft.com/geocode?api-version=2026-01-01&query=…&coordinates=lon,lat&top=…`. It returns a GeoJSON `FeatureCollection` (`properties.address.formattedAddress`, `geometry.coordinates`) and accepts an Entra token with `x-ms-client-id`. The built-in role *Azure Maps Search and Render Data Reader* covers it.
- **MapLibre GL JS v6** (2026-07-22; 6.1 since August) is **ESM-only** and **requires WebGL2**.
- **OpenFreeMap** public instance: no key, no registration, no stated request limits, free including commercial use, donation-funded, no SLA. **Attribution is required** and MapLibre adds it automatically from the style.

## Decision

1. **One `MapView` React component** (`web/src/components/map/`) with a provider-neutral props API:
   - `track?: TrackGeoJson`
   - `marker?: LatLon`
   - `highlight?: [lon, lat] | null` (hover sync)
   - `onPick?(LatLon)` (location picker)
   - `fitTo: "track" | "marker"`
   - `ariaLabel`

   Providers are **lazy chunks** chosen at runtime from `GET /api/client-config`:
   - `MapLibre` (`maplibre/MapLibreMap.tsx`, `maplibre-gl` 6.x + its CSS)
   - `AzureMaps` (`azure/AzureMapsMap.tsx`, `azure-maps-control` 3.x)

   Neither is in the initial bundle. Every map has a non-visual alternative: coordinates and the place name as text, and the track summary and splits table (plan §3.6). When WebGL2 is unavailable, `MapView` shows a localized "map unavailable" panel instead of throwing.
2. **Default provider = MapLibre + OpenFreeMap** for local development, CI and any environment without Azure Maps configuration. The style URL is configuration, `Maps:MapLibre:StyleUrl`, with default `https://tiles.openfreemap.org/styles/liberty` (exact style path ⚠ to be confirmed in step 4). The attribution control stays on (compact on phones). **Tests never touch the network**: Playwright intercepts the style host and serves a local, source-less style (`web/e2e/fixtures/map-style.json`); Vitest replaces the provider module with a fake.
3. **Azure Maps provider (production option, decided in M7):**
   - **Display:** `azure-maps-control` v3 with `authOptions: { authType: "sas", getToken }`. `getToken` calls **`GET /api/maps/token`**, which returns `{ token, expiresAt }` with `Cache-Control: private, no-store`. The endpoint is authenticated (users and guests) and has its own `maps-token` rate limit.
   - **SAS minting:** the API mints one SAS through ARM `POST {accountResourceId}/listSas?api-version=2023-06-01` using its managed identity (`DefaultAzureCredential`, scope `https://management.azure.com/.default`). Parameters:
     - `signingKey: "managedIdentity"` (⚠ verify that this value avoids shared keys)
     - `principalId` = the UAMI attached to the Maps account
     - `maxRatePerSecond` = `Maps:AzureMaps:SasMaxRatePerSecond` (default 20)
     - `regions` = configured
     - lifetime 2 h

     The API caches that one token and refreshes it when < 30 min remain, so all callers share one token whose rate cap bounds the bill.
   - **Roles (M7 Bicep):**
     - the API identity gets a custom role with only `Microsoft.Maps/accounts/listSas/action` on the account;
     - the UAMI attached to the account gets a custom render-only role (`Microsoft.Maps/accounts/services/render/read`);
     - Maps account CORS lists only the web origin.
   - **No key ever reaches the browser or the repo.**
4. **Geocoding goes through the API only:** `GET /api/geocoding/search?q=&lat=&lon=` → `{ results: [{ name, latitude, longitude, type }] }`. The browser never calls a geocoder directly. That means one CSP origin less, no token in the browser for search, and the server enforces the rate limit (`geocode` policy, 20/min users, 10/min guests).
   - Provider `Maps:Geocoding:Provider`:
     - `None` (default): returns 503 `urn:runnersdiary:problem:feature-unavailable`, and `client-config` reports `geocodingEnabled: false`, so the UI hides the search box. Picking on the map and typing a name still work.
     - `AzureMaps`: `AzureMapsGeocoder`, `HttpClient` + managed-identity Entra token (`https://atlas.microsoft.com/.default`) + `x-ms-client-id`, Search `2026-01-01`, `top=5`, `Accept-Language` from the request, optional `coordinates` bias from the current map centre.
   - The query must be 2–100 characters. Results are not stored; only the chosen point and name end up in `Runs.Location`. Reverse geocoding (naming a GPX start point) is not in M4.
5. **`GET /api/client-config` gains `maps`** (ADR 0015 pattern: anonymous, no DB, built at startup):

   ```ts
   maps: { provider: "MapLibre" | "AzureMaps"; mapLibreStyleUrl: string | null; geocodingEnabled: boolean }
   ```

   The provider can therefore be switched per environment without a rebuild. If `AzureMaps` is configured but the token endpoint later fails, the SPA falls back to MapLibre for that session (logged once).
6. **When the Azure adapters are built:** M4 builds the seam, contracts, `None` geocoder, MapLibre provider and client-config switch. The three Azure adapters are `AzureMapsGeocoder`, the `listSas` token issuer + `GET /api/maps/token`, and `AzureMapsMap.tsx`. They are built in **M4 step 8 only if the user opts in to creating an Azure Maps account now** (plan §9 Q1). Otherwise they move to M7, where the account, identities and roles are created anyway. Contracts are documented now either way.
7. **CSP notes for M7** (web tier, ADR 0004):
   - MapLibre/OpenFreeMap: `connect-src`/`img-src` `https://tiles.openfreemap.org`, `worker-src blob:` (⚠ check whether MapLibre v6 ESM still uses blob workers), `img-src data: blob:`.
   - Azure Maps: `https://atlas.microsoft.com` (+ `*.atlas.microsoft.com` for geographic endpoints) in `connect-src`, `img-src`, `style-src` and `font-src` as the SDK needs.
   - No `unsafe-eval`.

## Consequences

- M4 is fully testable with no Azure resources. The production map provider is a configuration choice made in M7 with cost data. OpenFreeMap costs €0, but has no SLA and depends on donations. Azure Maps is covered by credits and the Gen2 free quota and has an SLA, but needs the account, a UAMI and roles.
- Geocoding works only with Azure Maps. Until then the location picker is pin-on-map plus a free-text name.
- MapLibre v6 needs WebGL2. Old devices and CI browsers without GPU emulation get the fallback panel. Playwright must enable SwiftShader (plan §5 risk).
- SAS with `maxRatePerSecond` is the only Azure Maps browser-auth option with a hard cost cap. An Entra token for the managed identity handed to the browser would work too (Microsoft samples do this), but it cannot be rate-capped.

## Alternatives considered

- **Azure Maps only (no fallback provider in M4)**: M4 acceptance would then need an Azure account, and CI would need tiles from the network. Rejected.
- **MapLibre for both providers, with Azure Maps raster tiles via `transformRequest` + `jwt-sas` header**: one engine and a smaller bundle, but it departs from the brief ("Azure Maps Web SDK"), and attribution and styles would be hand-built. Kept as a fallback if `azure-maps-control` proves heavy.
- **Browser calls Azure Maps Search directly with the SAS token**: one hop less, but it widens the token's role from render to search and moves rate limiting out of our hands.
- **Entra token (managed identity) handed to the browser**: simpler (no UAMI or `listSas`), but there is no per-token rate cap, and the token carries all data-plane roles of the API identity.
- **Nominatim (OSM) for geocoding**: free, but its usage policy forbids heavy or autocomplete use and requires 1 req/s, an identifying User-Agent and attribution. It is a third-party dependency on the server and not part of the brief.
- **`react-map-gl` / `react-azure-maps` wrappers**: these add dependencies for about 100 lines of imperative glue that `MapView` needs anyway.

## References (checked 2026-11-03)

- Azure Maps map control (v3, npm vs CDN, `authOptions`; updated 2026-09-30): https://learn.microsoft.com/azure/azure-maps/how-to-use-map-control
- `atlas.AuthenticationOptions` (`authType` incl. `sas`/`anonymous`, `getToken`, `sasToken`): https://learn.microsoft.com/javascript/api/azure-maps-control/atlas.authenticationoptions
- Azure Maps authentication (Entra, SAS via UAMI + `listSas`, `maxRatePerSecond`, ≤ 24 h, `jwt-sas` header, roles, CORS, non-billed 429; updated 2026-09-15): https://learn.microsoft.com/azure/azure-maps/azure-maps-authentication
- Maps management `listSas` (api-version 2023-06-01 on the reference page; `signingKey` values incl. managed identity per SDK docs): https://learn.microsoft.com/rest/api/maps-management/accounts/list-sas · https://learn.microsoft.com/javascript/api/@azure/arm-maps/accountsasparameters
- Azure Maps Search Get Geocoding (api-version **2026-01-01**, also 2025-01-01; updated 2026-10-23): https://learn.microsoft.com/rest/api/maps/search/get-geocoding
- Web SDK v1/v2 retirement (2026-09-19 / 2026-09-30): https://azure.microsoft.com/updates/azure-maps-web-sdk-map-controls-version-1xx-will-be-retired-on-19-september-2026 · https://azure.microsoft.com/en-us/updates/azure-maps-web-sdk-map-control-version-20x-and-azure-maps-service-module-will-be-retired-on-30-september-2026/
- MapLibre GL JS v6 (ESM-only, WebGL2 required): https://maplibre.org/news/2026-08-02-maplibre-newsletter-jul-2026/ · secondary: https://geo.malagis.com/maplibre-gl-js-v6-mandatory-webgl-and-esm-only.html
- OpenFreeMap (free public instance, attribution "OpenFreeMap © OpenMapTiles Data from OpenStreetMap", automatic in MapLibre): https://openfreemap.org
- ⚠ Not verified: Azure Maps Gen2 free-quota numbers and tile transaction pricing (M7 cost estimate); whether `signingKey: managedIdentity` works with `disableLocalAuth: true`; `azure-maps-control` current 3.x minor and bundle size; OpenFreeMap exact `liberty` style URL; MapLibre v6 worker/CSP requirements.

## Amended 2026-11-03 (implementation)

The user chose **not** to create an Azure Maps account in M4 (plan §9 Q1), so §6's step 8 was skipped: `AzureMapsGeocoder`, `GET /api/maps/token` and `AzureMapsMap.tsx` move to **M7**. Differences from the text above (M4 plan [§12](../plans/M4-maps-gpx.md#12-implementation-notes--deviations-2026-11-03)):

- **§1 MapView:** MapLibre **6.16.0**; the worker is loaded as a same-origin file via `?url` + `setWorkerUrl` (no `blob:` worker). WebGL2 detection and the "map unavailable" panel as decided. Profile ↔ map sync uses a native range input over the Recharts chart for keyboard access.
- **§2 style URL:** `https://tiles.openfreemap.org/styles/liberty` confirmed as the default. `Maps:MapLibre:StyleUrl` must be an **absolute `https` URL**, otherwise the API **fails at startup**. Playwright stubs the style and uses the real MapLibre renderer (SwiftShader works with default flags; `--enable-unsafe-swiftshader` kept as insurance).
- **§4 geocoding:** `IGeocoder` + `NoGeocoder` only; `GET /api/geocoding/search` → 503 `feature-unavailable` with provider `None`, and also when a provider throws (no provider details leak). Results are capped at 5. The `geocode` policy **replaces** the general limiter on that route (Development ×10).
- **§5 client-config / fallback:** until the Azure adapter exists, `Maps:Provider` other than `MapLibre` **fails startup** instead of falling back in the SPA; the session fallback described in §5 is M7 work. `client-config` additionally carries `gpx: { maxFileBytesUser, maxFileBytesGuest }` (ADR 0020 amendment). `geocodingEnabled` reflects whether a working geocoder is registered.
- **§7 CSP (refined for M7):** `worker-src 'self'` (not `blob:`); `connect-src` the tile host **plus the sprite and glyph hosts the style names**; `img-src blob: data:` still to verify; markers use inline `style` attributes, which `style-src` must allow or which must move to classes. Tile requests reveal the viewed area to OpenFreeMap (privacy note for M7).
- **Track GeoJSON caching** is `private, no-cache` + ETag (ADR 0021 amendment), not `immutable`.
