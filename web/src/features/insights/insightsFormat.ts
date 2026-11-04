import { formatDuration } from '@/lib/format/duration'
import { formatPaceValue } from '@/lib/format/units'
import { diffDays } from '@/lib/dates/plainDate'
import type { DataQuality, Delta, InsightsWeek, RaceResult } from './insightsTypes'

/** What a delta is measured in; decides the unit and how the absolute value is written. */
export type DeltaKind = 'distance' | 'duration' | 'count' | 'elevation' | 'load' | 'pace'

export const PERCENT_UNAVAILABLE = '—'

/** Load in arbitrary units (RPE x minutes): "1,156 AU". `AU` is a unit symbol like `/km`, not translated. */
export function formatLoad(value: number, locale: string): string {
  return `${new Intl.NumberFormat(locale, { maximumFractionDigits: 0 }).format(value)} AU`
}

/** ACWR and monotony: two decimals. */
export function formatRatio(value: number, locale: string): string {
  return new Intl.NumberFormat(locale, { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(value)
}

export function formatCount(value: number, locale: string): string {
  return new Intl.NumberFormat(locale, { maximumFractionDigits: 0 }).format(value)
}

/** 0.18 -> "18%". */
export function formatShare(fraction: number, locale: string): string {
  return new Intl.NumberFormat(locale, { style: 'percent', maximumFractionDigits: 0 }).format(fraction)
}

function signed(value: number, locale: string, maximumFractionDigits: number, unit?: string): string {
  return new Intl.NumberFormat(locale, {
    ...(unit ? { style: 'unit', unit, unitDisplay: 'short' } : {}),
    maximumFractionDigits,
    signDisplay: 'exceptZero',
  }).format(value)
}

/** A signed absolute change in the unit of `kind`: "+47.7 km", "-0:05:00", "+904 m", "-6 s/km". */
export function formatDeltaAmount(kind: DeltaKind, absolute: number, locale: string): string {
  switch (kind) {
    case 'distance':
      return signed(absolute / 1000, locale, 1, 'kilometer')
    case 'duration':
      return `${absolute < 0 ? '-' : '+'}${formatDuration(Math.abs(absolute))}`
    case 'elevation':
      return signed(absolute, locale, 0, 'meter')
    case 'load':
      return `${signed(absolute, locale, 0)} AU`
    case 'pace':
      return `${signed(absolute, locale, 0)} s/km`
    case 'count':
      return signed(absolute, locale, 0)
  }
}

/** "+14.2%"; the dash when there is nothing to divide by (previous period = 0). */
export function formatDeltaPercent(percent: number | null, locale: string): string {
  return percent === null ? PERCENT_UNAVAILABLE : `${signed(percent, locale, 1)}%`
}

/** Direction in words' terms; a change that rounds to nothing in the shown unit is "same". */
export function deltaDirection(kind: DeltaKind, delta: Delta): 'up' | 'down' | 'same' {
  const threshold = kind === 'pace' ? 0.5 : 0
  if (Math.abs(delta.absolute) <= threshold) return 'same'
  return delta.absolute > 0 ? 'up' : 'down'
}

/** Pace in s/km as the bare clock "5:51" (axis ticks; the unit is in the axis label). */
export function formatPaceClock(secondsPerKm: number): string {
  const total = Math.round(secondsPerKm)
  return `${Math.floor(total / 60)}:${String(total % 60).padStart(2, '0')}`
}

/** Pace in s/km as "5:51 /km", or the dash when there is none. */
export function formatPaceOrDash(secondsPerKm: number | null): string {
  return secondsPerKm === null ? PERCENT_UNAVAILABLE : formatPaceValue(secondsPerKm)
}

/** Days of logged training up to `asOf`: the number the "available after 35 days" message counts. */
export function loggedDays(historyStart: string | null, asOf: string): number {
  return historyStart === null ? 0 : Math.max(0, diffDays(historyStart, asOf) + 1)
}

/** Monotony and strain exist only for complete ISO weeks (ADR 0024 §4): the latest one. */
export function lastCompleteWeek(weeks: readonly InsightsWeek[]): InsightsWeek | null {
  return [...weeks].reverse().find((week) => week.complete) ?? null
}

/** Races newest first; the report lists planned and completed races of the period. */
export function sortRaces(races: readonly RaceResult[]): RaceResult[] {
  return [...races].sort((a, b) => (a.date < b.date ? 1 : a.date > b.date ? -1 : 0))
}

/** True when something in the data makes the numbers less exact (estimated RPE, sessions without duration/distance). */
export function hasDataQualityIssues(quality: DataQuality): boolean {
  return (
    quality.estimatedLoadShare > 0 || quality.sessionsWithoutDuration > 0 || quality.runningSessionsWithoutDistance > 0
  )
}

/** `useId()` values go into `url(#…)`; keep only characters that are safe there. */
export function safeSvgId(id: string): string {
  return `p${id.replace(/[^a-zA-Z0-9_-]/g, '')}`
}
