/**
 * Calendar dates as `YYYY-MM-DD` strings, with no `Date` instants in the public API (ADR 0017, ADR 0008).
 * Arithmetic is integer-based and goes through UTC only internally, so nothing shifts with the browser's zone.
 * Labels use `Intl` with `timeZone: 'UTC'` applied to a UTC midnight, so they cannot shift either.
 * Feature code must not call `new Date(<string>)`, `Date.parse` or `toISOString().slice(0, 10)`.
 *
 * Deliberately has no imports (the fake API in src/test runs under Playwright's Node process too).
 */
export type PlainDate = string

export interface DateParts {
  year: number
  /** 1–12 */
  month: number
  /** 1–31 */
  day: number
}

const PLAIN_DATE = /^(\d{4})-(\d{2})-(\d{2})$/

function utcMs(year: number, month: number, day: number): number {
  const d = new Date(0)
  d.setUTCFullYear(year, month - 1, day)
  return d.getTime()
}

function fromMs(ms: number): DateParts {
  const d = new Date(ms)
  return { year: d.getUTCFullYear(), month: d.getUTCMonth() + 1, day: d.getUTCDate() }
}

/** Parses `YYYY-MM-DD`; null for anything else, including impossible dates such as 2026-02-30. */
export function parse(value: string): DateParts | null {
  const m = PLAIN_DATE.exec(value)
  if (!m) return null
  const year = Number(m[1])
  const month = Number(m[2])
  const day = Number(m[3])
  if (month < 1 || month > 12 || day < 1 || day > daysInMonth(year, month)) return null
  return { year, month, day }
}

export function isValid(value: string): boolean {
  return parse(value) !== null
}

export function format({ year, month, day }: DateParts): PlainDate {
  return `${String(year).padStart(4, '0')}-${String(month).padStart(2, '0')}-${String(day).padStart(2, '0')}`
}

function mustParse(value: PlainDate): DateParts {
  const parts = parse(value)
  if (!parts) throw new RangeError(`Not a plain date: ${value}`)
  return parts
}

export function isLeapYear(year: number): boolean {
  return (year % 4 === 0 && year % 100 !== 0) || year % 400 === 0
}

export function daysInMonth(year: number, month: number): number {
  if (month === 2) return isLeapYear(year) ? 29 : 28
  return [4, 6, 9, 11].includes(month) ? 30 : 31
}

export function addDays(value: PlainDate, days: number): PlainDate {
  const { year, month, day } = mustParse(value)
  return format(fromMs(utcMs(year, month, day + days)))
}

/** Adds calendar months and clamps the day to the target month (Jan 31 + 1 month = Feb 28/29). */
export function addMonths(value: PlainDate, months: number): PlainDate {
  const { year, month, day } = mustParse(value)
  const index = year * 12 + (month - 1) + months
  const y = Math.floor(index / 12)
  const m = (index % 12) + 1
  return format({ year: y, month: m, day: Math.min(day, daysInMonth(y, m)) })
}

/** ISO weekday: Monday = 1 … Sunday = 7. */
export function isoWeekday(value: PlainDate): number {
  const { year, month, day } = mustParse(value)
  const dow = new Date(utcMs(year, month, day)).getUTCDay()
  return dow === 0 ? 7 : dow
}

/** Days from `a` to `b` (positive when `b` is later). */
export function diffDays(a: PlainDate, b: PlainDate): number {
  const pa = mustParse(a)
  const pb = mustParse(b)
  return Math.round((utcMs(pb.year, pb.month, pb.day) - utcMs(pa.year, pa.month, pa.day)) / 86_400_000)
}

export function compare(a: PlainDate, b: PlainDate): number {
  return a < b ? -1 : a > b ? 1 : 0
}

/** The Monday on or before `value`. */
export function startOfIsoWeek(value: PlainDate): PlainDate {
  return addDays(value, 1 - isoWeekday(value))
}

/** ISO 8601 week and week-based year (the week containing the Thursday decides the year). */
export function isoWeek(value: PlainDate): { isoYear: number; isoWeek: number } {
  const thursday = addDays(value, 4 - isoWeekday(value))
  const { year } = mustParse(thursday)
  return { isoYear: year, isoWeek: Math.floor(diffDays(format({ year, month: 1, day: 1 }), thursday) / 7) + 1 }
}

export function monthOf(value: PlainDate): { year: number; month: number } {
  const { year, month } = mustParse(value)
  return { year, month }
}

export function firstOfMonth(year: number, month: number): PlainDate {
  return format({ year, month, day: 1 })
}

export function lastOfMonth(year: number, month: number): PlainDate {
  return format({ year, month, day: daysInMonth(year, month) })
}

/** "Today" is always the API's `me.today` (the user's zone), never the browser clock (ADR 0017). */
export function todayFromMe(me: { today: string }): PlainDate {
  mustParse(me.today)
  return me.today
}

function labelFormat(options: Intl.DateTimeFormatOptions, locale: string, value: PlainDate): string {
  const { year, month, day } = mustParse(value)
  return new Intl.DateTimeFormat(locale, { ...options, timeZone: 'UTC' }).format(utcMs(year, month, day))
}

/** "November 2026" */
export function monthLabel(year: number, month: number, locale: string): string {
  return labelFormat({ month: 'long', year: 'numeric' }, locale, firstOfMonth(year, month))
}

/** "November" / "Nov" for a month number 1-12. */
export function monthName(month: number, locale: string, width: 'long' | 'short' = 'long'): string {
  return labelFormat({ month: width }, locale, firstOfMonth(2024, month))
}

/** "Saturday, November 14, 2026" */
export function dayLabel(value: PlainDate, locale: string): string {
  return labelFormat({ weekday: 'long', year: 'numeric', month: 'long', day: 'numeric' }, locale, value)
}

/** "Nov 14" */
export function shortDayLabel(value: PlainDate, locale: string): string {
  return labelFormat({ month: 'short', day: 'numeric' }, locale, value)
}

/** "Aug 17 – Nov 8, 2026" (one date when `from` equals `to`); the range collapses shared parts per locale. */
export function rangeLabel(from: PlainDate, to: PlainDate, locale: string): string {
  const a = mustParse(from)
  const b = mustParse(to)
  const formatter = new Intl.DateTimeFormat(locale, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    timeZone: 'UTC',
  })
  const start = utcMs(a.year, a.month, a.day)
  const end = utcMs(b.year, b.month, b.day)
  return start === end ? formatter.format(start) : formatter.formatRange(start, end)
}

/** Weekday names Monday-first, e.g. ["Mon", …] or ["Monday", …]. */
export function weekdayLabels(locale: string, width: 'short' | 'long' | 'narrow' = 'short'): string[] {
  // 2024-01-01 was a Monday.
  return Array.from({ length: 7 }, (_, i) => labelFormat({ weekday: width }, locale, addDays('2024-01-01', i)))
}
