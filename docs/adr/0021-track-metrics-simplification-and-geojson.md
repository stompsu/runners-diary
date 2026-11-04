# 0021 – Track metrics, simplification and the processed-track GeoJSON

**Status:** Accepted (M4 plan approved 2026-11-03) · Amended 2026-11-03 (implementation, see the end)
**Date:** 2026-11-03

## Context

The brief (§2 "GPX processing") asks for haversine distance, elevation gain with smoothing, duration, splits, and Douglas–Peucker simplification for display. The processed track is stored as GeoJSON in Blob Storage "for fast map rendering". Numbers must be **deterministic**: the AI summary (M6) narrates them and must never disagree with what the UI shows. The demo GPX files (`seed/demo/README.md`) were generated with haversine R = 6 371 008.8 m and a 3 m hysteresis for gain/loss, and the README publishes the expected values. ADR 0008: GPX times are UTC; the run's local `Date` remains the source of truth. All of this logic lives in `RunnersDiary.Domain/Gpx/` (pure, no packages).

## Decision

### 1. Metrics (`Domain/Gpx/TrackMetrics`; computed once at upload; `ProcessorVersion = 1`)

| Metric | Definition |
|---|---|
| Distance | Sum of haversine distances between consecutive points **within a segment**, with R = **6 371 008.8 m** (mean Earth radius). Gaps between segments/tracks are not counted. No speed-spike filter in v1. |
| Elevation gain/loss | **3 m hysteresis** on the raw elevations, per segment: start a pivot at the first elevation; when `e − pivot ≥ 3` add the difference to gain and move the pivot; when `pivot − e ≥ 3` add to loss and move the pivot. Points without elevation are skipped. Null when no point has elevation (warning `noElevation`). Also min/max elevation. |
| Elapsed time | `last.time − first.time` (seconds), timed tracks only. Includes pauses and segment gaps. |
| Moving time | Sum of intervals within a segment where `dt > 0` and `distance / dt ≥ 0.5 m/s`. Timed tracks only. |
| Start/end | First/last point (coordinates); `StartUtc`/`EndUtc` from the times when timed. |
| Splits | Every full 1 000 m of cumulative distance plus the final partial split. Each split has `distanceMeters`, `elapsedSeconds` (linear interpolation of time at the boundary; null if untimed) and `elevationDeltaMeters` (interpolated end − start elevation; null without elevation). At most 1 000 splits. |
| Warnings | Flags: `timestampsIgnored`, `noElevation`, `multipleSegments`, `routeConverted` (points came from `rte`). |

Rounding is applied **only at the output boundary**: metres to 0.1 m, seconds to whole seconds. Internal sums use `double` in file order. The same input always gives the same output.

Pace is never stored (as with runs). It is derived in the UI from distance and elapsed or moving time.

### 2. Local start for run prefill (ADR 0008)

`TrackStartLocal.For(startUtc, run.TimeZoneId)` converts the first point's UTC instant into the run's zone, giving `date` + `time` (`HH:mm`). It is computed **on read** in the API mapping, so it follows later changes of the run's zone. The run's `Date` is never changed automatically. A different date is only *suggested* (ADR 0023). There is no time-zone lookup from coordinates, because that would need a tz-boundary database; a run recorded while travelling keeps the user's chosen zone.

### 3. Simplification for display (`Domain/Gpx/DouglasPeucker`)

- Iterative (stack-based, no recursion) Douglas–Peucker per segment, on an equirectangular projection in metres centred on the bounding box's mid-latitude.
- Tolerance starts at **3 m** and doubles until the whole track has **≤ 4 000 vertices**.
- The first and last point of each segment are always kept.
- Coordinates are rounded to 6 decimals (about 0.1 m). Elevation is not part of the line, because the profile carries it.

### 4. Elevation profile (`Domain/Gpx/ElevationProfile`)

- Resampled along cumulative distance at `step = max(10 m, distance / 600)`, so there are ≤ 601 samples including the end.
- Each sample is `[distanceMeters (int), elevationMeters (0.1), longitude, latitude]`, with elevation and position linearly interpolated from the raw points.
- The profile is null when the track has no elevation.
- The lon/lat pair lets the chart drive a map marker without a client-side nearest-point search (hover sync, plan §3.6).

