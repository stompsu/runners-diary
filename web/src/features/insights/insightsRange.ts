import {
  addDays,
  addMonths,
  diffDays,
  firstOfMonth,
  isValid,
  lastOfMonth,
  monthOf,
  startOfIsoWeek,
  type PlainDate,
} from '@/lib/dates/plainDate'
import type { CompareMode } from './insightsTypes'

/** Dashboard periods (plan §3.4). The URL (`?view&at&from&to&compare`) is the only source of truth for them. */
export type InsightsView = 'week' | 'month' | '12w' | 'ytd' | 'custom'

export const INSIGHTS_VIEWS: readonly InsightsView[] = ['week', 'month', '12w', 'ytd', 'custom']
export const COMPARE_MODES: readonly CompareMode[] = ['previous', 'previousYear', 'none']

export const DEFAULT_VIEW = '12w' satisfies InsightsView
/** The API rejects longer spans (inclusive). */
export const MAX_SPAN_DAYS = 366
export const MIN_DATE = '1950-01-01'
export const MAX_DATE = '2100-12-31'
const TWELVE_WEEKS_DAYS = 84

export interface InsightsRange {
  view: InsightsView
  /** Anchor date of a week, month or 12-week view; `today` for the others. */
  at: PlainDate
  from: PlainDate
  to: PlainDate
  compare: CompareMode
}

export type CustomRangeError = 'invalidFrom' | 'invalidTo' | 'toBeforeFrom' | 'tooLong' | 'outOfRange'

export function defaultCompare(view: InsightsView): CompareMode {
  return view === 'ytd' ? 'previousYear' : 'previous'
}

/** Inclusive number of days. */
export function spanDays(from: PlainDate, to: PlainDate): number {
  return diffDays(from, to) + 1
}

/** Validates a custom range the way the API does (dates 1950–2100, `to >= from`, at most 366 days). */
export function validateCustomRange(from: string, to: string): CustomRangeError | null {
  if (!isValid(from)) return 'invalidFrom'
  if (!isValid(to)) return 'invalidTo'
  if (from < MIN_DATE || from > MAX_DATE || to < MIN_DATE || to > MAX_DATE) return 'outOfRange'
  if (to < from) return 'toBeforeFrom'
  if (spanDays(from, to) > MAX_SPAN_DAYS) return 'tooLong'
  return null
}

/** The `from`/`to` of a view; `at` is any date in the week/month (for `12w` the date whose week ends the period). */
export function bounds(view: Exclude<InsightsView, 'custom'>, at: PlainDate): { from: PlainDate; to: PlainDate } {
  const monday = startOfIsoWeek(at)
  switch (view) {
    case 'week':
      return { from: monday, to: addDays(monday, 6) }
    case 'month': {
      const { year, month } = monthOf(at)
      return { from: firstOfMonth(year, month), to: lastOfMonth(year, month) }
    }
    case '12w':
      return { from: addDays(monday, -77), to: addDays(monday, 6) }
    case 'ytd':
      return { from: firstOfMonth(monthOf(at).year, 1), to: at }
  }
}

function build(view: Exclude<InsightsView, 'custom'>, at: PlainDate, compare?: CompareMode): InsightsRange {
  return { view, at, ...bounds(view, at), compare: compare ?? defaultCompare(view) }
}

/** The preset for a view, anchored on the user's `today` (`me.today`, never the browser clock). */
export function presetRange(view: Exclude<InsightsView, 'custom'>, today: PlainDate): InsightsRange {
  return build(view, today)
}

export function defaultRange(today: PlainDate): InsightsRange {
  return presetRange(DEFAULT_VIEW, today)
}

/** A custom range; null when it is not valid. */
export function customRange(
  from: PlainDate,
  to: PlainDate,
  today: PlainDate,
  compare: CompareMode = 'previous',
): InsightsRange | null {
  return validateCustomRange(from, to) === null ? { view: 'custom', at: today, from, to, compare } : null
}

export interface ParsedRange {
  range: InsightsRange
  /** False when the URL had to be corrected (unknown view, bad dates, …): the page then replaces it. */
  canonical: boolean
}

function isView(value: string | null): value is InsightsView {
  return value !== null && (INSIGHTS_VIEWS as readonly string[]).includes(value)
}

