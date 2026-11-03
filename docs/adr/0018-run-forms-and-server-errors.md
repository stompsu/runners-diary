# 0018 – Run forms: react-hook-form + zod in display units, server problems mapped to fields

**Status:** Accepted (M3 plan approved 2026-11-03) · Amended 2026-11-03 (implementation, see the end)
**Date:** 2026-11-03

## Context

The run form edits a full run: date/time/zone, status, type, title, notes, distance, duration, elevation, HR, RPE, gear, weather, surface, race details and the rating. Location (map/geocoding) arrives in M4. The API is authoritative (ADR 0012): it returns 400 `HttpValidationProblemDetails` with `errors` keyed by camelCase JSON path (`race.resultTimeSeconds`, `rating.stars`, `body`), and 409 problems for concurrency (`urn:runnersdiary:problem:concurrency-conflict`) and run caps (`urn:runnersdiary:problem:run-limit`). `PUT /api/runs/{id}` is a **full replace** with optimistic `version`, and unknown JSON members are rejected. ADR 0002 keeps zod schemas hand-written, separate from wire types. Brief §2: react-hook-form + zod. Current packages (secondary sources, 2026-08): react-hook-form 7.x, zod 4.x, `@hookform/resolvers` 5.x, where `zodResolver` supports zod 4 from 5.1 (at install time resolvers was on 7.x; see the amendment). shadcn/ui now builds forms with **`Field`** components plus RHF `Controller`.

## Decision

1. **Form model ≠ wire model.** `RunFormValues` uses display units and strings: `distanceKm`, `duration` (`h:mm:ss`), `race.targetTime`, `race.resultTime`, `elevationGain`/`elevationLoss` (m), `date` (`YYYY-MM-DD`), `startTime` (`HH:mm` or empty), `timeZoneId` (shown only when changed). Two pure, unit-tested functions convert between models:
   - `toFormValues(dto | defaults)`
   - `toWriteRequest(values, base?: RunDto)`: SI conversion and empty → `null`. Fields the M3 form does not render (`location`) are **copied from `base`**, so a full-replace PUT never drops them. A numeric field whose shown text still equals the formatted `base` value is sent as the **exact `base` value** (a GPX-derived 10 234.567 m stays 10 234.567, not 10 235). Read-only DTO members (`id`, `startUtc`, `tracks`, `createdAt`, `updatedAt`) are never sent. `version` comes from `base`. The request literal goes through `withAllWriteMembers`, a **type-level exhaustiveness check over `RunWriteRequest`**: a member added to the API and not named here fails `typecheck`.
2. **zod schema mirrors the API rules** for instant feedback. It covers ranges and lengths from `api-contract.md`, plus the cross-field rules:
   - rating only when Completed;
   - race only for Race-category types;
   - rest-day type: no metrics;
   - `maxHR ≥ avgHR`; distance > 0; elevation ≥ 0;
   - race result and place only when Completed.

   The UI also *prevents* most of these: the rating section is visible only when Completed, the race section only for race types, and metric fields are hidden for rest days. Moving back to Planned clears the rating. The server still has the final word.
3. **Server 400 → fields**: `lib/api/problem.ts` normalizes every error into a discriminated union: `validation{fieldErrors}`, `concurrency`, `runLimit`, `rateLimited{retryAfter}`, `guestCapacity{retryAfter}`, `notFound`, `unauthorized`, `payloadTooLarge`, `unavailable`, `unknown`. The form maps `errors` keys through a table (`distanceMeters → distanceKm`, `durationSeconds → duration`, `race.targetTimeSeconds → race.targetTime`, `race.resultTimeSeconds → race.resultTime`, `elevationGainMeters → elevationGain`, `elevationLossMeters → elevationLoss`, otherwise identity) and calls `setError`. Keys it cannot map, including `body`, go to a form-level error summary (`role="alert"`, focused). Server message texts are shown as-is for field errors, because they are fixed English messages. Localized text is used for all problem *types*.
4. **409 concurrency**: the form stays open with the user's values. An inline alert offers **Reload latest**, which refetches and resets the form (discarding edits), and **Save mine anyway**, which refetches only the `version` and resubmits the user's values. **409 run-limit**: form-level message (localized, generic). **404 on save/delete**: the run is gone; close the dialog, show a toast, and invalidate the queries.
5. **UI building blocks**: shadcn `Field`/`FieldLabel`/`FieldError`/`FieldDescription` through one `FormField` wrapper (`aria-invalid`, `aria-describedby`), inputs bound with RHF **`register`** (uncontrolled; `Controller` only for the rating), native `<input type="date|time">` and **native selects** (shadcn `native-select`). The rating is a native 5-star radio group (one tab stop, arrow keys, value also as text). The live pace is shown visually and announced through a **debounced** sr-only `<output>`. Submit is disabled while pending, and double submit is guarded.
6. **Quick actions** reuse the same mapping: **Mark as completed** (`…/edit?complete=1`) opens edit with `status = Completed` preset and focuses the first empty actual (distance/duration). **Rate** (completed runs, `…/runs/:runId?rate=1`) is a small dialog whose PUT is built with `toWriteRequest(toFormValues(dto) + rating, dto)`. On 409 it refetches the latest run, keeps the typed rating and asks the user to save again (no Reload/Save-mine choice: the rating is the only edit).

## Consequences

- Instant client feedback without duplicating authority. A drift between the zod rules and the server only degrades to a server-side field error, never to bad data.
- The base-merge in `toWriteRequest` is the critical piece for M4 (location) and later fields. It is unit-tested against a complete `RunDto` fixture: round-trip equality and no extra members.

## Alternatives considered

- **Server-only validation**: simpler, but a round trip for every typo and poor UX on slow or cold API starts.
- **zod generated from OpenAPI (Orval / openapi-zod)**: couples the form to the wire shape, which is in SI units, and loses the cross-field rules. Rejected in ADR 0002 too.
- **TanStack Form**: good, but the brief and ADR 0003 name react-hook-form, and shadcn documents both.

## References (checked 2026-11-03)

- shadcn/ui Field: https://ui.shadcn.com/docs/components/field ; React Hook Form guide (Controller + Field + zodResolver, `aria-invalid`): https://ui.shadcn.com/docs/forms/react-hook-form
- `@hookform/resolvers` releases (5.4.2: zod 4.3 type fix, 2026-07-25): https://github.com/react-hook-form/resolvers/releases/tag/v5.4.2
- ⚠ Exact current versions (RHF 7.85, zod 4.4, resolvers 5.7) come from a secondary source dated 2026-08; frontend-react pins whatever `npm view` reports at install. *Installed: react-hook-form 7.91.0, zod 4.6.7, `@hookform/resolvers` 7.0.1.*

## Amended 2026-11-03 (implementation)

Decision unchanged; edited in place to match M3 as built ([plan §12](../plans/M3-calendar-ui.md#12-implementation-notes--deviations-2026-11-03) #2, #3, #13):
- Context: `@hookform/resolvers` is on **7.x** (7.0.1 installed), not 5.x.
- §1: untouched numbers keep exact SI values; `withAllWriteMembers` exhaustiveness check.
- §5: `register` + native selects instead of `Controller` + shadcn `Select`; native radio rating; debounced pace announcement.
- §6: Rate route `?rate=1`; Rate-dialog 409 behaviour.
