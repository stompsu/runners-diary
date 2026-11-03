# 0008 – Run date & time: local calendar date + optional local time + IANA time zone

**Status:** Accepted (decided by the user, 2026-11-03; implemented in M2)
**Date:** 2026-11-03

## Context

The diary is organized by calendar days: calendar bubbles, ISO-week totals (Monday-first), monthly views, 7-/28-day training load (ACWR), "the week before the race". A run must stay on the day the runner experienced it, regardless of where the diary is viewed from.

Storing only a UTC instant breaks this:
- Runs near midnight move to another day (and, on Sundays, another ISO week) when grouped by UTC. Example: a 01:30 run in Gliwice in summer (UTC+2) is 23:30 UTC on the previous day.
- A run recorded while travelling (08:00 in New York) shows at a different time and possibly a different day depending on the viewer's zone.
- Planned runs often have no start time ("Sunday long run"); a UTC instant forces an invented time.

## Decision

A run stores its **wall-clock** date and time plus the zone they belong to; the UTC instant is derived.

| Field | SQL type | .NET type | Notes |
|---|---|---|---|
| `Date` | `date` | `DateOnly` | **Source of truth** for calendar placement, week/month grouping and all load metrics. Indexed with `UserId` for date-range queries. |
| `StartTime` | `time(0)` NULL | `TimeOnly?` | Optional (typical for planned runs). |
| `TimeZoneId` | `varchar(64)` | `string` | IANA id (e.g. `Europe/Warsaw`), validated against the runtime's time-zone database. |
| `StartUtc` | `datetime2(0)` NULL | `DateTime?` (Kind=Utc) | Computed in the domain from the three fields above whenever they change; NULL when `StartTime` is NULL. Used for ordering within a day, GPX matching and elapsed time between sessions — never for calendar grouping. |

- **User profile** has a default `TimeZoneId`, prefilled on first sign-in from the browser (`Intl.DateTimeFormat().resolvedOptions().timeZone`) and editable. New runs inherit it; the run form shows the zone only when the user changes it (e.g. travel).
- **Guests / demo data** use `Europe/Warsaw`; demo dates are generated relative to "today" in that zone.
- **DST rules** (domain logic, unit-tested in `RunnersDiary.Domain`):
  - Non-existent local time (spring forward, e.g. 02:30 on the last Sunday of March in Europe/Warsaw) → shift forward by the gap (03:30).
  - Ambiguous local time (fall back, 02:30 occurs twice in October) → the earlier occurrence (the first, daylight-time offset).
- **GPX import** (UTC timestamps): the first trackpoint's UTC time is converted with the run's `TimeZoneId` to fill `Date` / `StartTime`.
- **"Today"** for the user (calendar default month, demo data generation, load windows) is computed in the user's time zone, not the server's.
- API contract: `date` as `"YYYY-MM-DD"`, `startTime` as `"HH:mm"` (nullable), `timeZoneId` as an IANA string, `startUtc` read-only.

## Consequences

- Calendar, week totals and ACWR group by `Date` only — simple, index-friendly SQL with no time-zone math in queries.
- One extra column to keep consistent (`StartUtc`); the domain recomputes it, and tests cover DST edges.
- Two runs in different zones on the same day are ordered by `StartUtc`, which is correct in absolute time.
- Changing the profile time zone does **not** move existing runs (they keep their own `TimeZoneId`), which is the desired behaviour for travel and relocation.

## Alternatives considered

- **UTC instant only** — rejected: day/week misplacement near midnight, viewer-dependent display, no way to express a date-only planned run.
- **UTC instant as truth + cached local date** — workable but cannot represent date-only planned runs without an invented time; the cache could disagree with the truth after zone-rule updates.
- **Local date/time without a zone** — loses the ability to match GPX tracks and order runs across zones.
