# 0017 – Client-side dates: plain `YYYY-MM-DD` strings and a tiny helper module (no date library yet)

**Status:** Accepted (M3 plan approved 2026-11-03)
**Date:** 2026-11-03

## Context

ADR 0008: a run's `date` is a **local calendar date** (`DateOnly`). Its `startTime` is a wall-clock `HH:mm` in the run's IANA zone. "Today" is computed in the user's zone; the API returns it as `MeResponse.today`. A JS `Date` is an instant: `new Date("2026-11-14")` parses as **UTC midnight**, which shows as 13 November west of UTC. That is exactly the bug class ADR 0008 removes on the server.

The client needs only a few operations: parse/format `YYYY-MM-DD`, add days and months, ISO weekday, days in month, compare, and build localized labels. The API already provides grid bounds, ISO week numbers and totals.

Status of the options (checked 2026-11-03):
- **Temporal** (`Temporal.PlainDate` matches `DateOnly` exactly) ships in Chrome/Edge 144+, Firefox 139+ and Node 26 (on by default). It is **not in Safari** as of the newest sources found (May 2026). A polyfill would be required.
- **date-fns** v4 works on `Date` objects (instants), so the UTC/local parsing trap stays, and it adds a dependency.

## Decision

1. Dates travel as **`PlainDateString` (`YYYY-MM-DD`)** end to end. `web/src/lib/dates/plainDate.ts` (about 100 lines, unit-tested) provides `parse`, `format`, `addDays`, `addMonths`, `isoWeekday`, `daysInMonth`, `compare`, `monthOf`, `todayFromMe`. Internally it does integer arithmetic through `Date.UTC(...)` only.
2. Labels come from `Intl.DateTimeFormat(i18n.language, { ...opts, timeZone: "UTC" })` applied to `Date.UTC(y, m - 1, d)`. Because the format runs in UTC on a UTC midnight, the label never shifts.
3. **"Today" is `me.today` from the API**, never `new Date()`, for the default month, the Today button and the today highlight. Browser clock instants (`Date.now()`) are used only for durations between instants: the guest expiry countdown and "slow start" timers.
4. Times are `HH:mm` strings. Durations in forms are `h:mm:ss` strings, parsed into seconds by `lib/format/duration.ts`. Formatting km, pace (min/km) and elevation lives in `lib/format/units.ts`, all from SI.
5. Rules for reviewers: no `new Date(<string>)`, `Date.parse` or `toISOString().slice(0, 10)` in feature code; use the helpers. The date and calendar unit tests also run under `TZ=America/Los_Angeles` and `TZ=Pacific/Kiritimati` in CI, and one Playwright project runs with `timezoneId: "America/Los_Angeles"` against a fake API whose today is in Europe/Warsaw.
6. **Revisit**: once Safari ships Temporal (or the app accepts a polyfill), `plainDate.ts` becomes a thin wrapper over `Temporal.PlainDate`. The API (string in/out) does not change.

## Consequences

- Zero dependencies and no UTC shift by construction. The helpers are small enough to own.
- The time zone picker (travel) uses `Intl.supportedValuesOf("timeZone")`. It filters to the Area/Location form that the API accepts (`api-contract.md`).

## Alternatives considered

- **`temporal-polyfill` / `@js-temporal/polyfill`**: the right model, but about 20–60 KB gz for a handful of operations. Revisit with Safari support.
- **date-fns / dayjs / Luxon**: instant-based (`Date`), or larger than needed. They would still need discipline about parsing.

## References (checked 2026-11-03)

- Node.js 26 ships Temporal enabled (2026-05-07): https://www.helpnetsecurity.com/2026/05/07/node-js-26-released/
- Browser support summary, Safari gap (May 2026, secondary): https://lilting.ch/en/articles/nodejs-26-current-temporal-undici
- Safari 26 release notes (no Temporal found): https://developer.apple.com/documentation/safari-release-notes/safari-26-release-notes
- ⚠ Safari Temporal status could not be confirmed from an Apple primary source dated after May 2026.
