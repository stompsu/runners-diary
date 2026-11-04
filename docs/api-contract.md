# Runners Diary – API contract

Living document, owned by the `architect` agent. Status: **M2–M4 implemented (merged); M5 adds `GET /api/insights/report` (implemented, awaiting CI + approval)**.
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
| 429 | Rate limit; `Retry-After` header | `urn:runnersdiary:problem:rate-limited` |
| 503 | Guest capacity reached / seeding busy / seeding failed; `Retry-After` | `urn:runnersdiary:problem:guest-capacity` |
| 503 | GPX processing slots full; `Retry-After: 10` (M4) | `urn:runnersdiary:problem:processing-busy` |
| 503 | Blob storage failed on track upload/delete or run delete; nothing changed; `Retry-After: 5` (M4) | `urn:runnersdiary:problem:storage-unavailable` |
| 503 | Optional feature not configured or its provider failed (geocoding, M4) | `urn:runnersdiary:problem:feature-unavailable` |
| 500 | Unhandled; no details outside Development | default |

**400 `errors` keys.** DataAnnotations and domain-rule errors: camelCase JSON path of the member (`race.resultTimeSeconds`, `rating.stars`). Malformed, incomplete or unknown-member JSON: the JSON path where reading failed (`body` when there is none) with a fixed generic message. Route/query binding failures: the parameter name (`from`, `id`) with a fixed message. Framework/serializer exception texts are never echoed.

**Unmatched routes.** An unknown `/api/*` path answers **401** without a valid token (the fallback policy runs before routing can tell the path is unknown) and **404** with one. `/openapi/v1.json` is served (anonymously) in Development only.

## Endpoints (M2)

| Method & path | operationId | Policy | Success | Errors |
|---|---|---|---|---|
| `GET /alive`, `GET /health` | — (not in OpenAPI) | Anon | 200/503 text | — |
| `GET /api/health` | `getApiHealth` | Anon | 200 `ApiHealthResponse` | 503 |
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

All `/api` endpoints except the Anon ones (these two and, since M3, `GET /api/client-config`) also pass the general per-caller rate limiter (429). Exception (M4): `uploadTrack` and `searchPlaces` use their own policy (`gpx-upload`, `geocode`) **instead of** the general one — ASP.NET Core applies the most specific policy per endpoint. Every `/api` route (Anon included) may answer 413.

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
| `GET /api/client-config` | `getClientConfig` | Anon (anonymous group shared with health: no rate limiting, no DB; response built once at startup) | 200 `ClientConfigResponse`, `Cache-Control: public, max-age=300` | — |

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

There is no `listTracks` route: a run's tracks are in `RunDto.tracks[]`. `GET /api/maps/token` (Azure Maps SAS) is **not implemented**; its contract stays in ADR 0022 §3 for M7.

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

ClientConfigResponse.maps = { provider: "MapLibre" | "AzureMaps"; mapLibreStyleUrl: string | null; geocodingEnabled: boolean }
ClientConfigResponse.gpx  = { maxFileBytesUser: number; maxFileBytesGuest: number }   // both: the endpoint is anonymous
```

`ETag` of `getTrackGeoJson` is `"{hex of the first 8 bytes of the file's SHA-256}-v{processorVersion}"` (a track id's content never changes; a replace creates a new id). `client-config.maps.provider` is always `MapLibre` in M4 (another value fails API startup); `mapLibreStyleUrl` comes from `Maps:MapLibre:StyleUrl` (absolute https, default `https://tiles.openfreemap.org/styles/liberty`); `geocodingEnabled` is `false` while `Maps:Geocoding:Provider = None`.

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

## Planned for later milestones

Azure Maps SAS token endpoint and Azure geocoder (M7, ADR 0022). AI summaries (M6).
