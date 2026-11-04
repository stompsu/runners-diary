# 0020 – GPX upload: one synchronous request, strict XML parsing, per-caller limits

**Status:** Accepted (M4 plan approved 2026-11-03) · Amended 2026-11-03 (implementation, see the end)
**Date:** 2026-11-03

## Context

Brief §1.1.4 and §1.2: users import GPX files of up to about 20 MB, both before a race (planned course) and after it (recorded track). Files must be validated and sanitized, and their content is never trusted. Brief §1.3 adds guest limits on GPX size and count. Current state (M2/M3):
- `GpxTracks` holds metadata and the raw blob name. Status is `Pending`, and the demo tracks are never processed.
- Every `/api` route has a 64 KB body limit (`RequestBodyLimit`, checked against `Content-Length` before authentication).
- Rate limiting is per caller (`rd_uid`).
- Hosting is scale-to-zero Container Apps (ADR 0004/0006). A background queue or worker would add a resource, and it would also have to wake up to do its work.

A 20 MB GPX file has about 200 000 points. Parsing it with a forward-only `XmlReader` and computing metrics plus simplification takes well under a second on one core, so there is no need for asynchronous processing.

## Decision

1. **Endpoint.** `POST /api/runs/{runId}/tracks` takes `multipart/form-data` with the fields `kind` (`PlannedCourse` | `RecordedTrack`), `file` and optional `replace` (`true` | `false`). backend-dotnet wires the endpoint. The pipeline (`Infrastructure/Gpx/TrackIngestionService` plus the pure `Domain/Gpx`) belongs to maps-gpx. The steps run in this order:
   1. Authentication, the general limiter and the new `gpx-upload` policy run first.
   2. The run is loaded through the `Owner` filter; a missing or foreign run returns 404.
   3. Size check: the guest limit is enforced on `Content-Length` before the body is read, and again on `IFormFile.Length`.
   4. Conflict checks: if a track of that kind already exists and `replace` is not set, return 409 `urn:runnersdiary:problem:track-exists`. If the account's track cap is reached, return 409 `urn:runnersdiary:problem:track-limit`.
   5. The file is copied into a bounded `MemoryStream` and its SHA-256 is computed.
   6. The handler acquires a processing slot (`SemaphoreSlim(2)` per replica; a 5 s wait, then 503 `urn:runnersdiary:problem:processing-busy` with `Retry-After`).
   7. The file is parsed and processed (ADR 0021).
   8. Two blobs are uploaded under the new track id.
   9. One `SaveChanges` call removes the replaced row (if any) and inserts the new row as `Processed`.
   10. After commit, the replaced track's blobs are deleted on a best-effort basis (ADR 0023).
   11. The endpoint returns 201 `TrackDto`.

   If the database step fails, the blobs that were just uploaded are deleted (the same pattern as `DemoDataSeeder`).
2. **Processing is synchronous.** There is no queue, worker or `Pending` phase for uploads. A rejected file leaves nothing behind: no row and no blob. `Pending` stays in the enum for rows created before M4 and for future asynchronous importers (FIT, Strava). `Failed` is not used by uploads.
3. **Body limits per endpoint.** `RequestBodyLimit` honours endpoint metadata (`IRequestSizeLimitMetadata`, e.g. `RequestSizeLimitAttribute`). Every other route keeps 64 KB. The upload endpoint allows `GpxLimits:MaxFileBytes` (20 MB) + 64 KB of multipart overhead. The matching `FormOptions` limit (`WithFormOptions(multipartBodyLengthLimit: …)`) is set on the same endpoint. The endpoint calls `DisableAntiforgery()`, which is safe because authentication is a bearer token and there are no cookies.
4. **Safe XML** (`Domain/Gpx/GpxReader`, BCL `XmlReader` only):
   - `DtdProcessing = Prohibit` (a DOCTYPE is an error, so XXE and billion-laughs attacks are impossible).
   - `XmlResolver = null`, `MaxCharactersFromEntities = 0`.
   - `MaxCharactersInDocument = MaxFileBytes`, so a UTF-16 or entity trick cannot exceed the byte budget.
   - `IgnoreComments`, `IgnoreProcessingInstructions`, `IgnoreWhitespace`.
   - The reader never builds a DOM.
   - Element depth > 16 or more than 1 500 000 elements → reject.
