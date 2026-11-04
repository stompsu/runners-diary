import type { TFunction } from 'i18next'
import { runTypeLabelKey } from '@/features/calendar/runTypePresentation'
import { parse, rangeLabel } from '@/lib/dates/plainDate'
import { formatDuration } from '@/lib/format/duration'
import { formatDistance, formatElevation, formatPaceValue } from '@/lib/format/units'
import {
  formatCount,
  formatDeltaAmount,
  formatDeltaPercent,
  formatLoad,
  formatRatio,
  formatShare,
} from '../insightsFormat'
import type { SummaryValue } from '../insightsTypes'

export interface SummaryFormatContext {
  locale: string
  t: TFunction<'insights'>
  tType: TFunction<'runTypes'>
}

const BANDS = ['low', 'optimal', 'high'] as const
const CATEGORIES = ['fiveK', 'tenK', 'halfMarathon', 'marathon', 'other'] as const
const STATUSES = ['planned', 'completed'] as const

function oneOf<T extends string>(list: readonly T[], value: string | null): T | null {
  return list.find((item) => item === value) ?? null
}

/** Deltas travel as `comparison.delta.<metric>.absolute`; the dashboard writes them with a sign. */
const isAbsoluteDelta = (key: string) => key.endsWith('.absolute')
/** `meters` is used for distances and for elevation; only the key tells them apart. */
const isElevation = (key: string) => /elevation/i.test(key)

/**
 * Formats one resolved value with the helpers the dashboard uses (SI in, km / h:mm:ss / min/km out). Returns null
 * when the value cannot be shown (missing number or text, unknown code); the caller then renders nothing.
 */
export function formatSummaryValue(key: string, value: SummaryValue, ctx: SummaryFormatContext): string | null {
  const { locale, t, tType } = ctx
  const { number, text } = value
  const n = number !== null && Number.isFinite(number) ? number : null

  switch (value.unit) {
    case 'meters':
      if (n === null) return null
      if (isAbsoluteDelta(key)) return formatDeltaAmount(isElevation(key) ? 'elevation' : 'distance', n, locale)
      return isElevation(key) ? formatElevation(n, locale) : formatDistance(n, locale, 1)
    case 'seconds':
      if (n === null) return null
      return isAbsoluteDelta(key) ? formatDeltaAmount('duration', n, locale) : formatDuration(n)
    case 'load':
      if (n === null) return null
      return isAbsoluteDelta(key) ? formatDeltaAmount('load', n, locale) : formatLoad(n, locale)
    case 'ratio':
      return n === null ? null : formatRatio(n, locale)
    case 'percent':
      return n === null ? null : formatDeltaPercent(n, locale)
    case 'share':
      return n === null ? null : formatShare(n, locale)
    case 'secondsPerKm':
      return n === null ? null : formatPaceValue(n)
    case 'secondsPerKmPerWeek':
      return n === null
        ? null
        : t('summary.unit.paceSlope', {
            value: new Intl.NumberFormat(locale, { maximumFractionDigits: 1, signDisplay: 'exceptZero' }).format(n),
          })
    case 'count':
      if (n === null) return null
      return isAbsoluteDelta(key) ? formatDeltaAmount('count', n, locale) : formatCount(n, locale)
    case 'days':
      if (n === null) return null
      return isAbsoluteDelta(key)
        ? t('summary.unit.daysDelta', { amount: formatDeltaAmount('count', n, locale) })
        : t('summary.unit.days', { count: n })
    case 'date':
      return text !== null && parse(text) ? rangeLabel(text, text, locale) : null
    case 'band': {
      const band = oneOf(BANDS, text)
      return band ? t(`acwr.bandShort.${band}`) : null
    }
    case 'runType':
      return text ? tType(runTypeLabelKey(text)) : null
    case 'raceCategory': {
      const category = oneOf(CATEGORIES, text)
      return category ? t(`summary.raceCategory.${category}`) : null
    }
    case 'raceStatus': {
      const status = oneOf(STATUSES, text)
      return status ? t(`summary.raceStatus.${status}`) : null
    }
    case 'flag':
      return text === 'true' ? t('summary.flag.yes') : text === 'false' ? t('summary.flag.no') : null
    case 'stars':
      return n === null ? null : t('summary.unit.stars', { count: n })
    default:
      return null
  }
}

function dateTime(iso: string, locale: string, zone: string | undefined, options: Intl.DateTimeFormatOptions) {
  const at = new Date(iso)
  if (Number.isNaN(at.getTime())) return null
  try {
    return new Intl.DateTimeFormat(locale, { ...options, timeZone: zone }).format(at)
  } catch {
    // An unknown zone id must not break the card: fall back on UTC.
    return new Intl.DateTimeFormat(locale, { ...options, timeZone: 'UTC' }).format(at)
  }
}

/** "Nov 4, 2026" in the user's zone (the stored instant is UTC). */
export const formatCreated = (iso: string, locale: string, zone?: string) =>
  dateTime(iso, locale, zone, { dateStyle: 'medium' })

/** "Nov 5, 3:30 PM" in the user's zone. */
export const formatResetTime = (iso: string, locale: string, zone?: string) =>
  dateTime(iso, locale, zone, { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' })