function isCompare(value: string | null): value is CompareMode {
  return value !== null && (COMPARE_MODES as readonly string[]).includes(value)
}

/**
 * Reads `?view&at&from&to&compare`. Anything invalid falls back to the default view (`canonical: false`, so the
 * caller can `replace` the URL). Absent params are fine: `/insights` is the default view.
 */
export function parseInsightsParams(params: URLSearchParams, today: PlainDate): ParsedRange {
  const fallback = (): ParsedRange => ({ range: defaultRange(today), canonical: false })
  const viewParam = params.get('view')
  const compareParam = params.get('compare')
  if (compareParam !== null && !isCompare(compareParam)) return fallback()
  if (viewParam !== null && !isView(viewParam)) return fallback()
  const view = viewParam ?? DEFAULT_VIEW
  const compare = compareParam ?? defaultCompare(view)

  if (view === 'custom') {
    const from = params.get('from') ?? ''
    const to = params.get('to') ?? ''
    const range = customRange(from, to, today, compare)
    return range ? { range, canonical: true } : fallback()
  }

  const atParam = params.get('at')
  if (atParam !== null && (!isValid(atParam) || atParam < MIN_DATE || atParam > MAX_DATE)) return fallback()
  // YTD is always the year to the server's today; an `at` (or any other anchor) is a hand-edited URL.
  const at = view === 'ytd' ? today : (atParam ?? today)
  // `from`/`to` only belong to the custom view; a stray pair is a hand-edited URL.
  const canonical = !params.has('from') && !params.has('to') && !(view === 'ytd' && atParam !== null)
  const range = build(view, at, compare)
  // The computed period can leave the API's 1950..2100 window even when `at` is inside it (e.g. week of 1950-01-01).
  if (range.from < MIN_DATE || range.to > MAX_DATE) return fallback()
  return { range, canonical }
}

/** The shortest URL for a range: the default view, a 12-week anchor of today and the view's usual comparison are omitted. */
export function serializeInsightsRange(range: InsightsRange, today: PlainDate): URLSearchParams {
  const params = new URLSearchParams()
  if (range.view !== DEFAULT_VIEW) params.set('view', range.view)
  if (range.view === 'custom') {
    params.set('from', range.from)
    params.set('to', range.to)
  } else if (range.view === 'week' || range.view === 'month' || (range.view === '12w' && range.at !== today)) {
    params.set('at', range.at)
  }
  if (range.compare !== defaultCompare(range.view)) params.set('compare', range.compare)
  return params
}

/** The neighbouring period: one week, one month, 12 weeks or the same number of days. Null for YTD or out of range. */
export function shiftRange(range: InsightsRange, direction: -1 | 1, today: PlainDate): InsightsRange | null {
  let next: InsightsRange | null
  switch (range.view) {
    case 'ytd':
      return null
    case 'week':
      next = build('week', addDays(range.at, 7 * direction), range.compare)
      break
    case 'month':
      next = build(
        'month',
        addMonths(firstOfMonth(monthOf(range.at).year, monthOf(range.at).month), direction),
        range.compare,
      )
      break
    case '12w':
      next = build('12w', addDays(range.at, TWELVE_WEEKS_DAYS * direction), range.compare)
      break
    case 'custom': {
      const span = spanDays(range.from, range.to)
      next = customRange(
        addDays(range.from, span * direction),
        addDays(range.to, span * direction),
        today,
        range.compare,
      )
      break
    }
  }
  if (!next || next.from < MIN_DATE || next.to > MAX_DATE) return null
  // Planning ahead by one period is fine; a period that starts after today and has nothing to show is not offered.
  if (direction === 1 && range.from > today) return null
  return next
}

/** Switches to a preset keeping the user's comparison choice only when it is not the view's default. */
export function changeView(
  current: InsightsRange,
  view: Exclude<InsightsView, 'custom'>,
  today: PlainDate,
): InsightsRange {
  return build(view, today, current.compare === defaultCompare(current.view) ? undefined : current.compare)
}

/** Keeps the period and changes the comparison. */
export function changeCompare(range: InsightsRange, compare: CompareMode): InsightsRange {
  return { ...range, compare }
}
