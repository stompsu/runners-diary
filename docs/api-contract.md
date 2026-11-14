# Runners Diary – API contract

Living document, owned by the `architect` agent. Status: **M2–M6 implemented (merged); M7 Part A adds `GET /api/maps/token`, the geocode daily budget (429 `code: geocode-budget`), `client-config.maps.azureMapsDomain` and the anonymous/authenticated split of `/api/health` (implemented, awaiting approval; nothing deployed)**. **M8 (race proposals) adds the `/api/races` endpoints, `client-config.races`, `RunDto.catalogRaceId` and `--job race-import` (implemented on `m8-race-proposals`; the SPA calendar/details UI is in progress; not merged): see [Added in M8](#added-in-m8-race-proposals).**
Source of truth at runtime is the committed OpenAPI document `src/RunnersDiary.Api/openapi/runnersdiary-api.json` → `web/src/api/generated/schema.d.ts` ([ADR 0002](adr/0002-openapi-typescript-client.md)). This page states intent; if they disagree, fix one of them in the same PR.

## Conventions

- Everything under `/api`; JSON camelCase; enums as strings; units SI (`…Meters`, `…Seconds`).
- Dates `"YYYY-MM-DD"`, times `"HH:mm"` (custom `TimeOnly` converter; OpenAPI `pattern: ^\d{2}:\d{2}$`), UTC instants ISO 8601 with `Z`, time zones IANA ([ADR 0008](adr/0008-run-date-and-time-storage.md)).
- Unknown JSON properties → 400 (no mass assignment). No request ever carries a user id.
- Auth: `Authorization: Bearer <token>` — Entra External ID access token (scope `access_as_user`) or guest token ([ADR 0009](adr/0009-authentication-entra-and-guest-jwt.md)). Policy column: **Auth** = any authenticated caller (user or guest); **Anon** = allowlisted anonymous.
- Errors: RFC 9457 `application/problem+json` everywhere ([ADR 0007](adr/0007-api-surface-conventions.md)).

| Status | When | `type` |
|---|---|---|
| 400 | Validation (shape or domain rule); `errors` map keyed by camelCase JSON path (see below) | default (RFC 9110 §15.5.1) |
| 401 | Missing/invalid/expired token (incl. expired or deleted guest); `WWW-Authenticate: Bearer` | default |
| 404 | Not found **or owned by someone else** (indistinguishable) | default |
| 409 | `version` mismatch on update | `urn:runnersdiary:problem:concurrency-conflict` |
| 400 | Rejected GPX file (M4); extension `code`, `errors.file` | `urn:runnersdiary:problem:invalid-gpx` |
| 409 | Run cap reached (total per account or per date, see *Limits*) | `urn:runnersdiary:problem:run-limit` |
| 409 | Track of that kind exists and `replace` is not `true` (M4) | `urn:runnersdiary:problem:track-exists` |
| 409 | Track cap per account reached (M4) | `urn:runnersdiary:problem:track-limit` |
| 413 | Request body > `RequestLimits:MaxBodyBytes` (default 64 KB), any `/api` route incl. `POST /api/guest-sessions`; the GPX upload has its own limit (see M4) | default |
| 415 | GPX upload body is not `multipart/form-data` (M4) | default |
| 429 | Rate limit; `Retry-After` header. M7: the shared daily geocode budget answers with the same type plus extension `code: "geocode-budget"` | `urn:runnersdiary:problem:rate-limited` |
| 503 | Guest capacity reached / seeding busy / seeding failed; `Retry-After` | `urn:runnersdiary:problem:guest-capacity` |
| 503 | GPX processing slots full; `Retry-After: 10` (M4) | `urn:runnersdiary:problem:processing-busy` |
| 503 | Blob storage failed on track upload/delete or run delete; nothing changed; `Retry-After: 5` (M4) | `urn:runnersdiary:problem:storage-unavailable` |
| 503 | Optional feature not configured or its provider failed (geocoding, M4; AI summary generation not configured, switched off or not authenticated, M6; Azure Maps token when display is off or issuing failed, M7) | `urn:runnersdiary:problem:feature-unavailable` |
| 409 | An AI summary generation of this account is already running (M6) | `urn:runnersdiary:problem:summary-in-progress` |
| 429 | AI summary quota reached (M6); extensions `scope`, `limit`, `resetsAt` | `urn:runnersdiary:problem:ai-quota` |
| 502 | AI summary could not be generated (M6); extension `reason` | `urn:runnersdiary:problem:summary-failed` |
| 503 | AI generation slots full or the model provider is throttling (M6); `Retry-After` | `urn:runnersdiary:problem:ai-busy` |
| 500 | Unhandled; no details outside Development | default |

**400 `errors` keys.** DataAnnotations and domain-rule errors: camelCase JSON path of the member (`race.resultTimeSeconds`, `rating.stars`). Malformed, incomplete or unknown-member JSON: the JSON path where reading failed (`body` when there is none) with a fixed generic message. Route/query binding failures: the parameter name (`from`, `id`) with a fixed message. Framework/serializer exception texts are never echoed.

**Unmatched routes.** An unknown `/api/*` path answers **401** without a valid token (the fallback policy runs before routing can tell the path is unknown) and **404** with one. `/openapi/v1.json` is served (anonymously) in Development only.

## Endpoints (M2)

| Method & path | operationId | Policy | Success | Errors |
|---|---|---|---|---|
| `GET /alive`, `GET /health` | — (not in OpenAPI) | Anon | 200/503 text | — |
| `GET /api/health` | `getApiHealth` | Anon (token optional; M7: rate policy `health`, see *Added in M7*) | 200 `ApiHealthResponse` | 429 (M7), 503 |
| `POST /api/guest-sessions` | `createGuestSession` | Anon, rate policy `guest-create` + global window | 201 `GuestSessionResponse` (`Cache-Control: no-store`) | 413, 429, 503 |
| `GET /api/me` | `getMe` | Auth | 200 `MeResponse` | 401 |
| `PUT /api/me` | `updateMe` | Auth | 200 `MeResponse` | 400, 401 |
| `GET /api/run-types` | `listRunTypes` | Auth | 200 `RunTypeDto[]` | 401 |
| `GET /api/runs?from&to` | `listRuns` | Auth | 200 `RunListResponse` | 400 (`to < from`, span > 92 days), 401 |
| `GET /api/runs/{id}` | `getRun` | Auth | 200 `RunDto` | 401, 404 |
| `POST /api/runs` | `createRun` | Auth | 201 `RunDto` + `Location: /api/runs/{id}` | 400, 401, 409 (run-limit) |
| `PUT /api/runs/{id}` | `updateRun` | Auth | 200 `RunDto` | 400, 401, 404, 409 (concurrency; run-limit when `date` changes) |
| `DELETE /api/runs/{id}` | `deleteRun` | Auth | 204 (M4: track blobs deleted first, then the run; rows cascade) | 401, 404, 503 `storage-unavailable` (M4: nothing removed) |
| `GET /api/calendar/months/{year}/{month}` | `getCalendarMonth` | Auth | 200 `CalendarMonthResponse` | 400 (year ∉ 1950–2100, month ∉ 1–12), 401 |

All `/api` endpoints except the Anon ones (these two and, since M3, `GET /api/client-config`) also pass the general per-caller rate limiter (429). Exception (M4): `uploadTrack` and `searchPlaces` use their own policy (`gpx-upload`, `geocode`) **instead of** the general one (likewise M6 `ai-summary`, M7 `maps-token`; M7 also gives the anonymous `getApiHealth` its own `health` policy) — ASP.NET Core applies the most specific policy per endpoint. Every `/api` route (Anon included) may answer 413.

### Limits (configuration, defaults in `appsettings.json`)

| Limit | Default | Config key | Response |
|---|---|---|---|
| Request body size, all `/api` routes except the GPX upload | 64 KB (65 536 bytes) | `RequestLimits:MaxBodyBytes` | 413 |
| GPX upload body (M4) | file limit of the caller + 64 KB multipart overhead (route ceiling: max(user, guest) + 64 KB) | `GpxLimits:MaxFileBytes`, `GuestMaxFileBytes` | 413 |
| GPX file size (M4) | users 20 MB (20 971 520 B), guests 5 MB (5 242 880 B) | `GpxLimits:MaxFileBytes`, `GuestMaxFileBytes` | 413 |
| Tracks per account (M4) | users 2 000, guests 16 | `GpxLimits:UserMaxTracks`, `GuestMaxTracks` | 409 `track-limit` |
| GPX points per file (M4) | 250 000 | `GpxLimits:MaxPoints` | 400 `invalid-gpx` (`tooManyPoints`) |
| Concurrent GPX processing per replica (M4) | 2, wait ≤ 5 s | `GpxLimits:MaxConcurrentProcessing`, `ProcessingWait` | 503 `processing-busy` |
| GPX uploads per caller, sliding window (M4, policy `gpx-upload`) | users 30 / h, guests 10 / h (Development: 300 / 100) | `RateLimiting:GpxUpload:UserPermitLimit`, `GuestPermitLimit`, `Window` | 429 + `Retry-After` |
| Place searches per caller, sliding window (M4, policy `geocode`) | users 20 / min, guests 10 / min (Development: 200 / 100) | `RateLimiting:Geocode:UserPermitLimit`, `GuestPermitLimit`, `Window` | 429 + `Retry-After` |
| Guest creations per client (IPv4 address or IPv6 /64), sliding window | 5 / h (Development: 100) | `RateLimiting:GuestCreate:PermitLimit`, `Window` | 429 + `Retry-After` |
| Guest creations, global (all clients, per replica), sliding window | 60 / h (Development: 1000) | `RateLimiting:GuestCreate:GlobalPermitLimit`, `GlobalWindow` | 429 + `Retry-After` |
| Active guests | 200 | `Guests:MaxActive` | 503 + `Retry-After` |
| General per caller (`rd_uid`, else client IP), token bucket | 120, +60 / 30 s | `RateLimiting:General:*` | 429 + `Retry-After` |
| Runs per account | guests 500, users 20 000 | `RunLimits:GuestMaxRuns`, `UserMaxRuns` | 409 `run-limit` (POST) |
| Runs per date | 20 | `RunLimits:MaxRunsPerDate` | 409 `run-limit` (POST; PUT that changes `date`) |

The per-client guest limit is applied first, so one client exhausting its own budget does not consume the global one. Counters and caps are in-memory per replica (ADR 0011).

## Shapes

```ts
// POST /api/guest-sessions  (no body)
GuestSessionResponse = { accessToken: string; tokenType: "Bearer"; expiresAt: string /* UTC */; userId: string }

MeResponse = {
  id: string; kind: "User" | "Guest"; displayName: string | null;
  timeZoneId: string; timeZoneIsDefault: boolean; unitSystem: "Metric" | "Imperial";
  today: string;                 // "YYYY-MM-DD" in the user's zone (default month for the calendar)
  guestExpiresAt: string | null  // UTC, guests only (demo banner)
}
UpdateMeRequest = { displayName?: string | null /* ≤100 */; timeZoneId: string /* IANA */; unitSystem: "Metric" | "Imperial" }

RunTypeDto = { id: number; code: string; category: "Race" | "Training" | "Other";
               countsTowardRunningDistance: boolean; countsTowardLoad: boolean; isRestDay: boolean; sortOrder: number }

RunWriteRequest = {
  date: string; startTime?: string | null;
  timeZoneId?: string | null;   // omitted/null: POST → caller's profile zone; PUT → keeps the run's current zone
  status: "Planned" | "Completed"; runTypeId: number;
  title?: string | null /* ≤120 */; notes?: string | null /* ≤4000 */;
  location?: { latitude: number /* −90..90 */; longitude: number /* −180..180 */; name?: string | null /* ≤200 */ } | null;
  distanceMeters?: number | null; durationSeconds?: number | null;
  elevationGainMeters?: number | null; elevationLossMeters?: number | null;
  avgHeartRate?: number | null; maxHeartRate?: number | null; rpe?: number | null /* 1..10 */;
  gear?: string | null /* ≤100 */; weather?: string | null /* ≤200 */;
  surface?: "Road" | "Trail" | "Track" | "Treadmill" | "Mixed" | null;
  race?: {
    name: string /* ≤150 */; distanceCategory: "FiveK" | "TenK" | "HalfMarathon" | "Marathon" | "Other";
    officialDistanceMeters?: number | null; bibNumber?: string | null /* ≤20 */;
    targetTimeSeconds?: number | null; resultTimeSeconds?: number | null;
    overallPlace?: number | null; categoryPlace?: number | null; categoryName?: string | null /* ≤50 */;
    resultsUrl?: string | null /* absolute http(s), ≤2048 */
  } | null;
  rating?: { stars: number /* 1..5 */; comment?: string | null /* ≤500 */ } | null;
  version?: string | null   // required on PUT (from RunDto.version)
}

RunDto = RunWriteRequest-fields (resolved: timeZoneId always set) & {
  id: string; startUtc: string | null; version: string /* base64 rowversion */;
  tracks: { id: string; kind: "PlannedCourse" | "RecordedTrack"; status: "Pending" | "Processed" | "Failed";
            originalFileName: string; createdAt: string /* UTC */ }[];   // M4 added the last two; no summary (use getTrack)
  createdAt: string; updatedAt: string
}

RunListResponse = { from: string; to: string; items: RunDto[] }   // ordered by date, then startUtc (nulls last), then createdAt

CalendarMonthResponse = {
  year: number; month: number;
  gridStart: string; gridEnd: string;      // Monday on/before the 1st … Sunday on/after the last day (4–6 ISO weeks)
  runs: RunDto[];                          // all runs in [gridStart, gridEnd]
  weeks: {
    isoYear: number; isoWeek: number; start: string; end: string;
    completed: Totals; planned: Totals
  }[]
}
Totals = { runCount: number; distanceMeters: number; durationSeconds: number /* int64 */; elevationGainMeters: number }
// Totals include only run types with countsTowardRunningDistance (cross-training/rest excluded); computed in Domain.
```

### Domain rules surfaced as 400 (`RunRules`, ADR 0012)

- `rating` only when `status = Completed`; `race.resultTimeSeconds`, `overallPlace`, `categoryPlace` only when Completed.
- `race` only when the run type's category is `Race`.
- Rest-day type: no distance/duration/elevation/HR/RPE/race/rating.
- `maxHeartRate ≥ avgHeartRate`; `elevation*` ≥ 0; `distanceMeters` > 0.
- `timeZoneId` is a valid IANA id in Area/Location form (`Europe/Warsaw`, `Etc/UTC`) or bare `UTC`/`GMT`, ≤ 64 chars; Windows ids are rejected even where the runtime could resolve them; `date` within 1950-01-01 … 2100-12-31.
- Status may change in either direction via `PUT` (full replace); the client sends `rating: null` when moving back to Planned.

## Added in M3 ([plan](plans/M3-calendar-ui.md), [ADR 0015](adr/0015-spa-runtime-configuration.md))

| Method & path | operationId | Policy | Success | Errors |
|---|---|---|---|---|
| `GET /api/client-config` | `getClientConfig` | Anon (no rate limiting, no DB; response built once at startup. Until M7 it shared the anonymous group with health; health is rate-limited since M7) | 200 `ClientConfigResponse`, `Cache-Control: public, max-age=300` | — |

```ts
ClientConfigResponse = {
  entra: { clientId: string /* Auth:Entra:SpaClientId (trimmed) */; authority: string /* https://<TenantSubdomain>.ciamlogin.com/ */;
           apiScope: string /* Auth:Entra:ApiScope, default api://<Audience>/<RequiredScope> = …/access_as_user */ } | null
           // null: Entra disabled, or enabled without Auth:Entra:SpaClientId (startup warning, not a failure)
  maps: { ... };   // M4, see below
  gpx:  { ... }    // M4, see below
}
```

The SPA never sends a token to this endpoint (nor to `/api/health` or `POST /api/guest-sessions`).

## Added in M4 ([plan](plans/M4-maps-gpx.md), ADRs [0020](adr/0020-gpx-upload-and-safe-parsing.md)–[0023](adr/0023-run-tracks-prefill-and-deletion.md))

All track routes are **Auth**, under `/api/runs/{runId}/tracks`, and answer **404** for a missing or foreign run, a missing or foreign track, and a track id combined with another run's id (indistinguishable). Blob names always come from the row, never from the request.

| Method & path | operationId | Policy | Success | Errors |
|---|---|---|---|---|
| `POST /api/runs/{runId}/tracks` | `uploadTrack` | Auth, rate policy `gpx-upload` (replaces the general one); antiforgery disabled (bearer only) | 201 `TrackDto` + `Location: /api/runs/{runId}/tracks/{id}` | 400 (`invalid-gpx`; field errors `kind`/`file`/`replace`; rest day `errors.runId`), 401, 404, 409 (`track-exists`, `track-limit`), 413, 415, 429, 503 (`processing-busy`, `storage-unavailable`) |
| `GET /api/runs/{runId}/tracks/{trackId}` | `getTrack` | Auth | 200 `TrackDto` | 401, 404 |
| `GET /api/runs/{runId}/tracks/{trackId}/geojson` | `getTrackGeoJson` | Auth | 200 `TrackGeoJson`, `Content-Type: application/geo+json`, `Cache-Control: private, no-cache`, `ETag`; **304** on a matching `If-None-Match` | 401, 404 (also when not `Processed` or the blob is missing) |
| `GET /api/runs/{runId}/tracks/{trackId}/gpx` | `downloadTrackGpx` | Auth | 200 original bytes, `Content-Type: application/gpx+xml`, `Content-Disposition: attachment; filename="<ascii>"; filename*=UTF-8''<pct-encoded>`, `X-Content-Type-Options: nosniff`, `Content-Security-Policy: sandbox`, `Cache-Control: private, no-store` | 401, 404 (also when the blob is missing) |
| `DELETE /api/runs/{runId}/tracks/{trackId}` | `deleteTrack` | Auth | 204 (blobs first, then the row) | 401, 404, 503 `storage-unavailable` (row kept) |
| `GET /api/geocoding/search?q&lat&lon` | `searchPlaces` | Auth, rate policy `geocode` (replaces the general one) | 200 `PlaceSearchResponse` (≤ 5) | 400 (`q` 2–100 chars after trim; `lat`/`lon` together, −90…90 / −180…180), 401, 429, 503 `feature-unavailable` |
| `GET /api/client-config` (changed) | `getClientConfig` | Anon (unchanged) | 200 + `maps`, `gpx` | — |

There is no `listTracks` route: a run's tracks are in `RunDto.tracks[]`. `GET /api/maps/token` (Azure Maps SAS) was not implemented in M4; it is added in M7 (see *Added in M7*).

### Upload (`uploadTrack`)

`multipart/form-data` (the generated TS types `file` as `string`; the SPA sends `FormData` via `fetchClient` with an identity `bodySerializer`):

| Field | Required | Values |
|---|---|---|
| `kind` | yes | `PlannedCourse` \| `RecordedTrack` (exact names; numbers rejected) |
| `file` | yes | exactly one file part named `file`; name must end in `.gpx` |
| `replace` | no | `true` \| `false` (default `false`) — must be `true` to replace an existing track of that kind |

Order of checks: body size by `Content-Length` (caller's limit + 64 KB → 413) → content type (415) → run ownership (404), rest day (400), cap already full for a run without tracks (409 `track-limit`) — all **before the form is read** → form fields (400) → file length (413) → file name (`invalid-gpx`/`wrongExtension`) → `track-exists` / cap → processing slot (503) → parse (`invalid-gpx`) → blobs (503 `storage-unavailable`) → commit (cap re-counted; replacing a track deleted meanwhile → 404). A rejected or failed upload leaves no row and no blob. Upload/replace/delete never change the run row or its `version`.

`invalid-gpx` problem (400): `{ type: "urn:runnersdiary:problem:invalid-gpx", title: "Invalid GPX file", status: 400, errors: { file: ["<fixed English message>"] }, code }`. Parser text is never echoed.

| `code` | Meaning |
|---|---|
| `malformedXml` | Not well-formed XML |
| `dtdProhibited` | Contains a DOCTYPE (XXE / entity expansion impossible) |
| `unsupportedFormat` | Not plain XML (gzip/zip/binary, empty file, first non-blank byte not `<`) |
| `notGpx` | Root is not `gpx` in the GPX 1.0/1.1 namespace |
| `tooLarge` | Byte budget exceeded while reading |
| `tooManyPoints` | > 250 000 points |
| `tooFewPoints` | < 2 points (from `trk`, else `rte`) |
| `invalidCoordinate` | lat ∉ −90…90, lon ∉ −180…180, or not an invariant finite number |
| `tooComplex` | Depth > 16, > 1 500 000 elements, or > 1 000 segments/routes |
| `wrongExtension` | File name (after normalization) does not end in `.gpx` |

### Shapes

```ts
TrackDto = {
  id: string; runId: string; kind: "PlannedCourse" | "RecordedTrack"; status: "Pending" | "Processed" | "Failed";
  originalFileName: string;   // normalized: last path segment, no control/format chars or quotes, ≤ 255
  sizeBytes: number; createdAt: string /* UTC */;
  summary: null | {           // null unless Processed (pre-M4 demo rows stay Pending)
    pointCount: number; distanceMeters: number /* 0.1 m */;
    elevationGainMeters: number | null; elevationLossMeters: number | null;   // null = no elevation in the file
    minElevationMeters: number | null; maxElevationMeters: number | null;
    elapsedSeconds: number | null; movingSeconds: number | null;              // null = untimed
    startUtc: string | null; endUtc: string | null;                           // whole seconds
    start: { latitude: number; longitude: number }; finish: { latitude: number; longitude: number };
    startLocal: { date: string; time: string /* HH:mm */; timeZoneId: string } | null   // startUtc in the run's current zone, computed on read
  };
  warnings: ("timestampsIgnored" | "noElevation" | "multipleSegments" | "routeConverted")[]   // [] unless Processed
}

TrackGeoJson = {              // RFC 7946 FeatureCollection + foreign members (ADR 0021 §5); streamed from the blob
  type: "FeatureCollection"; formatVersion: 1;
  bbox: [minLon, minLat, maxLon, maxLat];
  features: { type: "Feature"; id: "line" | "start" | "finish"; properties: { role: string };
              geometry: { type: "MultiLineString" | "Point"; coordinates: unknown } }[];   // ≤ 4 000 line vertices, 6 decimals
  profile: [distanceMeters, elevationMeters, lon, lat][] | null;   // ≤ 601 samples; null without elevation
  splits: { index: number; distanceMeters: number; elapsedSeconds: number | null; elevationDeltaMeters: number | null }[]   // per km + final partial, ≤ 1 000
}

PlaceSearchResponse = { results: { name: string; latitude: number; longitude: number; type: string | null }[] }   // ≤ 5

ClientConfigResponse.maps = { provider: "MapLibre" | "AzureMaps"; mapLibreStyleUrl: string | null; geocodingEnabled: boolean;
                              azureMapsDomain: string | null /* M7 */ }
ClientConfigResponse.gpx  = { maxFileBytesUser: number; maxFileBytesGuest: number }   // both: the endpoint is anonymous
```

`ETag` of `getTrackGeoJson` is `"{hex of the first 8 bytes of the file's SHA-256}-v{processorVersion}"` (a track id's content never changes; a replace creates a new id). `client-config.maps.provider` was always `MapLibre` in M4 (M7 accepts `AzureMaps` with validated settings, see *Added in M7*); `mapLibreStyleUrl` comes from `Maps:MapLibre:StyleUrl` (absolute https, default `https://tiles.openfreemap.org/styles/liberty`); `geocodingEnabled` is `false` while `Maps:Geocoding:Provider = None`.

## Added in M5 (Insights)

[Plan](plans/M5-insights.md) (deviations [§12](plans/M5-insights.md#12-implementation-notes--deviations-2026-11-04)), ADRs [0024](adr/0024-training-metrics-definitions-v1.md) (metric definitions v1) and [0025](adr/0025-insights-report-computation-and-api.md) (computation, API, M6 contract).

| Method & path | operationId | Policy | Success | Errors |
|---|---|---|---|---|
| `GET /api/insights/report?from&to&compare` | `getInsightsReport` | Auth (users and guests), general rate limiter | 200 `InsightsReportResponse`, `Cache-Control: private, no-store` | 400 (keys `from`, `to`, `compare`), 401, 429 |

**Parameters**

| Name | Required | Values |
|---|---|---|
| `from` | yes | `YYYY-MM-DD`, 1950-01-01 … 2100-12-31 |
| `to` | yes | `YYYY-MM-DD`, 1950-01-01 … 2100-12-31, `≥ from`; span `to − from + 1 ≤ 366` days |
| `compare` | no | `none` \| `previous` (default) \| `previousYear` — a **plain string** in OpenAPI (no enum), matched **case-sensitively** (`Previous` → 400) |

- Validation: a missing or malformed date is a binding failure (`errors.from` / `errors.to`, fixed message). Range errors are reported per parameter; `to < from` and a span > 366 days are reported on `to`. An unknown `compare` → `errors.compare`. The handler's range and `compare` errors are returned together in one 400; a binding failure is answered by the framework before the handler runs.
- No route parameter names a user or a resource: the report always reads the caller's own runs (`Owner` filter), so there is no 404. An empty or wholly future period answers 200 with zeros and empty arrays.
- No server cache. The SPA invalidates `getInsightsReport` on `createRun`/`updateRun`/`deleteRun`.
- `metricsVersion` (currently `1`) changes only with a definitional change (ADR 0024 §9); additive fields keep it.

### Shapes

All units SI except load (AU = RPE × minutes) and pace (s/km). Every array is always present (possibly empty). Rounding: ADR 0024 §9.

```ts
InsightsReportResponse = {
  metricsVersion: number;                      // 1
  today: string;                               // in the caller's profile zone
  period: InsightsPeriodDto;
  thresholds: { acwrLow: number /* 0.8 */; acwrHigh: number /* 1.3 */; monotonyHigh: number /* 2.0 */; acwrMinHistoryDays: number /* 35 */ };
  totals: PeriodTotalsDto;                     // completed, [from, asOf]
  planned: { runCount: number; distanceMeters: number; durationSeconds: number /* int64 */; upcomingCount: number; missedCount: number };  // [from, to]
  comparison: InsightsComparisonDto | null;    // null for compare=none or when elapsedDays = 0
  load: {
    historyStart: string | null;               // first completed load-counting session ever (even without duration)
    atAsOf: AcwrPointDto;
    peak: AcwrPointDto | null;                 // max ACWR in [from, asOf], earliest on ties
    bandDays: { low: number; optimal: number; high: number; unavailable: number };
    daily: { date: string; load: number; acute: number; chronic: number | null; acwr: number | null; band: Band | null }[];  // [Monday(from), asOf]
  };
  weeks: InsightsWeekDto[];                    // whole ISO weeks Monday(from) … week of `to` (bucket rules: ADR 0024 amendment)
  months: InsightsMonthDto[];                  // whole months of `from` … `to`
  typeDistribution: { runTypeId: number; code: string; category: "Race" | "Training" | "Other";
                      runCount: number; distanceMeters: number; durationSeconds: number; load: number }[];   // rest excluded
  paceTrend: { runTypeCodes: string[] /* ["easy"] */; slopeSecondsPerKmPerWeek: number | null; excludedRunCount: number;
               weeks: { start: string; runCount: number; distanceMeters: number; durationSeconds: number;
                        paceSecondsPerKm: number; avgHeartRate: number | null /* 0.1 */ }[] };
  races: RaceResultDto[];
  dataQuality: { completedSessions: number /* rest excluded */; sessionsWithEstimatedRpe: number;
                 estimatedLoadShare: number /* 0..1, 2 dp */; sessionsWithoutDuration: number; runningSessionsWithoutDistance: number };
}

InsightsPeriodDto = { from: string; to: string;
  asOf: string;          // min(to, today); BEFORE `from` for a wholly future period (ADR 0025 amendment)
  isPartial: boolean;    // to > today
  days: number; elapsedDays: number /* 0 for a wholly future period */ }

PeriodTotalsDto = { runCount: number; distanceMeters: number; durationSeconds: number /* int64 */; elevationGainMeters: number;
  averagePaceSecondsPerKm: number | null; longestRunMeters: number | null;
  load: number; loadSessionCount: number; activeDays: number;
  restDays: number /* distinct dates with a completed rest entry */ }

InsightsComparisonDto = { kind: "previous" | "previousYear"; from: string; to: string /* aligned to elapsed days when partial */;
  totals: PeriodTotalsDto; deltas: DeltasDto }
DeltasDto = { [k in "runCount" | "distanceMeters" | "durationSeconds" | "elevationGainMeters" | "load" | "activeDays"]:
              { absolute: number; percent: number | null /* null when previous = 0 */ } }   // from rounded totals

AcwrPointDto = { date: string; value: number | null; band: Band | null; acute: number; chronic: number | null;
                 unavailableReason: "insufficientHistory" | "noChronicLoad" | null }
Band = "low" | "optimal" | "high"

InsightsWeekDto = { isoYear: number; isoWeek: number; start: string; end: string;
  completed: Totals; planned: Totals;          // calendar `Totals` schema (TotalsDto), running-distance types only
  load: number; monotony: number | null; strain: number | null;
  acwrAtEnd: number | null;                    // ACWR on min(Sunday, today); null for a future week
  complete: boolean }                          // Sunday ≤ today
InsightsMonthDto = { year: number; month: number; start: string; end: string; completed: Totals; planned: Totals; load: number }

RaceResultDto = { runId: string; date: string; status: "Planned" | "Completed"; runTypeCode: string; name: string;
  distanceCategory: "FiveK" | "TenK" | "HalfMarathon" | "Marathon" | "Other";
  officialDistanceMeters: number | null; distanceMeters: number | null;
  resultTimeSeconds: number | null;            // Race.ResultTimeSeconds ?? DurationSeconds (completed only)
  targetTimeSeconds: number | null; paceSecondsPerKm: number | null;
  overallPlace: number | null; categoryPlace: number | null; categoryName: string | null; ratingStars: number | null;
  isPersonalBest: boolean }                    // PB at the time (ADR 0024 §8; ties: earlier date holds it)
```

**Enum casing is mixed on purpose — do not "fix" it in M6.** Values that exist only in the insights contract are lowerCamel via dedicated DTO enums with `JsonStringEnumMemberName`: `band` (`BandDto`), `unavailableReason` (`AcwrUnavailableReasonDto`), `comparison.kind` (`ComparisonKindDto`). Values that reuse existing domain enums keep the API-wide **PascalCase**: race `status` (`RunStatus`), `distanceCategory` (`RaceDistanceCategory`) and `typeDistribution[].category` (`RunTypeCategory`), identical to `RunDto` and `RunTypeDto`. Changing either side would be a breaking contract change for the SPA and the M6 tool payloads.

## Added in M6 (AI summary)

[Plan](plans/M6-ai-summary.md) (deviations [§12](plans/M6-ai-summary.md#12-implementation-notes--deviations-2026-11-04)), ADRs [0026](adr/0026-training-insights-agent-and-grounding.md) (agent, fact sheet, validation), [0027](adr/0027-ai-model-provider-foundry-and-local-dev.md) (provider, cost) and [0028](adr/0028-summary-cache-quotas-and-demo-summaries.md) (cache, quotas, demo summaries).

| Method & path | operationId | Policy | Success | Errors |
|---|---|---|---|---|
| `GET /api/insights/summary?from&to&compare&lang` | `getInsightsSummary` | Auth (users and guests), general rate limiter. **Never calls the model.** | 200 `InsightsSummaryResponse`, `Cache-Control: private, no-store` | 400 (keys `from`, `to`, `compare`, `lang`), 401, 429 `rate-limited` |
| `POST /api/insights/summary` body `GenerateSummaryRequest` | `generateInsightsSummary` | Auth, rate policy **`ai-summary`** (replaces the general one) | 200 `InsightsSummaryResponse`, `Cache-Control: private, no-store` | 400, 401, 409 `summary-in-progress`, 413, 429 (`ai-quota`, `rate-limited`), 502 `summary-failed`, 503 (`feature-unavailable`, `ai-busy`) |
| `GET /api/client-config` (changed) | `getClientConfig` | Anon (unchanged) | 200 + `ai` | — |

**Parameters and body**
- **GET query:** `from`, `to`, `compare` exactly as `getInsightsReport` (dates 1950–2100, `to ≥ from`, span ≤ 366 days; `compare` case-sensitive, default `previous`), plus `lang`: only `en` (the default), matched **case-sensitively** (`EN` → 400 `errors.lang`).
- **POST body:** `GenerateSummaryRequest = { from: string; to: string; compare?: "none" | "previous" | "previousYear"; lang?: "en"; regenerate?: boolean }`.
  - `from` and `to` are nullable in the schema and validated by hand: missing → `errors.from` / `errors.to` "Required.".
  - The other rules are as for GET. Unknown members → 400 (ADR 0012). `regenerate` defaults to `false`; a non-boolean value (e.g. `"yes"`) → 400.
- No route parameter names a resource: both endpoints read the caller's own data only (`Owner` filter), so there is no 404.

### Shapes

```ts
InsightsSummaryResponse = {
  period: { from: string; to: string; compare: string; lang: string };
  state: "none" | "fresh" | "stale";      // demo and template summaries are always "fresh"
  summary: InsightsSummaryDto | null;      // null only with state "none"
  generation: {
    availability: "available" | "notConfigured" | "quotaExhausted" | "notNeeded";   // notNeeded = template period
    used: number | null; limit: number | null;     // account quota; null when notConfigured or notNeeded
    resetsAt: string | null;                       // UTC; users when exhausted; always null for guests
    scope: "rolling24h" | "account" | null         // users: rolling24h; guests: account (session lifetime)
  }
}
InsightsSummaryDto = {
  source: "model" | "demo" | "template";
  createdAt: string | null;                        // UTC, whole seconds; model summaries only
  metricsVersion: number; factSheetVersion: number;
  promptVersion: number | null; model: string | null;   // model summaries only (model = provider-reported id, ≤ 100 chars)
  headline: SummaryText; wentWell: SummaryText[]; watchOuts: SummaryText[];
  suggestion: SummaryText | null; dataNotes: SummaryText[];
  values: { [key: string]: SummaryValue }          // every key cited by any text
}
SummaryText  = { text: string /* prose with {key} placeholders */; keys: string[] /* in order of appearance */ }
SummaryValue = { number: number | null; text: string | null; unit: SummaryUnit }   // exactly one of number/text is set
ClientConfigResponse.ai = { summaryGeneration: boolean }   // provider configured and kill switch on; read per request (no quota info: anonymous)
```

**Text rendering.** `text` is plain prose with `{key}` placeholders for every number, date and category. The client replaces each with `values[key]`, formatted by its `unit`, and renders plain text only (never HTML). The server has validated that every key exists (ADR 0026 §3). A stale summary carries the values it was written with.

**Units and encodings** (enforced by `ValueEncodingContractTests`):

| `unit` | Field | Encoding |
|---|---|---|
| `meters`, `seconds`, `load` (AU), `ratio`, `percent` (e.g. `6.4` = +6.4 %), `share` (0..1), `secondsPerKm`, `secondsPerKmPerWeek`, `count`, `days`, `stars` (1–5) | `number` | SI / report rounding (ADR 0024 §9) |
| `date` | `text` | `yyyy-MM-dd` |
| `flag` | `text` | `"true"` / `"false"` |
| `band` | `text` | `low` \| `optimal` \| `high` |
| `runType` | `text` | run-type code, camelCase (`trailRace`) — i18n `runTypes.<code>` |
| `raceCategory` | `text` | `fiveK` \| `tenK` \| `halfMarathon` \| `marathon` \| `other` (camelCase, **not** the PascalCase `RaceDistanceCategory` of the report) |
| `raceStatus` | `text` | `completed` \| `planned` (camelCase) |

Signed deltas (`comparison.delta.*.absolute`) are signed numbers; the client formats the sign.

### POST outcomes (in order of the checks)

Order: validate → provider/kill switch → report + fact sheet → template period → fresh row (when `!regenerate`) → in-flight guard → quotas → slot → generate (≤ 2 attempts) → ledger row → validate → upsert.

| Status | `type` | When | Extras |
|---|---|---|---|
| 200 | — | Template period (no model, no quota); a fresh stored row with `regenerate: false` (no model, no quota); a new generation (`source: model`, `state: fresh`) | — |
| 400 | default | Validation | `errors` keys `from`, `to`, `compare`, `lang`, or a JSON path |
| 409 | `summary-in-progress` | A generation of this account is already running (per replica) | — |
| 429 | `ai-quota` | Account or global quota reached (ADR 0028 amendment) | `scope: "account" \| "global"`, `limit`, `resetsAt` (UTC, user account quota only; otherwise `null`); **`Retry-After` only for a user's account quota** |
| 429 | `rate-limited` | Burst limiter `ai-summary` | `Retry-After` |
| 502 | `summary-failed` | Invalid output twice, a timeout or a provider error. Nothing is stored; the attempts count. | `reason: "invalidOutput" \| "timeout" \| "provider"` |
| 503 | `feature-unavailable` | `Ai:Provider = None`, kill switch `Ai:Enabled = false`, or the credential cannot get a token (no HTTP call, no quota) | — |
| 503 | `ai-busy` | No free generation slot on this replica (`Retry-After: 10`), or the provider throttled (429): `Retry-After` from the provider's hint, ≤ 5 min, default 10 s. **No quota used.** | `Retry-After` |

A client abort cancels the model call; a ledger row (`Cancelled`) is still written when an attempt was sent.

### Limits (M6)

| Limit | Default | Config key | Response |
|---|---|---|---|
| Summary POSTs per caller, sliding window (policy `ai-summary`) | users 6 / 10 min, guests 3 / 10 min (Development: 60 / 30) | `RateLimiting:AiSummary:UserPermitLimit`, `GuestPermitLimit`, `Window` | 429 `rate-limited` + `Retry-After` |
| User generations, rolling 24 h | 10 | `Ai:Quotas:UserPer24h` | 429 `ai-quota` (`scope: account`) |
| Guest generations per account | 3 | `Ai:Quotas:GuestPerAccount` | 429 `ai-quota` (`scope: account`, `resetsAt: null`) |
| Model attempts, all callers, rolling 24 h | 200 | `Ai:Quotas:GlobalPer24h` | 429 `ai-quota` (`scope: global`) |
| Model attempts, guests, rolling 24 h | 60 | `Ai:Quotas:GuestGlobalPer24h` | 429 `ai-quota` (`scope: global`) |
| Concurrent generations per replica | 2 | `Ai:MaxConcurrentGenerations` | 503 `ai-busy` |
| Attempts per generation; timeout per attempt | 2; 30 s | `Ai:MaxAttempts`, `Ai:AttemptTimeout` | 502 `summary-failed` |
| Stored summaries per account | users 1 000, guests 20 (oldest dropped) | code constants | — |

## Added in M7 (Azure deployment, Part A; [plan](plans/M7-azure-deployment.md), ADRs [0033](adr/0033-production-maps-and-azure-maps.md), [0034](adr/0034-cost-guardrails-and-observability.md))

| Method & path | operationId | Policy | Success | Errors |
|---|---|---|---|---|
| `GET /api/maps/token` | `getMapsToken` | Auth, rate policy **`maps-token`** (replaces the general one) | 200 `MapsTokenResponse`, `Cache-Control: private, no-store` (also on errors) | 401, 429 `rate-limited`, 503 `feature-unavailable` (display off: `Maps:Provider` ≠ `AzureMaps`; or the token cannot be issued — no provider detail leaks) |
| `GET /api/geocoding/search` (changed) | `searchPlaces` | unchanged (`geocode`) + **global daily budget** | unchanged | + 429 `rate-limited` with `code: "geocode-budget"` and `Retry-After` = seconds until 00:00 UTC |
| `GET /api/client-config` (changed) | `getClientConfig` | Anon (unchanged) | 200; `maps` gains `azureMapsDomain` | — |
| `GET /api/health` (changed) | `getApiHealth` | Anon, token optional; rate policy **`health`** | 200 `ApiHealthResponse` (shape unchanged) | 429, 503 |

```ts
MapsTokenResponse = { token: string /* Azure Maps SAS; a bearer credential, keep in memory only */; expiresAt: string /* UTC */ }
ClientConfigResponse.maps.azureMapsDomain: string | null   // e.g. "eu.atlas.microsoft.com"; null unless maps.provider = "AzureMaps"
```

- **Maps token.** One shared, server-cached SAS token for all callers (no user data in it): rate cap per Azure location, allowed regions, ≈ 2 h lifetime, renewed by the server when < 30 min remain. The SPA requests it only when `maps.provider = "AzureMaps"`, keeps it in memory, and falls back to MapLibre for the session on any error.
- **Geocode budget.** Checked **after** request validation (a 400 never spends it) and **before** the provider call. Durable in SQL (`GeocodeUsage`), so it is global across replicas and restarts. The per-caller `geocode` 429 has **no** `code`; the budget 429 has `code: "geocode-budget"`, so the SPA can show "place search is used up for today".
- **`client-config.maps.provider = "AzureMaps"`** is accepted when the Azure Maps settings validate (account client id, account resource id, SAS principal id, an `*.atlas.microsoft.com` domain, rate 1–500, lifetime ≤ 24 h); otherwise API startup fails with the key names. `geocodingEnabled` is `true` with `Maps:Geocoding:Provider = AzureMaps`.
- **Health.** Outside Development:
  - without a valid bearer token (none, invalid or expired): only the `live` checks run (no SQL, no Blob), so anonymous traffic can never wake the serverless database;
  - with a valid user or guest token: the full report including SQL, cached 5 min per replica;
  - in Development the full report is always returned.
  
  The SPA's `ApiStatus` calls `/api/health` **without a token** (it is in the anonymous list of `client.ts`), only once a session exists, and once per page load (no polling, no refetch on focus). In Azure the badge therefore reflects the `live` checks only; the authenticated full report is used by `infra/scripts/smoke.sh`. `/alive` and `/health` (probes, not in OpenAPI) are unchanged.

### Limits (M7)

| Limit | Default | Config key | Response |
|---|---|---|---|
| Maps token requests per caller, sliding window (policy `maps-token`) | users 30 / h, guests 10 / h | `RateLimiting:MapsToken:UserPermitLimit`, `GuestPermitLimit`, `Window` | 429 `rate-limited` + `Retry-After` |
| Provider geocode calls, all callers, per UTC day (durable) | 75 | `Maps:Geocoding:GlobalDailyLimit` | 429 `rate-limited`, `code: geocode-budget`, `Retry-After` |
| `GET /api/health` per caller (user id, else client IP), sliding window (policy `health`) | 30 / min | `RateLimiting:Health:UserPermitLimit`, `GuestPermitLimit`, `Window` | 429 `rate-limited` + `Retry-After` |

**Job CLI (not HTTP):** `dotnet RunnersDiary.Api.dll --job guest-cleanup` → exit 0 on success, 1 on failure (any other `--job` value is a failure). Used by the Container Apps job `job-guest-cleanup` (ADR 0032).

<a id="planned-in-m8-race-proposals"></a>

## Added in M8 (race proposals)

**Implemented 2026-11-13 on `m8-race-proposals`** (OpenAPI document and `web/src/api/generated/schema.d.ts` regenerated); the SPA calendar/details UI is in progress.
- Sources: [plan](plans/M8-race-proposals.md) §4 and §12 (deviations D13–D19), ADRs [0036](adr/0036-race-catalogue-model-and-race-runs.md) (signup mapping) and [0037](adr/0037-race-proposals-filtering-and-state.md) (filtering, state, why a separate endpoint; both amended 2026-11-13).

All routes are **Auth** (users and guests) on the general per-caller rate limiter. No request carries a user id. Per-user rows (interests, states, the linked run) go through the `Owner` filter. The race catalogue is shared and not user-owned, so a race id is never "foreign": unknown, purged and hidden races answer 404.

**Feature switch:** while `Races:Enabled` is false (the production default until gate G-R4; read per request), **every `/api/races` route answers 503 `urn:runnersdiary:problem:feature-unavailable`** (after authentication, so an anonymous call is still 401), like `GET /api/maps/token` and AI generation. `client-config.races.enabled` tells the SPA not to call them.

| Method & path | operationId | Policy | Success | Errors |
|---|---|---|---|---|
| `GET /api/races/interests` | `getRaceInterests` | Auth | 200 `RaceInterestsResponse` (`configured: false` + defaults when the caller has none) | 401, 503 |
| `PUT /api/races/interests` | `updateRaceInterests` | Auth | 200 `RaceInterestsResponse` | 400, 401, 413, 503 |
| `GET /api/races/proposals?from&to&includeDismissed` | `listRaceProposals` | Auth | 200 `RaceProposalListResponse`, `Cache-Control: private, no-store` | 400 (`from`, `to`, `includeDismissed`), 401, 503 |
| `POST /api/races/proposals/seen` | `markRaceProposalsSeen` | Auth | 204 (no-op without a profile) | 401, 503 |
| `GET /api/races/{raceId}` | `getRace` | Auth | 200 `RaceDetailsDto`, `private, no-store` | 401, 404 (unknown or purged; removed unless the caller is Interested or signed up), 503 |
| `PUT /api/races/{raceId}/state` | `setRaceProposalState` | Auth | 200 `RaceUserStateDto`, `private, no-store` | 400, 401, 404 (unknown; `Interested`/`Dismissed` on a removed or duplicate race), 409 `proposal-state-limit`, 503 |
| `POST /api/races/{raceId}/signup` | `signUpForRace` | Auth | 201 `RunDto` + `Location: /api/runs/{id}` | 400, 401, 404, 409 (`race-already-linked`, `run-limit`), 503 |
| `GET /api/client-config` (changed) | `getClientConfig` | Anon (unchanged) | 200 + `races` | — |
| `GET/POST/PUT /api/runs…`, `GET /api/calendar/months/…` (changed) | unchanged | unchanged | `RunDto` gains read-only `catalogRaceId` | unchanged |

**Validation (ADR 0012 style; 400 `errors` keyed by camelCase path):**
- **`listRaceProposals`:**
  - `from`/`to` are required dates in 1950–2100, with `to ≥ from` and a span of ≤ 92 days **counting both ends** (`to − from + 1 ≤ 92`; errors on `to`);
  - `includeDismissed` is an optional boolean;
  - the server clamps to `[today, today + horizonDays]` in the caller's profile zone and returns `effectiveFrom`/`effectiveTo`; signed-up races also show outside the horizon;
  - no profile → 200 with `profileConfigured: false` and `items: []`.
- **`updateRaceInterests`** (full replace; unknown members → 400):
  - `kinds` ≥ 1 known value;
  - `regionCodes` ≤ 16 known ISO 3166-2:PL codes (no duplicates);
  - `center` and `radiusMeters` together, radius 5,000–300,000;
  - `minDistanceMeters`/`maxDistanceMeters` 1,000–250,000, min ≤ max;
  - `horizonDays` 7–365;
  - `keywords` ≤ 100 characters and ≤ 5 terms of 2–30 characters;
  - `center.latitude/longitude` in range, `center.name` ≤ 200.
- **`setRaceProposalState`:** `state` is the enum `RaceStateChoice` = `None | Interested | Dismissed` (`SignedUp` or an unknown value → 400 on `state`; `SignedUp` is derived). For a removed or duplicate race only `None` (clear) is accepted; the other values → 404.
- **`signUpForRace`** (the body is optional; answers are checked in this order: **404** unknown/removed/duplicate race → **409 `race-already-linked`** → **400** → **409 `run-limit`**):
  - `distanceMeters` must equal one of the race's distances; it is required when the race lists more than one (`errors.distanceMeters`);
  - `runTypeId` must be a Race-category type;
  - `startTime` is `HH:mm`; `targetTimeSeconds` 1 … 604,800;
  - a race dated before the caller's today → `errors.raceId`;
  - a removed or duplicate race → 404;
  - a concurrent second signup that loses the unique index → 409 `race-already-linked` as well.

**New problem types:**

| Status | When | `type` | Extras |
|---|---|---|---|
| 409 | The caller already has a run linked to this race | `urn:runnersdiary:problem:race-already-linked` | `runId` (the caller's own run) |
| 409 | Proposal-state rows per account reached (users 2,000, guests 200) | `urn:runnersdiary:problem:proposal-state-limit` | — |

### Shapes (M8)

```ts
RaceKind = "Road" | "Trail" | "Mountain" | "Ultra" | "CrossCountry" | "Ocr" | "Other"   // PascalCase, like RunStatus
ProposalState = "None" | "Interested" | "Dismissed" | "SignedUp"                         // SignedUp: derived, response only

RaceInterestsResponse = {
  configured: boolean;
  kinds: RaceKind[]; regionCodes: string[];
  center: { latitude: number; longitude: number; name: string | null } | null; radiusMeters: number | null;   // SI; the UI edits km
  minDistanceMeters: number | null; maxDistanceMeters: number | null;
  horizonDays: number; keywords: string | null; showInCalendar: boolean;
  updatedAt: string | null   // UTC
}
UpdateRaceInterestsRequest = Omit<RaceInterestsResponse, "configured" | "updatedAt">

RaceProposalListResponse = {
  from: string; to: string; effectiveFrom: string; effectiveTo: string;
  profileConfigured: boolean;
  catalogueUpdatedUtc: string | null;   // max LastSeenUtc of the whole catalogue (also without a profile); null when empty
  truncated: boolean;                   // the window had more than Races:Proposals:MaxCatalogueRows rows
  items: RaceProposalDto[]              // by date, then rank
}
RaceProposalDto = {
  raceId: string; name: string; date: string; startTime: string | null;
  locationName: string | null; regionCode: string | null; countryCode: string;
  location: { latitude: number; longitude: number } | null;
  kind: RaceKind; distancesMeters: number[];   // ascending, metres
  state: ProposalState; linkedRunId: string | null;
  isNew: boolean;        // imported after the caller's ProposalsSeenUtc
  dateChanged: boolean;  // linked run's date ≠ race date, or Interested and RaceDateWhenMarked ≠ race date
  source: string         // "running-life" | "demo" (demo has no entry in client-config.races.sources)
}
RaceDetailsDto = {       // a flat schema: every RaceProposalDto member, plus
  /* …RaceProposalDto members… */
  timeZoneId: string | null; surface: "Road" | "Trail" | "Track" | "Treadmill" | "Mixed" | null;
  elevationGainMeters: number | null; price: { amount: number; currency: string } | null;
  signupDeadline: string | null; organizerName: string | null;
  signupUrl: string | null; eventUrl: string | null; sourceUrl: string | null;   // absolute http(s); render via safeUrl, rel="noopener noreferrer"
  removed: boolean; previousDate: string | null;
  canonicalId: string | null;          // set when this id is a duplicate; the SPA navigates to it
  lastSeenUtc: string; updatedUtc: string
}
RaceStateChoice = "None" | "Interested" | "Dismissed"                                     // request-only enum
SetRaceProposalStateRequest = { state: RaceStateChoice }
RaceUserStateDto = { raceId: string; state: ProposalState; linkedRunId: string | null; dateChanged: boolean }
SignUpForRaceRequest = { distanceMeters?: number | null; startTime?: string | null; runTypeId?: number | null; targetTimeSeconds?: number | null }
ClientConfigResponse.races = { enabled: boolean; sources: { id: string; name: string; url: string }[] }   // real sources only: [{ "running-life", "running.life", "https://running.life/" }]
RunDto.catalogRaceId: string | null    // read-only; not part of RunWriteRequest (an unknown member there is still a 400)
```

**Signup result:**
- **Run fields:**
  - a planned run on the race's `date`;
  - `startTime` from the request or the race;
  - `timeZoneId` = the race's zone, falling back to the caller's profile zone;
  - the race run type (`roadRace` / `trailRace` / `ultra` / `crossCountry`, or the request's `runTypeId`).
- **Race details:** `race.name`, `distanceCategory` (±1 % of 5K / 10K / half / marathon, else `Other`) and `officialDistanceMeters` = the chosen distance.
- **Other fields:** `distanceMeters`, `surface`, `elevationGainMeters` and `location` come from the race. The full table is in ADR 0036 §5.
- The run cap rules of `createRun` apply.

**Limits (M8):** `Races:Proposals:MaxCatalogueRows` 1,000 (→ `truncated`), `Races:Proposals:MaxStatesPerUser` 2,000 and `MaxStatesPerGuest` 200 (→ 409 `proposal-state-limit`); signups use the run caps of `createRun`.

**Job CLI (not HTTP):** `dotnet RunnersDiary.Api.dll --job race-import`. Exit 0 covers success, disabled (no SQL connection), another run holding the lock, and a source stopped by 403/429 or three consecutive 5xx/timeouts (warning only). Exit 1 covers a layout change, a robots failure, a SQL error or an unhandled error. The job is the Container Apps job `job-race-import` (ADR 0038 + amendment).

## Planned for later milestones

None in the MVP (M8 above is post-MVP, implemented on its branch, UI in progress). A real Foundry deployment (M7 Part B, gate G7) changes no contract.