5. **Content checks.** Neither the client's `Content-Type` nor its file name is trusted.
   - The sniffed content must be XML. A gzip (`1F 8B`) or zip (`PK`) magic number, or any other first non-whitespace character than `<`, gives the code `unsupportedFormat`.
   - The root element must be `gpx` in the GPX 1.1 or 1.0 namespace (`notGpx`).
   - The file must contain 2 … 250 000 points (`tooFewPoints` / `tooManyPoints`). Points come from `trk/trkseg/trkpt`; if there are none, from `rte/rtept`, because course planners export routes.
   - Latitude must be in −90…90 and longitude in −180…180, invariant-culture numbers only, NaN and ∞ rejected (`invalidCoordinate`).
   - Elevations outside −500…9 000 m are dropped (treated as missing).
   - Timestamps are used only when **every** point has one, they never decrease, and they fall within 1990-01-01 … now + 1 day. Otherwise the track is treated as **untimed** and the `timestampsIgnored` warning is set. The file is not rejected.
   - `extensions`, `wpt`, metadata text and names are skipped and never stored or echoed.
   - The original file name is reduced to its last path segment, control characters and `"\/` are stripped, and it is cut to 255 characters. It must end in `.gpx` (`wrongExtension`). It is used for display and as the download file name only.
   - Blob names are always server-generated (`users/{userId:N}/tracks/{trackId:N}/…`).
6. **Errors.** A rejected file returns 400 `application/problem+json` with type `urn:runnersdiary:problem:invalid-gpx`, `errors.file = [<fixed English message>]` and an extension `code` (one of the codes above, plus `malformedXml`, `dtdProhibited`, `tooLarge`). Parser exception texts and line numbers are never echoed; they are logged at Debug level only.
7. **Limits** (configuration section `GpxLimits`; defaults in `appsettings.json`):

   | Limit | Users | Guests | Response |
   |---|---|---|---|
   | File size | 20 MB (`MaxFileBytes`) | 5 MB (`GuestMaxFileBytes`) | 413 |
   | Tracks stored per account | 2 000 (`UserMaxTracks`) | 16 (`GuestMaxTracks`; demo uses 6) | 409 `track-limit` |
   | Uploads per caller, sliding 1 h window (policy `gpx-upload`) | 30 | 10 | 429 + `Retry-After` |
   | Concurrent processing per replica | 2 (`MaxConcurrentProcessing`) | shared | 503 + `Retry-After` |

   Like the M2 limiters, the counters are in memory and per replica (ADR 0011). The worst-case guest storage is 16 × (5 MB raw + ≤ 0.3 MB processed) ≈ 85 MB, so 200 active guests use at most about 17 GB for at most 24 h. In practice it is a few MB per guest.

## Consequences

- One request, one transaction, and no worker to host or wake. The upload response already carries the metrics, so the UI can offer the prefill immediately (ADR 0023).
- The memory peak per upload is about 2–3× the file size: the buffer, the point list and the processed JSON. Two processing slots cap this at about 150 MB per replica. That is acceptable for a 0.5–1 GiB ACA replica, and M7 must size the replica accordingly.
- `IFormFile` spools files larger than the form memory threshold to a temp file before the handler runs. ACA's ephemeral disk is sufficient for this.
- Large files from slow phone connections hold a request open for the duration of the upload. That is acceptable, and the limits bound it.
- The 64 KB global limit stays intact for every JSON route.

## Alternatives considered

- **Raw body (`Content-Type: application/gpx+xml`) instead of multipart**: it streams more simply and avoids `IFormFile` spooling. It was rejected because multipart lets the request carry `kind`/`replace` alongside the file, is what browsers' `FormData` produces, and is what the task specifies. It remains a cheap switch if spooling causes trouble.
- **Background processing (queue + `Pending` → `Processed`)**: it would add a queue, a hosted worker that keeps a replica alive, polling in the UI, and a partial-failure state. Nothing here is slow enough to need it.
- **A GPX library (e.g. NetTopologySuite.IO.GPX)**: `Domain` must have no package references (ADR 0001). The parsing surface is also small, and owning it makes the security settings explicit.
- **Rejecting files with partial or non-monotonic timestamps**: that is too strict for real devices. Treating such a track as untimed and warning the user is more useful.
- **Direct-to-blob upload with a user-delegation SAS**: it avoids proxying 20 MB through the API, but the file would then be stored before it is validated, it needs a second processing trigger, and it exposes storage URLs to the browser.

## References (checked 2026-11-03)

