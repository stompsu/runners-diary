# M4 – Maps & GPX: plan

**Status:** Implemented — awaiting CI + approval (plan approved 2026-11-03; user answered §9, all defaults; step 8 skipped per Q1; deviations in [§12](#12-implementation-notes--deviations-2026-11-03)) · **Date:** 2026-11-03 · **Author:** architect
**Builds on:** M2 as merged (`GpxTracks` metadata and blobs, demo tracks `Pending`, `TODO(M4)` in `DeleteRun`, 64 KB body limit; [M2 §11](M2-domain-runs-api.md#11-implementation-notes--deviations-2026-11-03)) and M3 as built: the run form keeps `location` from `base`, lazy panel routes, the fake API shared by Vitest and Playwright, and an initial load of ≈ 195 kB gzip ([M3 §12](M3-calendar-ui.md#12-implementation-notes--deviations-2026-11-03)).
**Related ADRs:** [0008](../adr/0008-run-date-and-time-storage.md) dates · [0010](../adr/0010-current-user-and-data-scoping.md) scoping · [0011](../adr/0011-guest-accounts-lifecycle.md) guests · [0015](../adr/0015-spa-runtime-configuration.md) client config · [0018](../adr/0018-run-forms-and-server-errors.md) forms · [0019](../adr/0019-frontend-test-strategy.md) tests · **new (accepted, amended after implementation):** [0020](../adr/0020-gpx-upload-and-safe-parsing.md) upload & safe parsing · [0021](../adr/0021-track-metrics-simplification-and-geojson.md) metrics, simplification, GeoJSON · [0022](../adr/0022-map-provider-geocoding-and-azure-maps-tokens.md) map provider, geocoding, Azure Maps tokens · [0023](../adr/0023-run-tracks-prefill-and-deletion.md) run ↔ track, prefill, deletion
**Contracts:** §3 here is the approved plan; the implemented contract is in [`api-contract.md`](../api-contract.md#added-in-m4-plan-adrs-00200023) and [`data-model.md`](../data-model.md#gpxtracks), with the differences listed in §12.

## 1. Goal & acceptance criteria

**Goal (brief §4 M4, "Maps & GPX"):** location picker + geocoding, GPX upload (planned course or recorded track), server-side parsing & metrics, auto-fill, map track display, elevation profile, secure token flow for Azure Maps. Constraints: **no Azure resources are needed for acceptance** (ADR 0022), and CI makes no external network calls for maps.

**Out of scope:** FIT/TCX import, Strava/Garmin sync; reverse geocoding (naming a GPX start point); a time-zone lookup from coordinates; speed-spike filtering; reprocessing existing tracks; insights over track data (M5); deployment, CSP and the Maps account (M7). Azure Maps adapters are in scope only if §9 Q1 = yes.

| # | Criterion | How verified |
|---|---|---|
| AC1 | `POST /api/runs/{runId}/tracks` (multipart `kind`, `file`, `replace`) stores a valid GPX and returns 201 `TrackDto` with metrics. It accepts `trk` and `rte` files, multi-segment files, and untimed or elevation-less files (with warnings). Rejected files leave **no row and no blob**. | Api.Tests (Testcontainers + Azurite) |
| AC2 | Safe parsing (ADR 0020): DOCTYPE/XXE/billion-laughs, non-GPX root, malformed XML, gzip/zip, > 250 000 points, < 2 points, invalid coordinates and a wrong extension each give 400 `invalid-gpx` with the right `code`. No parser text is echoed. 21 MB (user) or 6 MB (guest) → 413. The 64 KB limit still applies to every other route. | Domain.Tests (fixtures) + Api.Tests |
| AC3 | Metrics (ADR 0021) are deterministic and match the demo README: distance ±1 m, gain/loss ±2 m, elapsed time exact. Hand-computed fixtures cover haversine, hysteresis, moving time, splits (incl. the partial split), DP vertex cap and endpoints, and profile ≤ 601 samples. | Domain.Tests |
| AC4 | `GET …/tracks/{id}`, `…/geojson` (RFC 7946 FeatureCollection + `profile`/`splits`, cache headers, ETag) and `…/gpx` (attachment, `nosniff`, `no-store`, sanitized file name) are all **404 for foreign or missing runs and tracks** (isolation matrix: guest/guest, user/user, guest/user, and a track id combined with someone else's run id). | Api.Tests |
| AC5 | Replace needs `replace=true` (else 409 `track-exists`). Delete track and **delete run remove the blobs** (`TODO(M4)` resolved). A blob-delete failure → 503 and nothing removed. Replace removes the old blobs after commit. | Api.Tests (fault-injected `IBlobStore`) |
| AC6 | Limits: guest track cap 16 / user cap 2 000 → 409 `track-limit`. `gpx-upload` sliding window (users 30/h, guests 10/h) → 429 + `Retry-After`. Processing slots full → 503 + `Retry-After`. Rest-day run → 400. | Api.Tests with low configured limits |
| AC7 | Demo: a fresh guest's 6 demo tracks are `Processed` (summary + GeoJSON blob) with no per-guest parsing. The startup check fails if a demo file is rejected. Guest cleanup still removes everything. | Api.Tests + Domain.Tests |
| AC8 | Run details show each track: summary, a map with the line plus start/finish markers (fit to bbox), an elevation profile linked to the map (pointer **and** keyboard on the chart move a map marker), an accessible text alternative (summary + splits table), plus download, replace and delete with confirmation. The track view is deep-linkable (`…/runs/:runId/tracks/:trackId`). | Vitest + Playwright (real MapLibre, stubbed tiles) |
| AC9 | Prefill (ADR 0023): after an upload (or **Use GPX values**), a review dialog lists GPX vs current values. Empty fields are pre-checked, differing ones unchecked, and a date mismatch is warned and unchecked. Apply opens the edit form with the values dirty; **nothing is saved before Save**. A Planned run with a recorded track offers *Mark as completed*. | Vitest (`trackSuggestions`, dialog) + Playwright journey |
| AC10 | Location picker: the run form shows the location (name, coordinates) with *Pick on map* (click/tap sets the pin; keyboard alternative: latitude/longitude inputs), *Clear*, and **Search** (only when `client-config.maps.geocodingEnabled`). `toWriteRequest` sends the edited location; the round trip keeps exact coordinates. | Vitest + Playwright |
| AC11 | Geocoding `GET /api/geocoding/search` exists. With provider `None` it returns 503 `feature-unavailable` and the UI hides search. It has auth, the `geocode` rate limit and 2–100 character validation. Provider-neutral `IGeocoder`, tested with a fake in Api.Tests. | Api.Tests + Vitest |
| AC12 | `GET /api/client-config` returns `maps { provider, mapLibreStyleUrl, geocodingEnabled }`. The SPA selects the provider lazily. Without WebGL2 a "map unavailable" panel appears and the text alternatives still work. | Api.Tests + Vitest |
| AC13 | **Azure Maps token flow designed**: `GET /api/maps/token` contract, SAS parameters, roles and CORS are documented (ADR 0022). Implemented and manually verified **only if §9 Q1 = yes**; otherwise deferred to M7 (the same precedent as M3 AC3/AC4). **No key in the browser or repo.** | reviewer · manual (Q1) |
| AC14 | Bundle: initial load grows ≤ 5 kB gzip over M3. MapLibre (+CSS), Recharts and the track/location UI are lazy chunks. Checked by a `bundle-budget` script in CI. | CI |
| AC15 | Phone (360×640): the track view and picker work as bottom sheets with no horizontal scroll, the map is at least 240 px tall, and the profile is readable. axe (serious/critical = fail) on the run details with a track, the track view, the upload dialog, the prefill dialog and the picker. i18n keys for every string. | Playwright `phone-la` + axe |
| AC16 | OpenAPI and TS schema regenerated (multipart documented); `dotnet build` warning-free; all CI jobs green; `RunnersDiary.Domain` still has no package references. | CI |

## 2. Files / projects by owning agent

### maps-gpx (Domain pipeline, Infrastructure pipeline, map components)
```
src/RunnersDiary.Domain/Gpx/
  GpxReader.cs, GpxReadLimits.cs, GpxRejection.cs (code enum + fixed messages), GpxDocument.cs (segments/points)
  Haversine.cs, ElevationHysteresis.cs, MovingTime.cs, Splits.cs, TrackMetrics.cs (TrackSummary, TrackWarnings flags)
  DouglasPeucker.cs, ElevationProfile.cs, ProcessedTrack.cs (line, start/finish, bbox, profile, splits), TrackProcessor.cs (ProcessorVersion = 1)
  TrackStartLocal.cs                                  # UTC → run zone date/time (uses Time/ helpers)
src/RunnersDiary.Infrastructure/Gpx/
  GpxLimitsOptions.cs, TrackIngestionService.cs (bounded copy, SHA-256, semaphore, process, blobs, rollback), TrackGeoJsonWriter.cs (System.Text.Json)
src/RunnersDiary.Infrastructure/Maps/
  IGeocoder.cs, NoGeocoder.cs, MapsOptions.cs         # + AzureMapsGeocoder.cs, AzureMapsSasTokenIssuer.cs (step 8, Q1)
web/src/components/map/
  MapView.tsx (provider switch, lazy, WebGL2 check, fallback panel), types.ts (TrackGeoJson re-export, LatLon), useMapProvider.ts
  maplibre/MapLibreMap.tsx (+ maplibre-gl.css import), maplibre/trackLayers.ts (line, casing, start/finish markers)
  azure/AzureMapsMap.tsx                              # step 8 (Q1)
  ElevationProfile.tsx (Recharts AreaChart, accessibilityLayer, onHover(index)), TrackDataTable.tsx (summary + splits)
web/e2e/fixtures/map-style.json                       # source-less style for tests (background only)
tests/fixtures/gpx/                                   # see §6 (qa-tester co-owns)
```

### backend-dotnet
```
src/RunnersDiary.Domain/Tracks/GpxTrack.cs            # + summary members, CreateProcessed(...), WarningFlags; keep CreatePending (legacy)
src/RunnersDiary.Infrastructure/Persistence/Configurations/GpxTrackConfiguration.cs   # new columns, CK_GpxTracks_Processed
src/RunnersDiary.Infrastructure/Persistence/Migrations/<ts>_GpxTrackProcessing.cs
src/RunnersDiary.Infrastructure/Storage/IBlobStore.cs, AzureBlobStore.cs, BlobPaths.cs # + OpenReadAsync, TrackPrefix, TrackProcessed
src/RunnersDiary.Infrastructure/Guests/DemoDatasetLoader.cs, DemoDatasetStartupCheck.cs, DemoDataSeeder.cs  # pre-processed demo tracks
src/RunnersDiary.Infrastructure/DependencyInjection.cs  # GpxLimits, ingestion, geocoder
src/RunnersDiary.Api/Http/RequestBodyLimit.cs          # honour endpoint IRequestSizeLimitMetadata
src/RunnersDiary.Api/Http/ProblemTypes.cs               # invalid-gpx, track-exists, track-limit, processing-busy, feature-unavailable
src/RunnersDiary.Api/RateLimiting/*                     # policies gpx-upload, geocode (+ maps-token in step 8)
src/RunnersDiary.Api/Features/Tracks/{TrackContracts.cs, UploadTrack.cs, GetTrack.cs, GetTrackGeoJson.cs,
                                      DownloadTrackGpx.cs, DeleteTrack.cs, TracksEndpoints.cs}
src/RunnersDiary.Api/Features/Runs/{DeleteRun.cs, RunContracts.cs, RunMapping.cs}   # blobs on delete; tracks[] + fileName/createdAt
src/RunnersDiary.Api/Features/Geocoding/SearchPlaces.cs
src/RunnersDiary.Api/Features/ClientConfig/GetClientConfig.cs  # + maps
src/RunnersDiary.Api/Features/Maps/GetMapsToken.cs      # step 8 (Q1)
src/RunnersDiary.Api/appsettings.json                   # GpxLimits, Maps (non-secret defaults)
src/RunnersDiary.Api/openapi/runnersdiary-api.json      # regenerated
```

### frontend-react
```
web/package.json, package-lock.json        # §7
web/vite.config.ts                         # advancedChunks: 'map-vendor' (maplibre-gl), 'chart-vendor' (recharts) — lazy only
web/scripts/bundle-budget.mjs              # fails if initial (entry + its static imports) gzip > M3 baseline + 5 kB
web/src/features/tracks/{TrackSection.tsx, TrackUploadDialog.tsx, TrackViewer.tsx (map + profile, hover state),
                         TrackPrefillDialog.tsx, trackSuggestions.ts, trackQueries.ts, uploadTrack.ts (FormData via fetchClient),
                         downloadTrack.ts (blob → object URL), trackProblemMessage.ts}
web/src/features/runs/{RunDetailsDialog.tsx (+TrackSection), RunForm.tsx (+LocationField, prefill notice, "Save & add GPX"),
                       LocationField.tsx, LocationPickerDialog.tsx, PlaceSearch.tsx,
                       runFormTypes.ts, runFormMapping.ts (location from values; applyPrefill), runFormSchema.ts (lat/lon/name rules),
                       panelRoutes.ts (+ runs/:runId/tracks/:trackId)}
web/src/auth/useClientConfig.ts            # maps section
web/src/i18n/en/{tracks,maps}.json (+ runs.json location keys)
```

### qa-tester
```
tests/fixtures/gpx/                         # §6 list; small generated files, the 25 MB one generated at test time (not committed)
tests/RunnersDiary.Domain.Tests/Gpx/*       # parser, limits, metrics, DP, profile, demo golden values
tests/RunnersDiary.Api.Tests/Tracks/*, Geocoding/*, Isolation/IsolationMatrixTests.cs (+ track endpoints),
                              Security/EndpointAuthorizationTests.cs (inventory), Guests/* (processed demo tracks)
web/src/test/fakeApi/*                      # tracks, geojson, gpx, geocoding, client-config.maps; GPX fixtures as prebuilt TrackDto/GeoJSON
web/src/**/*.test.ts(x)                     # §6
web/e2e/{tracks.spec.ts, location.spec.ts, tracks.phone.spec.ts}, e2e/support.ts (map style route stub)
web/e2e-fullstack/journey.spec.ts           # + upload tests/fixtures/gpx/road-5k.gpx → map → prefill → save
```

### azure-infra (CI only in M4)
```
.github/workflows/ci.yml   # frontend: + `node scripts/bundle-budget.mjs` after build; nothing else (no secrets, no cloud)
```

### architect
After implementation: `data-model.md` (columns), `api-contract.md` (endpoints, problem types, limits), `architecture.md`, ADR status/deviations.

## 3. Contracts

### 3.1 Schema delta — migration `GpxTrackProcessing` (all new columns nullable; existing `Pending` rows stay valid)

| Column | SQL type | Notes |
|---|---|---|
| `ProcessedBlobName` | `varchar(400)` | `users/{u:N}/tracks/{t:N}/track.json` |
| `ProcessorVersion` | `tinyint` | 1 |
| `ProcessedAt` | `datetime2(0)` | UTC |
| `PointCount` | `int` | raw points |
| `DistanceMeters` | `float` | |
| `ElevationGainMeters` / `ElevationLossMeters` | `float` | null = no elevation |
| `MinElevationMeters` / `MaxElevationMeters` | `float` | |
| `ElapsedSeconds` / `MovingSeconds` | `int` | null = untimed |
| `StartUtc` / `EndUtc` | `datetime2(0)` | null = untimed |
| `StartPoint` / `FinishPoint` | `geography` | SRID 4326, existing `GeoPointConverter` |
| `WarningFlags` | `tinyint` | bit flags (ADR 0021) |

`CK_GpxTracks_Processed`: `Status <> 1 OR (ProcessedBlobName IS NOT NULL AND DistanceMeters IS NOT NULL AND PointCount IS NOT NULL AND StartPoint IS NOT NULL)`. No new indexes; the M5 queries go through `Runs`. Blob layout: ADR 0021 §6.

### 3.2 Endpoints (all **Auth** + general limiter; 404 for foreign runs/tracks; problem+json)

| Method & path | operationId | Success | Errors |
|---|---|---|---|
| `POST /api/runs/{runId}/tracks` (multipart: `kind`, `file`, `replace?`) | `uploadTrack` | 201 `TrackDto` + `Location` | 400 (`invalid-gpx`; validation of `kind`; rest-day), 404, 409 (`track-exists`, `track-limit`), 413, 429 (`gpx-upload`), 503 (`processing-busy`) |
| `GET /api/runs/{runId}/tracks/{trackId}` | `getTrack` | 200 `TrackDto` | 404 |
| `GET /api/runs/{runId}/tracks/{trackId}/geojson` | `getTrackGeoJson` | 200 `TrackGeoJson` (`application/geo+json`), `Cache-Control: private, max-age=86400, immutable`, `ETag` | 404 (incl. not `Processed`) |
| `GET /api/runs/{runId}/tracks/{trackId}/gpx` | `downloadTrackGpx` | 200 `application/gpx+xml`, `Content-Disposition: attachment; filename="…"; filename*=UTF-8''…`, `X-Content-Type-Options: nosniff`, `Content-Security-Policy: sandbox`, `Cache-Control: private, no-store` | 404 |
| `DELETE /api/runs/{runId}/tracks/{trackId}` | `deleteTrack` | 204 | 404, 503 (blob delete failed) |
| `DELETE /api/runs/{id}` (changed) | `deleteRun` | 204 (blobs deleted first) | 404, **503** (new) |
| `GET /api/geocoding/search?q&lat&lon` | `searchPlaces` | 200 `PlaceSearchResponse` | 400 (q 2–100; lat/lon pair, ranges), 429 (`geocode`), 503 `feature-unavailable` |
| `GET /api/maps/token` (step 8, Q1) | `getMapsToken` | 200 `{ token, expiresAt }`, `no-store` | 429 (`maps-token`), 503 `feature-unavailable` |
| `GET /api/client-config` (changed) | `getClientConfig` | + `maps` | — |

```ts
TrackDto = {
  id: string; runId: string; kind: "PlannedCourse" | "RecordedTrack"; status: "Pending" | "Processed" | "Failed";
  originalFileName: string; sizeBytes: number; createdAt: string;
  summary: null | {                                   // null unless Processed
    pointCount: number; distanceMeters: number;
    elevationGainMeters: number | null; elevationLossMeters: number | null;
    minElevationMeters: number | null; maxElevationMeters: number | null;
    elapsedSeconds: number | null; movingSeconds: number | null;
    startUtc: string | null; endUtc: string | null;
    start: { latitude: number; longitude: number }; finish: { latitude: number; longitude: number };
    startLocal: { date: string; time: string /* HH:mm */; timeZoneId: string } | null   // startUtc in the run's current zone
  };
  warnings: ("timestampsIgnored" | "noElevation" | "multipleSegments" | "routeConverted")[]
}
RunDto.tracks[] = { id; kind; status; originalFileName; createdAt }      // + 2 members (additive)
TrackGeoJson   = ADR 0021 §5 (FeatureCollection; features line/start/finish; bbox; formatVersion; profile | null; splits[])
PlaceSearchResponse = { results: { name: string; latitude: number; longitude: number; type: string | null }[] }  // ≤ 5
ClientConfigResponse.maps = { provider: "MapLibre" | "AzureMaps"; mapLibreStyleUrl: string | null; geocodingEnabled: boolean }
// invalid-gpx problem: { type: "urn:runnersdiary:problem:invalid-gpx", status: 400, errors: { file: [msg] }, code: "notGpx" | … }
```

**OpenAPI multipart:** the endpoint binds `[FromForm] UploadTrackForm { TrackKindDto Kind; IFormFile File; bool Replace }`. If the generated schema types the form badly, `uploadTrack.ts` builds `FormData` and passes it with `bodySerializer: (b) => b` (openapi-fetch), still through `fetchClient`, so oxlint's "no raw fetch" rule holds. ⚠ To be verified in step 3. A `downloadTrackGpx` call uses `parseAs: "blob"`.

**Body limit:** `RequestBodyLimit` reads `context.GetEndpoint()?.Metadata.GetMetadata<IRequestSizeLimitMetadata>()`; the upload endpoint gets `MaxFileBytes + 65 536` and `WithFormOptions(multipartBodyLengthLimit: MaxFileBytes + 65 536)`. The guest limit is checked in the handler before the form is read (`Content-Length`), then against `IFormFile.Length`.

### 3.3 Configuration (non-secret; `appsettings.json` defaults)
```jsonc
"GpxLimits": { "MaxFileBytes": 20971520, "GuestMaxFileBytes": 5242880, "UserMaxTracks": 2000, "GuestMaxTracks": 16,
               "MaxPoints": 250000, "MaxConcurrentProcessing": 2 },
"RateLimiting": { "GpxUpload": { "UserPermitLimit": 30, "GuestPermitLimit": 10, "Window": "01:00:00" },
                  "Geocode":   { "UserPermitLimit": 20, "GuestPermitLimit": 10, "Window": "00:01:00" } },
"Maps": { "Provider": "MapLibre", "MapLibre": { "StyleUrl": "https://tiles.openfreemap.org/styles/liberty" },
          "Geocoding": { "Provider": "None" },
          "AzureMaps": { "AccountClientId": null, "AccountResourceId": null, "SasPrincipalId": null,
                         "SasMaxRatePerSecond": 20, "SasRegions": [] } }   // only used in step 8 / M7
```
Development keeps the same GPX limits; the rate limits are raised as in M2 (`appsettings.Development.json`).

### 3.4 Prefill (client, ADR 0023)
`trackSuggestions(run: RunDto, track: TrackDto, rules: RunTypeRules) → Suggestion[]`:

| Row | Recorded track | Planned course | Default checked |
|---|---|---|---|
| distance | ✓ | ✓ | run field empty |
| duration (elapsed \| moving) | ✓ if timed | — | empty; radio defaults to elapsed |
| elevation gain / loss | ✓ if elevation | ✓ if elevation | empty |
| date + start time | ✓ if `startLocal` | — | `startTime` empty **and** date equal; a date mismatch is unchecked with a warning |
| location (GPX start point) | ✓ | ✓ | run has no location; an existing name is kept |
| mark as completed | ✓ if Planned | — | checked |

Rows whose values are equal are omitted. For rest-day types: no rows and a message. Apply → `navigate("…/runs/:id/edit", { state: { prefill: FormPatch, source: fileName } })` → `applyPrefill(values, patch)` → RHF `reset(values, { keepDefaultValues: true })` so the fields are dirty.

### 3.5 Location form field (ADR 0023 §4)
`RunFormValues.location: { latitude: number; longitude: number; name: string } | null`.
- Schema: name ≤ 200 characters.
- The coordinates come only from the map or search. The keyboard alternative is two numeric inputs inside the picker, validated to the ranges.
- `toWriteRequest`: `location: values.location ? { latitude, longitude, name: orNull(name) } : null`.
- The picker opens at the existing pin, else at the GPX start, else the browser's last map centre (sessionStorage), else Gliwice (`50.2945, 18.6714`) as the app default.

### 3.6 Track view, profile ↔ map sync, accessibility
- `TrackViewer` holds `hoverIndex: number | null`. `ElevationProfile` reports `onHover(index)` from pointer moves and from **arrow keys** on the focused chart (Recharts 3 `accessibilityLayer`). `MapView.highlight = profile[index][2..3]` draws a single marker. Map → chart sync (nearest vertex on hover) is a stretch goal and not an AC.
- Text alternative, always rendered under the map:
  - a summary `<dl>` (distance, elapsed/moving, pace, gain/loss, min/max elevation, warnings in words);
  - a collapsible **splits table** (`<table>` with km, time, pace, elevation change);
  - the map container has `role="region"` + `aria-label`, and the canvas is `aria-hidden` (MapLibre's own controls stay keyboard-operable).
- Phone: the viewer is a bottom-sheet dialog; map height `max(240px, 40vh)`; the profile is 120 px high; the table is collapsed by default.
- Pace, distance and elevation use the existing `lib/format/units` helpers.

### 3.7 Queries (TanStack Query via `$api`)

| Query/mutation | Cache | Invalidates |
|---|---|---|
| `getTrack(runId, id)` | 5 min | — |
| `getTrackGeoJson(runId, id)` | `staleTime: Infinity`, `gcTime` 30 min (immutable id) | — |
| `uploadTrack` (mutation, no retry) | — | `getRun(runId)`, calendar months, `listRuns` |
| `deleteTrack` | — | same |
| `searchPlaces(q)` | 10 min; manual submit (no search on every keystroke); min 2 chars | — |

## 4. Implementation steps (each = one reviewable commit)

1. **maps-gpx (+ qa-tester) – Domain pipeline.** `Domain/Gpx/*` (reader with limits, metrics, DP, profile, splits, `TrackStartLocal`), fixture set (§6), Domain.Tests incl. **demo golden values**. No API changes yet.
2. **backend-dotnet – schema & demo.** `GpxTrack` summary + `CreateProcessed`, configuration + migration `GpxTrackProcessing`, `BlobPaths`/`IBlobStore.OpenReadAsync`, `TrackGeoJsonWriter`, demo loader pre-processing + startup check + seeder writes both blobs and `Processed` rows. Tests: schema, seeding (6 processed tracks, 12 blobs), cleanup unchanged.
3. **backend-dotnet (+ maps-gpx ingestion) – endpoints.** Track endpoints, `TrackIngestionService`, endpoint body limit + form options + `DisableAntiforgery`, problem types, rate policies, limits, `DeleteRun` blob deletion (`TODO(M4)` removed), `RunDto.tracks` members, `client-config.maps`, geocoding endpoint with `NoGeocoder`, inventory/allowlist test, isolation matrix extension. Regenerate OpenAPI + TS schema. **Spike first:** multipart binding + antiforgery + per-endpoint size limit + OpenAPI shape for `IFormFile` (record the outcome in §12).
4. **frontend-react + maps-gpx – map & profile.** Deps (§7), `MapView` + MapLibre provider + WebGL2 fallback, `ElevationProfile`, `TrackDataTable`, `TrackViewer` route, chunk groups, `bundle-budget.mjs`, fake API extension (tracks/geojson/client-config), the e2e map-style stub. **Spike first:** MapLibre v6 in headless Chromium on CI (WebGL2 via SwiftShader; see §5).
5. **frontend-react – tracks in run details.** `TrackSection` (upload with kind choice, replace confirm, delete confirm, download), `TrackUploadDialog` (client-side size/extension pre-check, progress text, problem → message), `TrackPrefillDialog` + `trackSuggestions` + `applyPrefill`, "Save & add GPX" on the create form.
6. **frontend-react + maps-gpx – location.** `LocationField`, `LocationPickerDialog` (pin, keyboard lat/lon inputs), `PlaceSearch` (shown only when geocoding is enabled), `toWriteRequest`/schema/round-trip updates, run details show location name + small static map (lazy).
7. **qa-tester – E2E & CI.** Mocked Playwright journeys (§6), phone + axe, full-stack journey extended with an upload; **azure-infra** adds the bundle-budget step.
8. **(Only if §9 Q1 = yes) maps-gpx + backend-dotnet – Azure Maps adapters.** `AzureMapsGeocoder`, `AzureMapsSasTokenIssuer` + `GET /api/maps/token` (cached SAS, `maps-token` limit), `AzureMapsMap.tsx` (`authType: "sas"`), configuration via AppHost user-secrets (`Maps:*` ids, non-secret), manual verification against the user's account with `az login` (`DefaultAzureCredential`). Otherwise → M7 backlog.
9. **reviewer** – XML/upload security, IDOR on track routes, blob naming and headers, limits and cost, bundle, a11y, ADR consistency. **architect** – deviations, docs (`data-model.md`, `api-contract.md`, `architecture.md`), ADR status. Main agent updates CLAUDE.md and commits "M4: maps & GPX"; stop for approval.

## 5. Risks

| Risk | Impact | Mitigation |
|---|---|---|
| MapLibre v6 needs WebGL2; headless Chromium may not create a WebGL context (Chrome removed the automatic SwiftShader fallback around M139) | Map E2E fails in CI | Step-4 spike: Playwright `launchOptions.args: ["--enable-unsafe-swiftshader"]` (or `--use-angle=swiftshader`) for the map project. Assertions use `data-map-state="ready"` / `data-track-vertices` set by `MapView`, not pixels. Fallback: map specs assert the "map unavailable" panel + text alternatives, and the real map is checked manually. |
| `IFormFile`/antiforgery/size-limit behaviour in .NET 10 not verified in official docs | 400/500 instead of 201/413 | Step-3 spike + tests for 21 MB, guest 6 MB, normal upload; raw-body endpoint as documented fallback (ADR 0020 alternatives). |
| OpenAPI/openapi-typescript type for multipart is awkward | TS friction | `FormData` + `bodySerializer` through `fetchClient`; contract test asserts the operation exists. |
| Memory: 20 MB file buffered (+ points + JSON) per upload | OOM on a small replica | 2 processing slots, bounded copy; M7 sizes replicas (≥ 1 GiB). |
| Hysteresis gain differs from watch/Strava numbers | User confusion | Documented rule + tooltip; `ProcessorVersion` allows later change. |
| GPS spikes inflate distance | Wrong distance → wrong prefill | User confirms the prefill; spike filter as a follow-up (processor v2). |
| Track date vs run date mismatch (wrong file, travel, or the demo's 2025 timestamps) | Wrong date applied | The date row is unchecked by default, with a warning; there is no automatic date change. |
| OpenFreeMap has no SLA or availability guarantee (dev and possibly prod) | Blank map | Attribution + fallback panel; style URL is configuration; Azure Maps provider in M7. Tests never use the network. |
| Bundle growth (maplibre ≈ 230 kB gzip ⚠, recharts ≈ 100 kB gzip ⚠, est.) | Slow first load on phones | Lazy chunks only; budget script in CI; Recharts is shared with M5. |
| Recharts 3 peer range vs React 19.3 | Install conflict | Check `npm info recharts peerDependencies` at install; fall back to a hand-rolled SVG profile (≈ 150 lines) if it conflicts. |
| `toWriteRequest` now sends an edited location (not `base`) | Accidental location loss on PUT | Round-trip + "untouched location equals base" tests; `withAllWriteMembers` still enforced. |
| Blob delete first → storage outage blocks run deletion | 503 on delete | Retryable, visible, no orphans (ADR 0023). |
| Orphaned blobs after a failed best-effort replace delete (users) | Small storage leak | Logged `TrackBlobOrphaned`; guests swept by cleanup; reconciliation post-MVP. |
| Azure Maps SAS (`listSas`, `signingKey: managedIdentity`) unverified | M7 surprise | Q1 lets the user verify early; Entra token handed to the browser is the documented fallback (no rate cap). |
| Fake API drifts from track endpoints | Green tests, broken app | Typed against `schema.d.ts`; full-stack journey uploads a real file on `main`. |

## 6. Test plan outline (qa-tester)

**Fixtures `tests/fixtures/gpx/`** (≤ 50 KB each unless noted; generated by a small script or by hand; no personal data):
- `road-5k.gpx`: hand-checkable: 6 points on a meridian (Δlat known, so haversine is known), times, flat.
- `hill-hysteresis.gpx`: elevation sequence with ±2 m noise and real 10 m steps (expected gain/loss computed by hand).
- `stop-and-go.gpx`: 2 min standstill (moving < elapsed).
- `multi-segment.gpx`, `two-tracks.gpx`, `route-only.gpx` (`rte`), `no-elevation.gpx`, `no-time.gpx`, `partial-time.gpx`, `time-backwards.gpx`.
- `gpx10.gpx` (GPX 1.0 namespace), `utf16.gpx`, `bom.gpx`, `extensions-heavy.gpx` (Garmin TrackPointExtension, ignored).
- **Malicious/invalid:** `xxe-file.gpx` (external entity to `file:///etc/passwd`), `xxe-http.gpx`, `billion-laughs.gpx`, `dtd-only.gpx`, `not-gpx-root.xml` (`<kml>`), `wrong-namespace.gpx`, `malformed.gpx` (truncated), `deep-nesting.gpx` (depth 64 inside `extensions`), `invalid-lat.gpx` (91), `nan-lon.gpx`, `one-point.gpx`, `empty.gpx` (0 bytes), `gzip.gpx.gz` renamed `.gpx`, `zip-as.gpx`, `html-as.gpx`, `script-in-name.gpx` (`<name><script>…`, never echoed). Generated at test time: `too-many-points` (250 001 pts, ≈ 20 MB) and a **25 MB** file (`MemoryStream`, not committed).
- The six demo files under `seed/demo/gpx/` are golden references (README table).

**Domain.Tests (`Gpx/`)**
- Every malicious fixture → the expected `GpxRejection` code, with no file system or network access (XXE: a resolver that would throw is never called; the test also asserts the exception text does not contain file contents).
- Haversine against a known value (1° of latitude ≈ 111 195 m with R = 6 371 008.8). Hysteresis: hand sequence and per-segment reset. Moving time with the 0.5 m/s rule. Elapsed time includes segment gaps. Splits (exact boundaries, partial last split, untimed → null). DP keeps endpoints, ≤ 4 000 vertices for a 250k-point synthetic spiral, deterministic output, no recursion (a 250k-point straight line does not overflow the stack). Profile ≤ 601 samples, monotonic distance, interpolation.
- Demo golden values: each README file within the tolerance (AC3); the demo run's distance ↔ track within 1 %.
- `TrackStartLocal`: 2025-09-14T07:00Z → Europe/Warsaw 09:00; across the DST boundary; America/Los_Angeles previous date.
- Architecture test: Domain still has no package references.

**Api.Tests**
- Upload happy paths (recorded, planned, `rte`, untimed), with blob contents and headers checked in Azurite (`original.gpx` bytes equal the upload, `track.json` parses into `TrackGeoJson` strictly).
- Each invalid fixture → 400 `invalid-gpx` + `code` and **no blob under the user prefix, no row**.
- 413 for users (21 MB) and guests (6 MB). The other routes are still limited to 64 KB.
- 409 `track-exists` / replace (old prefix gone, new id) / `track-limit`; 429 `gpx-upload`; 503 processing-busy (slots = 0 via config); rest-day 400.
- `geojson`/`gpx` headers (Cache-Control, ETag, Content-Disposition sanitized and RFC 5987, nosniff, CSP sandbox).
- Delete track / delete run → blobs gone. A fault-injected blob delete → 503 and the row is kept.
- Isolation matrix: every track route × {guest A vs B, user U1 vs U2, guest vs user}, plus a cross-pair (own run id + foreign track id, and the reverse) → 404 with no blob read.
- Fresh guest: 6 `Processed` tracks with summaries; cleanup removes 12 blobs.
- `client-config.maps`; geocoding: `None` → 503, a fake `IGeocoder` → 200 shape, 400 validation, 429.
- 401 sweep picks up the new routes automatically; inventory unchanged (no new anonymous endpoints).

**Vitest + RTL** (MapLibre module mocked by a fake that renders `data-*` attributes and a "pick here" button; Recharts rendered in jsdom with a fixed size)
- `trackSuggestions` table cases (empty/differs/equal, date mismatch, planned vs recorded, rest day, untimed, no elevation). `applyPrefill` makes the fields dirty. A prefilled save sends the expected SI values. The location round trip keeps exact coordinates. An untouched location equals `base`.
- Upload dialog: client pre-checks (extension, size by kind of account), each problem code → message, replace confirm on 409, progress/disabled state.
- TrackViewer: hover/keyboard on the profile sets the highlight passed to `MapView`; the splits table renders; warnings are shown as text; WebGL2 missing → fallback panel.
- LocationField/picker: pick sets the coordinates, keyboard inputs validate ranges, Clear, Search hidden when `geocodingEnabled: false`, results list keyboard-selectable.
- `test:tz` covers `startLocal` display (no client-side zone math beyond formatting).

**Playwright (mocked fake API; tiles/style stubbed with `page.route("https://tiles.openfreemap.org/**")` → `e2e/fixtures/map-style.json`; any other external request fails the test)**
- `tracks.spec`: open a demo race → track view: map `data-map-state=ready`, vertices > 0, start/finish markers, profile visible; keyboard on the profile moves the highlight; download triggers a file named `gliwice-10k.gpx`; upload `road-5k.gpx` to a planned run → prefill dialog defaults → Apply → edit form dirty → Save → details updated; replace with confirmation; delete with confirmation; invalid file → message.
- `location.spec`: pick on map, keyboard coordinates, name, save; search (fake with `geocodingEnabled: true`).
- `tracks.phone.spec` (360×640, LA zone): bottom sheets, no horizontal scroll, map ≥ 240 px.
- axe on the details-with-track, track view, upload, prefill and picker.

**Full-stack (main only):** guest → upload `tests/fixtures/gpx/road-5k.gpx` to a seeded planned run → summary shown → prefill → save → run distance updated → delete the track. MapLibre style stubbed the same way.

**Manual:** real OpenFreeMap tiles in `npm run dev` (attribution visible), phone Safari over the LAN (upload from the Files app, pinch-zoom), the 20 MB upload time on the AppHost. With Q1: Azure Maps tiles via SAS, geocoding results for "Gliwice Rynek".

## 7. Dependencies

### npm (`web/`; pin exact versions at install, check React 19.3 peer ranges)
| Package | Dep | Why |
|---|---|---|
| `maplibre-gl` (6.x) | prod (lazy chunk) | Map engine for the default provider (brief's fallback; free, no key); renders GeoJSON natively. |
| `recharts` (3.x) | prod (lazy chunk) | Elevation profile (named in the brief); reused by the M5 dashboard. |
| `azure-maps-control` (3.x) | prod (lazy chunk) — **only if step 8 runs** | Azure Maps Web SDK v3 for the Azure provider with SAS auth (brief). |

**Rejected:** `react-map-gl`/`react-azure-maps` (a thin wrapper of our own is enough), `@turf/*` (metrics are server-side), MSW (ADR 0019), any client GPX parser, `@types/geojson` unless `maplibre-gl` types require it (then dev-only, one line).

### NuGet
**None for the core.** `XmlReader`, `System.Text.Json` and `SHA256` are in the BCL; spatial uses the existing NTS mapping. Step 8 only: `Azure.Identity` is used directly (`DefaultAzureCredential`). It is already transitive through `Aspire.Azure.Storage.Blobs`; if referenced directly, add a central pin (same version). **Rejected:** `NetTopologySuite.IO.GPX`/`…GeoJSON` (Domain stays package-free; output is small), `Azure.ResourceManager.Maps` (one ARM REST call), `Azure.Maps.Search` (one GET; SDK churn).

## 8. What the user must do

1. **Nothing in the cloud for M4 acceptance.** No Azure resources, keys or tenants are needed. Local development needs internet only to see real OpenFreeMap tiles; tests need none.
2. Answer the §9 questions, especially **Q1** (Azure Maps account now or in M7).
3. If Q1 = yes, **you** create (or explicitly confirm that an agent may create) in your subscription, after reviewing the commands we give in step 8:
   - one Azure Maps account (Gen2, a non-`global` location, e.g. `westeurope`);
   - one user-assigned managed identity attached to it;
   - role assignments: you as *Azure Maps Search and Render Data Reader* for local geocoding via `az login`, the UAMI with the render-only custom role, and you with `listSas` for local SAS minting;
   - CORS origin `http://localhost:5173`.

   Then run `dotnet user-secrets set "Maps:AzureMaps:AccountClientId" …` (and the other non-secret ids) in the AppHost. **No keys.** Cost: within the Gen2 free monthly quota for this use (⚠ to be re-checked in the M7 estimate).
4. Optional: try a GPX from your own watch locally (it is not committed; personal data stays on your machine).

## 9. Open questions — answered by the user (2026-11-03)

1. **Azure Maps account**: not now — Azure Maps adapter, `GET /api/maps/token` and the Azure geocoder move to **M7**; step 8 is skipped in M4. No cloud resources in M4.
2. **Production map provider**: decided in M7 (both stay behind `MapView`).
3. **Duration from GPX**: elapsed time.
4. **Limits**: defaults kept (users 20 MB / 2,000 tracks / 30 uploads per hour; guests 5 MB / 16 tracks / 10 uploads per hour).
5. **Recorded track on a Planned run**: allowed, with a "mark as completed" suggestion.
6. **Reverse geocoding of the GPX start point**: deferred.
7. **Full-stack CI journey**: extended with a GPX upload.

## 10. Assumptions

1. Synchronous processing is fast enough for 20 MB files (< 1 s CPU). There is no queue or worker (ADR 0020).
2. Existing local `Pending` demo tracks (pre-M4 guests) are not reprocessed. They show "not processed" and expire with their guests within 24 h. No production data exists.
3. The demo GPX timestamps stay in 2025; only metrics and geometry are shown for demo tracks, and the demo run values already match the files.
4. The run's zone (not the track's location) converts GPX UTC times. There is no tz lookup from coordinates.
5. GPX `name`/`desc`/`extensions` (HR, cadence) are ignored in M4. Heart rate from GPX could fill `avgHeartRate` later.
6. The OpenFreeMap public instance is acceptable for development use; tests never call it.
7. The map is the only WebGL user. Browsers without WebGL2 get a text-only experience.
8. `PUT /api/runs/{id}` is unchanged; the prefill uses it as is.
9. The new routes go through the existing fallback policy and general limiter. No new anonymous endpoint (the inventory is unchanged except for `client-config`'s new field).
10. Units stay metric in the UI; pace is derived and never stored.

## 11. Docs checked (2026-11-03)

- Azure Maps map control v3 (npm/CDN, `authOptions`; updated 2026-09-30): https://learn.microsoft.com/azure/azure-maps/how-to-use-map-control
- `atlas.AuthenticationOptions` (`sas`, `anonymous`, `getToken`): https://learn.microsoft.com/javascript/api/azure-maps-control/atlas.authenticationoptions
- Azure Maps authentication (Entra, SAS via UAMI, `listSas` parameters, `maxRatePerSecond`, `jwt-sas`, roles, CORS; updated 2026-09-15): https://learn.microsoft.com/azure/azure-maps/azure-maps-authentication
- Maps management `listSas` (2023-06-01): https://learn.microsoft.com/rest/api/maps-management/accounts/list-sas
- Search Get Geocoding **2026-01-01** (updated 2026-10-23): https://learn.microsoft.com/rest/api/maps/search/get-geocoding
- Web SDK v1/v2 retirements: https://azure.microsoft.com/updates/azure-maps-web-sdk-map-controls-version-1xx-will-be-retired-on-19-september-2026 · https://azure.microsoft.com/en-us/updates/azure-maps-web-sdk-map-control-version-20x-and-azure-maps-service-module-will-be-retired-on-30-september-2026/
- MapLibre GL JS v6 (2026-07-22; ESM-only, WebGL2): https://maplibre.org/news/2026-08-02-maplibre-newsletter-jul-2026/
- OpenFreeMap terms/attribution: https://openfreemap.org
- `XmlReaderSettings.MaxCharactersInDocument` (.NET 10): https://learn.microsoft.com/dotnet/api/system.xml.xmlreadersettings.maxcharactersindocument?view=net-10.0
- Chrome SwiftShader WebGL fallback removal / `--enable-unsafe-swiftshader`: https://chromium.googlesource.com/chromium/src/+/da170c2267b2201954efe1c8fa5d2d89ab09f7bf/docs/gpu/swiftshader.md · https://developer.samsung.com/browser/policy/enable-unsafe-swift-shader.html (secondary)

**Not verified / secondary only:**
- ASP.NET Core 10 minimal-API file upload page (official URL 404): antiforgery requirement for `IFormFile`, `WithFormOptions`, default multipart limit, spooling threshold. Community sources only → step-3 spike.
- **Recharts** current 3.x version and React 19.3 peer range (only 2025 versions found) → check at install.
- Azure Blob SDK `BlobHttpHeaders` (content type/disposition): not re-fetched. Stable API, already used in `AzureBlobStore`; the API sets response headers itself.
- Exact OpenFreeMap style URL, MapLibre v6 CSP/worker needs, `azure-maps-control` 3.x size, Gen2 pricing/free quota, `signingKey: managedIdentity` with `disableLocalAuth`.
- Whether the current Playwright Chromium enables SwiftShader WebGL by default (one secondary source says it does) → step-4 spike.

## 12. Implementation notes & deviations (2026-11-03)

Step 8 (Azure Maps adapters) was **skipped** per §9 Q1; its contracts stay in ADR 0022. ADRs 0020–0023 carry an "Amended 2026-11-03 (implementation)" section for every point below that changes a decision.

| # | Deviation from this plan | Reason |
|---|---|---|
| 1 | **Domain pipeline (step 1)** as planned, with these details: an extra rejection code **`tooComplex`** (depth > 16, > 1 500 000 elements, or > **`MaxSegments` = 1 000** `trkseg`/`rte` sections); timestamps outside **1990-01-01 … now + 1 day**, missing on some points or decreasing make the whole track **untimed** (`timestampsIgnored`), not rejected; `rte` points are used **only when the file has no `trk` points**; a DOCTYPE is reported as `dtdProhibited` by a **byte scan of the first 8 KB** after the parser fails (the scan only picks the code; `DtdProcessing.Prohibit` is what blocks it); an empty file → `unsupportedFormat`. File-name normalization also strips Unicode **format characters** (bidi overrides, zero-width). | Bounded work for adversarial structure; accurate codes without echoing parser text. |
| 2 | **Demo data:** `seed/demo/README.md` and `runs.json` gain/loss values were updated to the pipeline's output; `WarningFlags` is a **non-null `tinyint`** (0 = none). `DemoDatasetLoader` parses and processes each demo file **once per process**; a fresh guest gets 6 `Processed` rows and **12 blobs** (original + `track.json`). | Golden values must be what the code computes (AC3). |
| 3 | **Upload reads the form itself** (`HttpRequest.ReadFormAsync`) instead of `[FromForm] UploadTrackForm` binding. `UploadTrackForm` exists only to document the body (`.Accepts<UploadTrackForm>("multipart/form-data")`). The handler returns field-keyed 400s (`kind`, `file`, `replace`), parses `kind` **strictly** (names only, case-sensitive, no numbers), requires exactly one file in `file`, and answers **415** for a non-multipart body. | Binding failures gave generic 400s and read the body before the caller-specific size check. |
| 4 | **`WithFormOptions` does not exist for minimal APIs in .NET 10**: `FormOptions` is configured **globally** in `Program.cs` (`MultipartBodyLengthLimit` = max file limit + 64 KB, `ValueCountLimit` 16, `ValueLengthLimit` 1 024). The upload is the only form endpoint. | API not available. |
| 5 | **Body limit:** the route carries `EndpointBodyLimit` (`IRequestSizeLimitMetadata`) = max(user, guest file limit) + 64 KB, honoured by `RequestBodyLimit`. The handler then **lowers the request's `MaxRequestBodySize` to the caller's own limit** + 64 KB and checks `Content-Length` before reading, so a chunked guest body stops at 5 MB. | Guests must not be able to stream 20 MB. |
| 6 | **Preflight before the body:** ownership (404), rest day (400) and an already-full cap (409 `track-limit`, for a run with no track to replace) are checked **before the multipart form is read**; `kind`-dependent checks (`track-exists`) follow after reading. | Cheap rejections cost no upload bandwidth. |
| 7 | **Concurrency:** every change to a user's set of tracks (insert, replace, run delete) takes a per-user SQL Server application lock (`sp_getapplock`, resource `user-tracks:{userId:N}`, transaction-owned, 15 s timeout). The **cap is re-counted inside the insert transaction**. A run delete re-reads the run's tracks under the lock; tracks committed by a racing upload are **cleaned up best effort after commit**. Replacing a track that was deleted meanwhile → **404**; one replaced concurrently → 409 `track-exists`. | Review finding: cap and run-delete races. |
| 8 | **New problem type `urn:runnersdiary:problem:storage-unavailable`** (503, `Retry-After: 5`) for blob failures on upload and delete (track and run). Upload rolls back the new track's prefix on storage failure, DB failure **and cancellation** (rollback ignores the request token). | One retryable type for storage outages. |
| 9 | **Rate limits:** the `gpx-upload` and `geocode` endpoint policies **replace** the general `api` token bucket on their routes (the most specific policy metadata wins); a test pins this. Development raises them ×10. | ASP.NET Core applies one policy per endpoint. |
| 10 | **No `listTracks` route** (never planned as a route, noted for clarity): the SPA uses `RunDto.tracks[]` + `getTrack`. `RunDto.tracks[]` has **no summary**; the viewer and the prefill dialog fetch `getTrack`. | Keeps run lists small. |
| 11 | **Generated TS types `file` as `string`**: `uploadTrack.ts` builds `FormData` and passes it via `fetchClient` with `bodySerializer: (b) => b` (the browser sets the boundary). Downloads use `parseAs: "blob"`. | openapi-typescript multipart limitation (§3.2 fallback). |
| 12 | **Douglas–Peucker:** one stack-based pass computes each point's **significance**; the 3 m → doubling tolerance is then a threshold over those values (no re-simplification per tolerance). A budget of **5·10⁷ point-to-chord evaluations** per track bounds adversarial zigzags; when exhausted the line is **decimated uniformly** (ends kept, ≤ 4 000 vertices). Processing honours the **request cancellation token**, so an aborted upload frees its processing slot. | Review finding: quadratic worst case under the processing semaphore. |
| 13 | **GeoJSON caching:** `Cache-Control: private, no-cache` + `ETag: "{first 8 bytes of SHA-256 as hex}-v{ProcessorVersion}"`, `If-None-Match` → 304. Not `max-age=86400, immutable`. The SPA still uses `staleTime: Infinity` (`gcTime` 30 min) in memory. | The browser cache must not serve a track's geometry after sign-out; every use revalidates through the authenticated API. |
| 14 | `StartUtc`/`EndUtc` are **rounded to whole seconds** (half up, as `datetime2(0)`) in `GpxTrack.CreateProcessed`, so the 201 response equals a later GET. | Review finding. |
| 15 | `XmlReaderSettings.MaxCharactersFromEntities = 1024` (not 0: **0 means unlimited**). DTDs are prohibited regardless. | Correct defence in depth. |
| 16 | `Maps:MapLibre:StyleUrl` must be an **absolute `https` URL** and `Maps:Provider` must be `MapLibre` (Azure Maps deferred) — both **fail startup** otherwise. | Bad values surface at deploy, not in the browser. |
| 17 | **`client-config.gpx = { maxFileBytesUser, maxFileBytesGuest }`** added (anonymous endpoint, so both values); the upload dialog pre-checks with the caller's value. | Client pre-check matches server configuration. |
| 18 | **MapLibre 6.16.0** renders in headless Chromium with the default flags (SwiftShader); `--enable-unsafe-swiftshader` is kept in both Playwright projects as insurance. Playwright uses the **real map** with the stubbed style. The worker is loaded via `?url` + `setWorkerUrl` (same-origin file, so M7 CSP can use `worker-src 'self'`, no `blob:`). The MapLibre CSS position override is fixed inline; attribution links are underlined (contrast/a11y). | Spike outcome (§5 risk 1). |
| 19 | **Recharts 3.10.1** with an explicit **`react-is` 19.3.1** pin (peer of Recharts, aligned with React 19.3). Profile keyboard access is a **native `<input type="range">` over the chart** (not Recharts `accessibilityLayer`); arrow keys move the map marker. | Native control: predictable keyboard behaviour. |
| 20 | i18n namespaces **`maps`** and **`tracks`** are registered lazily (`web/src/i18n/lazyBundles.ts`, imported by the lazy chunks). Vitest setup uses Node's `FormData`/`Blob`/`File` instead of jsdom's (multipart through the fake API). | Initial bundle budget. |
| 21 | Location picker start: existing pin → last pick in this tab (sessionStorage `rd.lastMapPick`) → Gliwice. **Not** the GPX start point. Longitude from map clicks is wrapped to −180…180. `ResponsiveDialog` gained `returnFocusTo` for nested dialogs. | Simpler; GPX start is offered by the prefill instead. |
| 22 | Frontend follow-ups (reported done): a "file missing" state when the original/GeoJSON blob is gone (404 on an existing track); download object-URL revocation delayed until the download has started; `map.spec` flake fixed; upload dialog limits from `client-config.gpx`. A map **harness page exists only in tests**. | — |
| 23 | **Bundle:** initial load **178.7 kB gzip** vs a **201.5 kB** limit (was 200.6 kB before the review fixes made the month/year picker popover lazy) (`web/scripts/bundle-budget.mjs`, M3 baseline 201 242 B ≈ 196.5 kB + 5 kB); MapLibre, Recharts and the track/location UI are lazy. CI frontend job runs `npm run bundle-budget` after the build. | AC14. |
| 24 | **QA:** real-backend verification (multipart boundary, 201 + `Location`, every `invalid-gpx` code from the server, exact 5 MB guest limit); new full-stack spec **`web/e2e-fullstack/tracks.spec.ts`** next to the extended `journey.spec.ts`. | — |

**Verified (2026-11-04, quiet machine):** .NET **943/943** (`CI=true`, Docker); Vitest **685/685** in 5 consecutive runs; `test:tz` **512** per zone; Playwright **76/76** three times in a row (`chromium` + `phone-la`, real MapLibre, axe) and `map.spec.ts --repeat-each=20` **160/160**; full-stack **4/4** twice against a local replica of the CI `e2e-fullstack` job (fresh SQL + Azurite, API in Release, `vite preview`); `dotnet build` warning-free; `RunnersDiary.Domain` has no package references; OpenAPI + TS schema regenerated without drift; bundle budget passes (178.7 kB). Earlier failures under machine load (~50 load average from parallel agents) did not reproduce on a quiet machine. Two full-stack test bugs found by the replica run and fixed: the foreign-host check assumed `localhost` (CI serves on `127.0.0.1:4173`), and the delete assertions passed while the confirmation dialog still hid the list, letting the reload abort the in-flight DELETE.

**Review follow-ups applied before commit:**
single-pass significance Douglas–Peucker with evaluation budget, uniform-decimation fallback and cancellation; blob rollback on cancellation; GeoJSON `private, no-cache` + ETag instead of `immutable`; per-user `sp_getapplock` for track insert and run delete with the cap re-counted in the insert transaction and late tracks cleaned after a run delete; preflight ownership/rest-day/cap checks before reading the form; per-request body limit = caller's limit; replace of a vanished track → 404; `StartUtc`/`EndUtc` whole seconds; `MaxCharactersFromEntities` 1 024; Unicode format characters stripped from file names; https-only `StyleUrl`; `client-config.gpx`.

**Still open:**
- **M7:** Azure Maps adapter (`AzureMapsMap.tsx`), `GET /api/maps/token` + `listSas` issuer, `AzureMapsGeocoder` (real place search), reverse geocoding (post-MVP).
- **M7 CSP** for the web tier: `worker-src 'self'`; `connect-src` the tile host plus the sprite/glyph hosts named by the style; `img-src blob: data:` (to verify); style attributes used by the markers (`style-src` needs to allow them, or the markers move to classes).
- **Privacy note** (M7 docs/UI): tile requests reveal the viewed area to OpenFreeMap (no cookies, no user id).
- **No orphan-blob sweep for users** (ADR 0023 §5): orphans only from a failed best-effort delete (logged `TrackBlobOrphaned`).
- **Unverified:** a chunked over-limit body against real Kestrel (413 path via `BadHttpRequestException`; tests use `TestServer`); a manual 20 MB user upload on the AppHost (time and memory).
- `GpxTrack.MaxSizeBytes` (20 MB) is a **Domain constant**: raising `GpxLimits:MaxFileBytes` above 20 MB would fail in `CreatePending`. Keep them in step (backend-dotnet, if limits ever change).
