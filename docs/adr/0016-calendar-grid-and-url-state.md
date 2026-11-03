# 0016 – Calendar: hand-built ARIA grid, URL-addressable panels and dialogs

**Status:** Accepted (M3 plan approved 2026-11-03) · Amended 2026-11-03 (implementation, see the end)
**Date:** 2026-11-03

## Context

The brief asks for a monthly diary grid: Monday first, an ISO week column with totals, run "bubbles" coloured by type, planned runs dashed and completed runs filled, and a race marker. It needs prev/next, Today and a month/year picker, a bookmarkable URL (`/diary/2026/11`), day and month list panels, and add/edit/delete dialogs. The grid must use ARIA grid semantics, support the keyboard, and work on a phone. The API already returns the grid bounds and ISO week totals (`GET /api/calendar/months/{y}/{m}`), so the client does not compute calendar math or metrics. The router is React Router v8 in data mode (ADR 0003).

## Decision

1. **Hand-built grid** (`features/calendar/MonthGrid.tsx`), following the WAI-ARIA APG grid / date-grid pattern:
   - A `<table role="grid">` (native table semantics; oxlint `prefer-tag-over-role`) named with `aria-label` ("Calendar for November 2026"). One header row: "Week" first, then Mon…Sun (visible abbreviation `aria-hidden`, full name as sr-only text). One row per ISO week: the week `rowheader` cell with the totals **first**, then 7 day `gridcell`s.
   - **Roving tabindex**: exactly one day cell is in the tab order. On load it is the selected day, otherwise today if it is in view, otherwise the 1st. Arrow keys move by a day or a week. Home/End go to Monday/Sunday. PageUp/PageDown go to the previous/next month: they navigate the URL and keep the same day number, clamped. Enter/Space opens the day panel. Moving past the grid edge changes the month.
   - Each cell's accessible name reads the full date plus a run summary, e.g. "Saturday, November 14, 2026, 2 runs: Long run, Completed, 21.1 km; Strides and drills, Planned". The name comes from **sr-only text inside the cell** (the visible day number and bubbles are `aria-hidden`), not from `aria-label`, so axe's `label-content-name-mismatch` cannot fire. Today gets `aria-current="date"`. Days outside the month are dimmed but stay navigable.
   - Bubbles inside a cell are presentational for keyboard users: the cell is one focus stop and the day panel lists the runs with their actions. Mouse/touch users can click a bubble to open that run directly.
   - Never colour alone: planned = dashed outline, completed = filled, race = trophy icon, plus the type's colour token (`--run-<family>` CSS variables in `index.css`, about 8 families covering the 20 run types). Text contrast is ≥ 4.5:1.
   - **Phone** (< 640 px): the same 8-column grid with compact bubbles (coloured dots + total km for the day). The week column shows distance only; time and elevation appear in its accessible name and in the week detail of the month list. Panels and dialogs open as bottom sheets: one `ResponsiveDialog` (a Radix dialog styled as a sheet below `sm`), not a separate shadcn `Sheet`. No horizontal scroll at 360 px.
2. **URL is the state** (nested routes under `/diary/:year/:month`, rendered through an `<Outlet>` as Sheet/Dialog). Back/forward closes and reopens them:

   | Route | UI |
   |---|---|
   | `/diary/:year/:month` | grid |
   | `/diary/:year/:month/day/:date` | day panel (`date` = `YYYY-MM-DD`) |
   | `/diary/:year/:month/list` | month list panel |
   | `/diary/:year/:month/runs/new?date=YYYY-MM-DD` | add run |
   | `/diary/:year/:month/runs/:runId` | run details (`?rate=1` opens the Rate dialog for a Completed run) |
   | `/diary/:year/:month/runs/:runId/edit` | edit run (`?complete=1` presets Completed) |

   Year range 1950–2100 (matches the API). `/`, `/diary` → `/diary/<me.today year>/<month>`. Panel routes are code-split with React Router `lazy` and preloaded in the background once the grid has rendered. Closing a panel goes `navigate(-1)` when it was opened inside the app and replaces itself with the month after a deep link. **Nested focus return**: an action link passes a `returnFocus` selector in navigation state; on close it is handed to the dialog that remounts, which focuses that link. Closing to the grid focuses the originating day cell. Browser Back does not restore nested focus (accepted).
3. **Data**: one query per visible month, `getCalendarMonth`. The day and month panels read runs from that cached response, so they make no extra request. Run details and edit fetch `getRun` (fresh `version`). The previous and next months are prefetched once the visible month has loaded. Mutations invalidate every `getCalendarMonth` and `listRuns` query (prefix match on the openapi-react-query key) and the `getRun` query for the run.
4. **Month/year picker**: a `Popover` with a year stepper and a 12-month button grid (each button navigates). **Today** goes to `me.today`'s month and focuses today. Month changes are announced through a polite sr-only status.

## Consequences

- Full control of markup, ARIA and focus. The calendar logic is small, because the API supplies grid bounds and totals, and it can be tested with RTL + user-event.
- Deep links into dialogs (`…/runs/<id>/edit`) make E2E tests and bookmarks simple. Closing a dialog navigates to the parent route.
- About 300–400 lines of owned UI code. Keyboard behaviour must be covered by tests (risk if neglected).

## Alternatives considered

- **shadcn `Calendar` (react-day-picker)**: a date *picker*. It has no week totals column or multi-item cells, and customising it into a diary grid costs more than building one. Native `<input type="date">` covers date entry in forms.
- **FullCalendar / react-big-calendar**: heavy, opinionated styling. Week-number totals need custom rendering anyway, and the a11y of their month views is weaker than the APG grid pattern.
- **Dialog state in component state or search params only**: not bookmarkable, and Back does not close the dialog.

## References

- WAI-ARIA APG Grid pattern and date-picker dialog (grid keyboard model): https://www.w3.org/WAI/ARIA/apg/patterns/grid/ , https://www.w3.org/WAI/ARIA/apg/patterns/dialog-modal/examples/datepicker-dialog/ (not re-fetched 2026-11-03; stable W3C guidance)
- ADR 0003 (React Router v8 data mode), ADR 0008 (dates), `api-contract.md` (`CalendarMonthResponse`).

## Amended 2026-11-03 (implementation)

Decision unchanged; edited in place to match M3 as built ([plan §12](../plans/M3-calendar-ui.md#12-implementation-notes--deviations-2026-11-03) #3, #8–#11):
- §1: `<table role="grid">` with `aria-label` (was "labelled by the month heading"); week column first; cell names from sr-only content (was implied `aria-label`); weekday names via sr-only text (was `abbr`/`aria-label`); bottom sheets via `ResponsiveDialog` (no shadcn `Sheet`).
- §2: Rate is `runs/:runId?rate=1`; lazy panel routes with background preload; close/focus-return rules.
- §3: neighbour prefetch after the visible month loads (was "when idle").
- §4: month-change announcement.
