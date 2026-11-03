# Runners Diary – API contract

Living document, owned by the `architect` agent. Status: **M2 implemented (awaiting approval)**.
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
| 409 | Run cap reached (total per account or per date, see *Limits*) | `urn:runnersdiary:problem:run-limit` |
| 413 | Request body > `RequestLimits:MaxBodyBytes` (default 64 KB), any `/api` route incl. `POST /api/guest-sessions` | default |
| 429 | Rate limit; `Retry-After` header | `urn:runnersdiary:problem:rate-limited` |
| 503 | Guest capacity reached / seeding busy / seeding failed; `Retry-After` | `urn:runnersdiary:problem:guest-capacity` |
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
| `DELETE /api/runs/{id}` | `deleteRun` | Auth | 204 | 401, 404 |
| `GET /api/calendar/months/{year}/{month}` | `getCalendarMonth` | Auth | 200 `CalendarMonthResponse` | 400 (year ∉ 1950–2100, month ∉ 1–12), 401 |

All `/api` endpoints except the two Anon ones also pass the general per-caller rate limiter (429). Every `/api` route (Anon included) may answer 413.

### Limits (configuration, defaults in `appsettings.json`)

| Limit | Default | Config key | Response |
|---|---|---|---|
| Request body size, all `/api` routes | 64 KB (65 536 bytes) | `RequestLimits:MaxBodyBytes` | 413 |
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
  tracks: { id: string; kind: "PlannedCourse" | "RecordedTrack"; status: "Pending" | "Processed" | "Failed" }[];
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

## Planned for later milestones (not in M2)

GPX upload/download/GeoJSON (M4, maps-gpx), Maps token (M4), insights/metrics (M5), AI summaries (M6), SPA runtime auth config if needed (M3).