### 5. Processed-track document (blob `track.json`, `Content-Type: application/geo+json`)

A valid RFC 7946 `FeatureCollection` with three foreign members (RFC 7946 §6.1):

```json
{
  "type": "FeatureCollection",
  "formatVersion": 1,
  "bbox": [18.6512, 50.2861, 18.6977, 50.3104],
  "features": [
    { "type": "Feature", "id": "line",   "properties": { "role": "line" },
      "geometry": { "type": "MultiLineString", "coordinates": [[[18.676642, 50.294862], [18.677455, 50.294917]]] } },
    { "type": "Feature", "id": "start",  "properties": { "role": "start" },  "geometry": { "type": "Point", "coordinates": [18.676642, 50.294862] } },
    { "type": "Feature", "id": "finish", "properties": { "role": "finish" }, "geometry": { "type": "Point", "coordinates": [18.676650, 50.294870] } }
  ],
  "profile": [[0, 214.0, 18.676642, 50.294862], [17, 214.4, 18.676880, 50.294880]],
  "splits":  [{ "index": 1, "distanceMeters": 1000, "elapsedSeconds": 291, "elevationDeltaMeters": 1.8 }]
}
```

- Size: ≤ 4 000 vertices + ≤ 601 profile samples ≈ 150–250 KB uncompressed. M4 sends it uncompressed. Response compression (or a gzip-encoded blob) is an M7 option.
- The C# record `TrackGeoJson` (Api contracts) describes exactly this shape, so it appears in OpenAPI and the TS client. The endpoint **streams the blob bytes** and does not re-serialize them. A contract test deserializes the stored JSON into the record (strict, no unmapped members).
- Serialization lives in Infrastructure (`System.Text.Json`, invariant culture). Domain returns plain records.

### 6. Blob layout and headers

- `gpx/users/{userId:N}/tracks/{trackId:N}/original.gpx`: uploaded with `Content-Type: application/gpx+xml`.
- `gpx/users/{userId:N}/tracks/{trackId:N}/track.json`: uploaded with `Content-Type: application/geo+json`.
- Both sit under the user prefix (ADR 0011: prefix delete). The per-track folder makes track deletion a prefix delete too.
- Blobs are never served directly to the browser: there are no SAS URLs and the container stays private. The API sets the response headers itself (plan §3.3), ignoring stored blob properties.
- **Caching:** a track id is immutable (a replace creates a new id). `GET …/geojson` therefore sends `Cache-Control: private, max-age=86400, immutable` and `ETag: "{trackId:N}-{formatVersion}"`, and the SPA caches it with `staleTime: Infinity`. `GET …/gpx` sends `private, no-store`.

### 7. Stored summary (SQL, `GpxTracks` columns)

These columns are stored so that lists, M5 insights and M6 tools never need to read blobs:
- `DistanceMeters`, `ElevationGainMeters`, `ElevationLossMeters`, `MinElevationMeters`, `MaxElevationMeters`
- `ElapsedSeconds`, `MovingSeconds`, `StartUtc`, `EndUtc`
- `StartPoint` (`geography`), `FinishPoint` (`geography`)
- `PointCount`, `WarningFlags`, `ProcessedBlobName`, `ProcessorVersion`, `ProcessedAt`

See `data-model.md`. Splits and the profile live only in the blob.

### 8. Demo tracks

`DemoDatasetLoader` processes each distinct demo file **once per process** and caches the summary plus the `track.json` bytes. `DemoDatasetStartupCheck` fails startup if any demo file is rejected. The seeder uploads both blobs per guest track and inserts `Processed` rows. Seeding therefore costs no parsing per guest (12 small uploads instead of 6). The demo timestamps stay in 2025 (README); only metrics and geometry are shown for them.

## Consequences

