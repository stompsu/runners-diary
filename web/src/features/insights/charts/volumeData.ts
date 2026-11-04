import { monthLabel, monthName, shortDayLabel } from '@/lib/dates/plainDate'
import { formatDuration } from '@/lib/format/duration'
import { formatDistance, formatElevation } from '@/lib/format/units'
import { formatLoad } from '../insightsFormat'
import type { InsightsReport } from '../insightsTypes'

export type VolumeMetric = 'distance' | 'duration' | 'load' | 'elevation'
export const METRICS: readonly VolumeMetric[] = ['distance', 'duration', 'load', 'elevation']
/** Periods longer than 16 weeks are shown by month; shorter ones by ISO week. */
export const MAX_WEEKLY_DAYS = 16 * 7

export interface VolumeRow {
  key: string
  label: string
  tick: string
  completed: number
  planned: number
}

/** Chart units: km, hours, AU, metres. */
function toChartUnit(
  metric: Exclude<VolumeMetric, 'load'>,
  totals: { distanceMeters: number; durationSeconds: number; elevationGainMeters: number },
) {
  switch (metric) {
    case 'distance':
      return totals.distanceMeters / 1000
    case 'duration':
      return totals.durationSeconds / 3600
    case 'elevation':
      return totals.elevationGainMeters
  }
}

export function volumeRows(
  report: InsightsReport,
  metric: VolumeMetric,
  weekly: boolean,
  locale: string,
  weekOf: (date: string) => string,
): VolumeRow[] {
  if (weekly) {
    return report.weeks.map((week) => ({
      key: week.start,
      label: weekOf(shortDayLabel(week.start, locale)),
      tick: shortDayLabel(week.start, locale),
      completed: metric === 'load' ? week.load : toChartUnit(metric, week.completed),
      planned: metric === 'load' ? 0 : toChartUnit(metric, week.planned),
    }))
  }
  return report.months.map((month) => ({
    key: month.start,
    label: monthLabel(month.year, month.month, locale),
    tick: monthName(month.month, locale, 'short'),
    completed: metric === 'load' ? month.load : toChartUnit(metric, month.completed),
    planned: metric === 'load' ? 0 : toChartUnit(metric, month.planned),
  }))
}

export function formatVolumeValue(metric: VolumeMetric, chartValue: number, locale: string): string {
  switch (metric) {
    case 'distance':
      return formatDistance(chartValue * 1000, locale, 1)
    case 'duration':
      return formatDuration(chartValue * 3600)
    case 'elevation':
      return formatElevation(chartValue, locale)
    case 'load':
      return formatLoad(chartValue, locale)
  }
}
