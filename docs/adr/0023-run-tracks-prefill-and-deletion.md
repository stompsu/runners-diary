# 0023 – Run ↔ track relationship, GPX prefill with user confirmation, and blob deletion order

**Status:** Accepted (M4 plan approved 2026-11-03) · Amended 2026-11-03 (implementation, see the end)
**Date:** 2026-11-03

## Context

Brief §1.1.4: a GPX can be imported before a race (planned course) and after it (recorded track). For a completed run the app "offers to auto-fill distance, duration, elevation and start location". The M2 schema already allows **0..1 track per `Kind`** per run (`UX_GpxTracks_Run_Kind`), with a composite ownership FK. Three pieces of earlier work constrain the design:
- ADR 0018: `PUT /api/runs/{id}` is a full replace with optimistic `version`. The form keeps exact SI values for untouched numbers and must never change data silently.
- ADR 0008: the run's local `Date` is the truth.
- M2 left `DeleteRun` with a `TODO(M4)`: track blobs are not deleted. ADR 0011 orders guest cleanup "blobs first, then rows", so no blob is ever left without a record.

## Decision

1. **Relationship.** A run has at most one `PlannedCourse` and at most one `RecordedTrack`, both allowed together (compare plan vs actual). Both kinds are allowed on any status. A `RecordedTrack` on a `Planned` run is accepted, and the prefill suggests *Mark as completed*. Rest-day runs (`IsRestDay`) reject uploads with a 400 (`errors.runId`, "Rest days have no tracks").
2. **Tracks are a sub-resource with their own lifecycle.** Uploading, replacing or deleting a track does **not** change the run row or its `version`. `RunDto.tracks[]` gains `originalFileName` and `createdAt`. The full summary comes from `GET /api/runs/{runId}/tracks/{trackId}`, and the upload response returns it too. Replacing requires `replace=true`; otherwise the response is 409 `track-exists`, and the UI asks for confirmation first.
3. **The server never copies track values into the run.** The upload stores only the track. The **prefill is a client step** that the user confirms:
   1. After a successful upload, or with **Use GPX values** on an existing track, `TrackPrefillDialog` shows a table: field, current run value, GPX value, and a checkbox per row.
      - Recorded track rows: distance; duration (radio: elapsed, the default, or moving); elevation gain/loss; date + start time (`summary.startLocal`, converted with the run's zone by the server); location (GPX start point; an existing location name is kept); and for Planned runs, *Mark as completed*.
      - Planned course rows: distance (target), elevation gain/loss, location.
   2. Defaults: rows where the run field is **empty are checked**; rows where it **differs are unchecked**; equal rows are hidden. A different **date** row is unchecked and shows a warning ("This track was recorded on 14 Sep 2025; the run is on 3 Nov 2026").
   3. **Apply** navigates to the existing edit route with router state `{ prefill }`. The form opens with those values set and marked dirty, plus a notice "Values from <file> — review and save". Nothing is saved until the user presses **Save**. The save is the normal `PUT` with `version`, so 409 handling, validation and the type-level `withAllWriteMembers` check (ADR 0018) apply unchanged.
   4. A pure function `trackSuggestions(run, track)` (`features/tracks/trackSuggestions.ts`) produces the rows and is unit-tested. Prefilled numbers go through the display format (km with 3 decimals, `h:mm:ss`, metres with 1 decimal), so the run stores them at 1 m / 1 s / 0.1 m precision. This is acceptable and documented.
4. **The location becomes an edited form field.** This extends ADR 0018 §1, where it was copied from `base`. `RunFormValues.location = { latitude, longitude, name } | null` is edited by `LocationField` (name input, *Pick on map*, *Search* when geocoding is enabled, *Clear*). `toWriteRequest` takes it from the values. Coordinates stay exact numbers; they are never formatted and re-parsed. The round-trip test is updated.
5. **Deletion order: blobs first, then rows** (the same as ADR 0011):
   - **Delete track** (`DELETE /api/runs/{runId}/tracks/{trackId}`): load through the filter (404 if absent), delete prefix `users/{u:N}/tracks/{t:N}/`, then delete the row. If the blob delete fails, the response is 503, nothing is removed, and the user can retry.
   - **Delete run** (resolves `TODO(M4)`): load the run's track ids through the filter, delete each track prefix, then `ExecuteDelete` the run (the cascade removes track rows). If any blob delete fails, the response is 503 and the run is kept. The 404 behaviour for foreign or missing runs is unchanged.
   - **Replace**: the new blobs are written first. One transaction then removes the old row and inserts the new one. The old prefix is deleted **after commit, best-effort**; a failure is logged as `TrackBlobOrphaned` (warning, with the prefix). If the database step fails, the new prefix is deleted.
   - **Guests**: the existing prefix cleanup removes everything, including orphans, within 24 h + 1 h.
   - **Orphan sweep for users**: **not built in M4.** Orphans can only come from a failed best-effort delete after a replace (rare, logged, small). A periodic `users/` prefix reconciliation is a post-MVP option. It would need a blob listing plus a DB lookup per prefix, which costs more than the orphans it would remove.

## Consequences

- No silent overwrite: every value from a GPX passes through the form and the user's Save, with concurrency protection. One extra click compared with auto-fill.
- The run's `version` does not change on upload, so an edit form that is already open does not hit a 409 because of an upload.
- Deleting a run with tracks costs 1–2 blob prefix deletes (list + delete), a few hundred ms in Azure. A storage outage makes run deletion fail with a retryable 503 instead of leaving files behind.
- The M3 fake API and `toWriteRequest` round-trip tests change (location is now a field).
- Future importers (FIT/TCX, Strava) can reuse the same `GpxTracks` row (another `Kind` or format column) and the same prefill dialog.

## Alternatives considered

- **Server-side auto-fill on upload** (write distance etc. into the run): simplest, but silent; it changes `version` under an open form and contradicts the brief's "offer to".
- **Upload from the create form (before the run exists)**: would need staging of uploads without a run, or a double upload. Instead, the create form gets **Save & add GPX**, which saves and opens the run's details with the upload panel.
- **"Analyse only" endpoint (parse without storing) feeding the form**: the user would upload twice (analyse, then store); this doubles traffic for 20 MB files.
- **Delete rows first, blobs best-effort afterwards** (with or without an outbox table): the user-visible delete always succeeds, but blobs can be orphaned without any record. An outbox table adds a schema and retry loop. Kept as an option if blob-delete failures prove common.

## References

- ADR 0008, 0010, 0011, 0013, 0018; M2 plan §11 #15 (`TODO(M4)`).

## Amended 2026-11-03 (implementation)

The decision stands; the code differs as follows (M4 plan [§12](../plans/M4-maps-gpx.md#12-implementation-notes--deviations-2026-11-03)).

- **§2:** `RunDto.tracks[]` = `{ id, kind, status, originalFileName, createdAt }` — **no summary**; the viewer and the prefill dialog fetch `getTrack`. There is no `listTracks` route.
- **§5 concurrency (new):** every change to a user's set of tracks takes a per-user SQL Server application lock (`sp_getapplock`, `user-tracks:{userId:N}`, transaction-owned, 15 s timeout):
  - **Insert/replace:** the cap is re-counted inside the transaction. Replacing a track that was deleted meanwhile → **404**; one replaced by a concurrent upload → 409 `track-exists`.
  - **Delete run:** blobs of the known tracks are deleted first (unchanged); then, under the lock, the run's tracks are re-read and the run is deleted. Tracks that a racing upload committed in between are **deleted best effort after commit** (logged `TrackBlobOrphaned` on failure); no later insert can succeed because the run is gone (composite FK).
- **§5 storage errors:** the 503 uses the new problem type `urn:runnersdiary:problem:storage-unavailable` with `Retry-After: 5` (delete track, delete run, upload).
- **§4 location picker:** opens at the existing pin, else the last pick in this tab (sessionStorage `rd.lastMapPick`), else Gliwice. The GPX start point is offered by the prefill rather than as the picker's start.