- One documented algorithm per metric, with the demo README values as golden numbers (±1 m distance, ±2 m gain/loss, exact times).
- `ProcessorVersion` allows reprocessing later (e.g. a speed-spike filter) without guessing which rows used which rules. Reprocessing is not built in M4.
- The 3 m hysteresis without pre-smoothing slightly under-counts gentle rolling terrain and over-counts very noisy GPS-only elevation. This is accepted for v1 and documented in the UI tooltip ("climbs under 3 m are ignored").
- Distance is not filtered for GPS spikes; a bad fix inflates it. A follow-up can bump the processor version.
- The processed JSON is about 2–10 % of the raw size, so blob storage cost is dominated by raw files.

## Alternatives considered

- **Moving-average smoothing + 0 m threshold** (several vendors do this): more parameters, and the result depends on the sampling rate. Hysteresis matches the demo generator and is easy to explain.
- **DEM-corrected elevation**: needs an external service or data, which is out of scope and costs money.
- **Vincenty/ellipsoidal distance**: differs from haversine by < 0.5 %; not worth it for running.
- **Storing simplified geometry in SQL (`geography` LineString)**: makes rows heavy and needs a blob anyway for the profile. The brief asks for GeoJSON in Blob.
- **Client-side processing (parse GPX in the browser)**: the server could not trust the metrics, and parsing 20 MB on a phone is slow.
- **Vector-tile or Polyline encoding**: smaller, but the map engines take GeoJSON natively, and 250 KB per track is fine.

## References (checked 2026-11-03)

- RFC 7946 GeoJSON (§3 objects, §5 bbox, §6.1 foreign members): https://www.rfc-editor.org/rfc/rfc7946 (stable; not re-fetched)
- Demo generator parameters (R, 3 m hysteresis): `seed/demo/tools/generate_gpx.py` lines 11 and 88–99; `seed/demo/README.md`

## Amended 2026-11-03 (implementation)

The decision stands; the code differs as follows (M4 plan [§12](../plans/M4-maps-gpx.md#12-implementation-notes--deviations-2026-11-03)).

- **§1 timestamps:** points with a time outside 1990-01-01 … now + 1 day make the track untimed (ADR 0020 §5), so `StartUtc`/`EndUtc` are null and `timestampsIgnored` is set. Stored `StartUtc`/`EndUtc` are **rounded to whole seconds** (half up, as `datetime2(0)`), so the upload response equals a later read.
- **§3 simplification algorithm:** a **single stack-based pass** records each point's *significance* (the largest tolerance at which it survives, capped by its parent split); the 3 m → doubling tolerance is then only a threshold over those values, so the cap search does not re-run Douglas–Peucker. Splits below 3 m are not followed. A hard budget of **5·10⁷ point-to-chord evaluations** per track bounds adversarial input (alternating zigzags are quadratic); when it is exhausted the segment lines are **decimated uniformly** (every n-th point plus both ends, ≤ 4 000 vertices). Processing observes the **request cancellation token**, so an aborted upload frees its processing slot.
- **§5 contract records:** the GeoJSON records (`TrackGeoJson`, `…Feature`, `…Geometry` with `coordinates` as free JSON, `…Split`) exist only for OpenAPI; the endpoint streams the blob.
- **§6 caching (replaces the `immutable` bullet):** `GET …/geojson` sends `Cache-Control: private, no-cache` and `ETag: "{hex of the first 8 bytes of the file's SHA-256}-v{ProcessorVersion}"`; a matching `If-None-Match` returns **304**. Reason: with `max-age`/`immutable` the geometry stays readable from the browser cache after sign-out; `no-cache` makes every use revalidate through the authenticated API at the cost of one small request. The SPA keeps `staleTime: Infinity` (`gcTime` 30 min) in memory. A missing blob for an existing row → 404.
- **§7 storage:** `WarningFlags` is a **non-null `tinyint`** (0 = none); `ProcessorVersion` is `tinyint`. Constraint `CK_GpxTracks_Processed` as in the plan (§3.1).
- **§8 demo:** as decided — processed once per process; 6 `Processed` rows and 12 blobs per guest. The demo README and `runs.json` gain/loss values were updated to the pipeline's output.