- `XmlReaderSettings.MaxCharactersInDocument` (includes characters from expanded entities; 0 = unlimited; DoS mitigation): https://learn.microsoft.com/dotnet/api/system.xml.xmlreadersettings.maxcharactersindocument?view=net-10.0 (updated 2026-07-01)
- ⚠ Not verified against the official .NET 10 docs (the Learn page for minimal-API file uploads returned 404; community sources only): `IFormFile` + `DisableAntiforgery()` behaviour without antiforgery services, `WithFormOptions`, `MultipartBodyLengthLimit` default 128 MB, temp-file spooling threshold. Community summary: https://skills.sh/managedcode/dotnet-skills/minimal-api-file-upload . backend-dotnet verifies this in step 3 with an integration test (a 21 MB body → 413; a 6 MB guest upload → 413; an upload without antiforgery services → 201).
- GPX 1.1 schema: https://www.topografix.com/GPX/1/1/ (not re-fetched; stable)

## Amended 2026-11-03 (implementation)

The decision stands; the code differs from the text above as follows (M4 plan [§12](../plans/M4-maps-gpx.md#12-implementation-notes--deviations-2026-11-03)).

- **§1.1 rate limiting:** the `gpx-upload` policy **replaces** the general `api` token bucket on the upload route (ASP.NET Core applies the most specific policy; one policy per endpoint). It does not run in addition to it. Pinned by a test.
- **§1 step order / §1.3 size check.** The implemented order is:
  1. authentication + `gpx-upload` limiter; `RequestBodyLimit` with the route's `EndpointBodyLimit` (max file limit + 64 KB);
  2. `Content-Length` > the **caller's** limit + 64 KB → 413, and the request's `MaxRequestBodySize` is lowered to that value (so a chunked guest body also stops at 5 MB);
  3. non-multipart → **415**;
  4. **preflight without the body:** run through the `Owner` filter (404), rest day (400 `errors.runId`), cap already reached for a run with no track (409 `track-limit`);
  5. `ReadFormAsync` (the handler reads the form itself; no `[FromForm]` binding) → field-keyed 400s for `kind` (strict names), `file` (exactly one), `replace`; `IFormFile.Length` > caller's limit → 413; file name normalization (`wrongExtension`);
  6. the steps 2–11 of §1 inside `TrackIngestionService` (run re-loaded, `track-exists`, cap, slot, bounded copy + SHA-256, parse, process, both blobs, commit, best-effort old-blob delete).
- **§1 step 9 commit:** one transaction under a **per-user SQL application lock** (`sp_getapplock` on `user-tracks:{userId:N}`, transaction-owned). The track cap is **re-counted inside the transaction**; the replaced row is deleted with `ExecuteDelete`. If the track to replace was deleted meanwhile → **404**; if another upload replaced it → 409 `track-exists`. The new prefix is rolled back on a storage failure (503 `urn:runnersdiary:problem:storage-unavailable`, `Retry-After: 5`), a DB failure, and on **request cancellation** (rollback ignores the cancelled token).
- **§3 form options:** `WithFormOptions` does not exist for minimal APIs in .NET 10. `FormOptions` is configured globally (`MultipartBodyLengthLimit` = max file limit + 64 KB, `ValueCountLimit` 16, `ValueLengthLimit` 1 024); the upload is the only form endpoint. `DisableAntiforgery()` as decided. The ⚠ in *References* is resolved by integration tests (21 MB → 413, guest 6 MB → 413, exact 5 MB guest file accepted, upload without antiforgery → 201).
- **§4 entities:** `MaxCharactersFromEntities = 1024`, not 0 — **0 means unlimited**. DTDs are prohibited, so entities cannot occur anyway; the bound is defence in depth.
- **§4/§5 additions:** rejection code **`tooComplex`** (depth > 16, > 1 500 000 elements, > 1 000 `trkseg`/`rte` sections). `rte` points are used only when the file has no `trk` points. `dtdProhibited` is chosen by a byte scan of the first 8 KB after the parser fails (code selection only). An empty file → `unsupportedFormat`. File names also lose Unicode format characters (bidi overrides, zero-width).
- **§6 codes, complete list:** `malformedXml`, `dtdProhibited`, `unsupportedFormat`, `notGpx`, `tooLarge`, `tooManyPoints`, `tooFewPoints`, `invalidCoordinate`, `tooComplex`, `wrongExtension`.
- **New:** `GET /api/client-config` exposes `gpx: { maxFileBytesUser, maxFileBytesGuest }` for the client pre-check (ADR 0015 pattern; anonymous, so both values).
