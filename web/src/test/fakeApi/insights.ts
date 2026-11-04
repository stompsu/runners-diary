import type { components } from '../../api/generated/schema.d.ts'
import {
  addDays,
  addMonths,
  diffDays,
  firstOfMonth,
  lastOfMonth,
  monthOf,
  parse,
  startOfIsoWeek,
} from '../../lib/dates/plainDate.ts'
import { demoReport } from './insightsFixture.ts'

type S = components['schemas']
export type InsightsReportDto = S['InsightsReportResponse']
type Totals = S['TotalsDto']
type PeriodTotals = S['PeriodTotalsDto']
type Week = S['InsightsWeekDto']
type Month = S['InsightsMonthDto']
type DailyLoad = S['DailyLoadDto']

export { demoReport, DEMO_REPORT_TODAY } from './insightsFixture.ts'

/**
 * What the fake `GET /api/insights/report` answers with. All scenarios are derived from the Domain golden report
 * (`demoReport`); the fake does not compute metrics, the real computation is covered by Domain/Api tests.
 * - `demo`: the guest's 12-week story wherever the requested range overlaps it, zeros elsewhere.
 * - `empty`: an account without completed runs.
 * - `insufficientHistory`: less than 35 days of training, so ACWR has no chronic load yet.
 * - `noChronicLoad`: training in the last 7 days but none in the 4 weeks before.
 * - `estimated`: like `demo`, but with estimated RPE and sessions without duration (data-quality note).
 */
export type InsightsScenario = 'demo' | 'empty' | 'insufficientHistory' | 'noChronicLoad' | 'estimated'

const COMPARE_MODES = ['none', 'previous', 'previousYear']
const round1 = (value: number) => Math.round(value * 10) / 10

const ZERO_TOTALS: Totals = { runCount: 0, distanceMeters: 0, durationSeconds: 0, elevationGainMeters: 0 }
const ZERO_PERIOD_TOTALS: PeriodTotals = {
  runCount: 0,
  distanceMeters: 0,
  durationSeconds: 0,
  elevationGainMeters: 0,
  averagePaceSecondsPerKm: null,
  longestRunMeters: null,
  load: 0,
  loadSessionCount: 0,
  activeDays: 0,
  restDays: 0,
}

export interface InsightsQuery {
  from: string
  to: string
  compare: string
}

/** Validation like the real endpoint: errors keyed `from|to|compare`, or null when the query is fine. */
export function validateInsightsQuery(query: { from: string | null; to: string | null; compare: string | null }) {
  const errors: Record<string, string[]> = {}
  const from = query.from ? parse(query.from) : null
  const to = query.to ? parse(query.to) : null
  if (!from) errors.from = ['From must be a valid date (YYYY-MM-DD).']
  if (!to) errors.to = ['To must be a valid date (YYYY-MM-DD).']
  if (query.compare !== null && !COMPARE_MODES.includes(query.compare))
    errors.compare = ['Compare must be none, previous or previousYear.']
  if (from && to) {
    if (from.year < 1950 || to.year > 2100) errors.from = ['Dates must be between 1950 and 2100.']
    else if (query.to! < query.from!) errors.to = ['To must not be before from.']
    else if (diffDays(query.from!, query.to!) + 1 > 366) errors.to = ['The period is at most 366 days.']
  }
  return Object.keys(errors).length > 0 ? errors : null
}

function sumPeriodTotals(weeks: Week[], from: string, asOf: string): PeriodTotals {
  const inside = weeks.filter((week) => week.start >= startOfIsoWeek(from) && week.start <= asOf)
  const sum = (pick: (week: Week) => number) => inside.reduce((total, week) => total + pick(week), 0)
  const distanceMeters = sum((week) => week.completed.distanceMeters)
  const durationSeconds = sum((week) => week.completed.durationSeconds)
  const runCount = sum((week) => week.completed.runCount)
  return {
    runCount,
    distanceMeters,
    durationSeconds,
    elevationGainMeters: sum((week) => week.completed.elevationGainMeters),
    averagePaceSecondsPerKm: distanceMeters > 0 ? round1(durationSeconds / (distanceMeters / 1000)) : null,
    longestRunMeters: runCount > 0 ? demoReport.totals.longestRunMeters : null,
    load: round1(sum((week) => week.load)),
    loadSessionCount: runCount,
    activeDays: runCount,
    restDays: 0,
  }
}

const sameWindow = (a: { from: string; to: string }, b: { from: string; to: string }) =>
  a.from === b.from && a.to === b.to

function delta(current: number, previous: number): S['DeltaDto'] {
  return {
    absolute: round1(current - previous),
    percent: previous === 0 ? null : round1(((current - previous) / previous) * 100),
  }
}

/** Same number of elapsed days one period back: a month/week-aligned period moves by calendar, others by length. */
function comparisonWindow(from: string, to: string, elapsedDays: number, kind: 'previous' | 'previousYear') {
  const { year, month } = monthOf(from)
  const wholeMonths = from === firstOfMonth(year, month) && to === lastOfMonth(monthOf(to).year, monthOf(to).month)
  let start: string
  if (kind === 'previousYear') start = wholeMonths ? addMonths(from, -12) : addDays(from, -364)
  else if (wholeMonths && monthOf(to).month === month) start = addMonths(from, -1)
  else start = addDays(from, -(diffDays(from, to) + 1))
  return { from: start, to: addDays(start, Math.max(elapsedDays, 1) - 1) }
}

export function buildInsightsReport(
  query: InsightsQuery,
  today: string,
  scenario: InsightsScenario,
): InsightsReportDto {
  const { from, to } = query
  const days = diffDays(from, to) + 1
  const asOf = to < today ? to : today
  const elapsedDays = asOf < from ? 0 : diffDays(from, asOf) + 1
  const period = { from, to, asOf, isPartial: asOf < to, days, elapsedDays }
  const base = scenario === 'empty' ? null : demoReport
  const exact = base !== null && sameWindow(period, demoReport.period) && today === demoReport.today

  // Weeks (whole ISO weeks) and months (whole calendar months) overlapping the period.
  const sourceWeeks = new Map((base?.weeks ?? []).map((week) => [week.start, week]))
  const weeks: Week[] = []
  for (let start = startOfIsoWeek(from); start <= to; start = addDays(start, 7)) {
    const end = addDays(start, 6)
    const known = sourceWeeks.get(start)
    const { isoYear, isoWeek } = isoParts(start)
    weeks.push(
      known
        ? { ...known, complete: end <= asOf }
        : {
            isoYear,
            isoWeek,
            start,
            end,
            completed: ZERO_TOTALS,
            planned: ZERO_TOTALS,
            load: 0,
            monotony: null,
            strain: null,
            acwrAtEnd: null,
            complete: end <= asOf,
          },
    )
  }
  const sourceMonths = new Map((base?.months ?? []).map((month) => [month.start, month]))
  const months: Month[] = []
  for (
    let { year, month } = monthOf(from);
    firstOfMonth(year, month) <= to;
    { year, month } = monthOf(addMonths(firstOfMonth(year, month), 1))
  ) {
    const start = firstOfMonth(year, month)
    months.push(
      sourceMonths.get(start) ?? {
        year,
        month,
        start,
        end: lastOfMonth(year, month),
        completed: ZERO_TOTALS,
        planned: ZERO_TOTALS,
        load: 0,
      },
    )
  }

  // Totals.
  const monthMatch = months.find((month) => month.start === from && month.end === to)
  const totals: PeriodTotals = exact
    ? demoReport.totals
    : monthMatch && base
      ? {
          ...sumPeriodTotals([], from, asOf),
          runCount: monthMatch.completed.runCount,
          distanceMeters: monthMatch.completed.distanceMeters,
          durationSeconds: monthMatch.completed.durationSeconds,
          elevationGainMeters: monthMatch.completed.elevationGainMeters,
          averagePaceSecondsPerKm:
            monthMatch.completed.distanceMeters > 0
              ? round1(monthMatch.completed.durationSeconds / (monthMatch.completed.distanceMeters / 1000))
              : null,
          longestRunMeters: monthMatch.completed.runCount > 0 ? demoReport.totals.longestRunMeters : null,
          load: monthMatch.load,
          loadSessionCount: monthMatch.completed.runCount,
          activeDays: monthMatch.completed.runCount,
        }
      : base
        ? sumPeriodTotals(weeks, from, asOf)
        : ZERO_PERIOD_TOTALS

  // Comparison.
  let comparison: InsightsReportDto['comparison'] = null
  const kind = query.compare === 'previousYear' ? 'previousYear' : query.compare === 'none' ? null : 'previous'
  if (kind && elapsedDays > 0) {
    const window = comparisonWindow(from, to, elapsedDays, kind)
    const previous: PeriodTotals =
      exact && kind === 'previous'
        ? demoReport.comparison!.totals
        : base
          ? sumPeriodTotals([...sourceWeeks.values()], window.from, window.to)
          : ZERO_PERIOD_TOTALS
    comparison = {
      kind,
      ...window,
      totals: previous,
      deltas: {
        runCount: delta(totals.runCount, previous.runCount),
        distanceMeters: delta(totals.distanceMeters, previous.distanceMeters),
        durationSeconds: delta(totals.durationSeconds, previous.durationSeconds),
        elevationGainMeters: delta(totals.elevationGainMeters, previous.elevationGainMeters),
        load: delta(totals.load, previous.load),
        activeDays: delta(totals.activeDays, previous.activeDays),
      },
    }
  }

  // Planned.
  const plannedWeeks = weeks.filter((week) => week.planned.runCount > 0)
  const planned = {
    runCount: plannedWeeks.reduce((sum, week) => sum + week.planned.runCount, 0),
    distanceMeters: plannedWeeks.reduce((sum, week) => sum + week.planned.distanceMeters, 0),
    durationSeconds: plannedWeeks.reduce((sum, week) => sum + week.planned.durationSeconds, 0),
    upcomingCount: plannedWeeks
      .filter((week) => week.end >= today)
      .reduce((sum, week) => sum + week.planned.runCount, 0),
    missedCount: plannedWeeks.filter((week) => week.end < today).reduce((sum, week) => sum + week.planned.runCount, 0),
  }

  // Load: the demo's daily series where it overlaps, zero days elsewhere.
  const sourceDaily = new Map((base?.load.daily ?? []).map((day) => [day.date, day]))
  const dailyEnd = to < asOf ? to : asOf
  let daily: DailyLoad[] = []
  for (let date = startOfIsoWeek(from); date <= dailyEnd; date = addDays(date, 1)) {
    daily.push(sourceDaily.get(date) ?? { date, load: 0, acute: 0, chronic: null, acwr: null, band: null })
  }
  let historyStart = base?.load.historyStart ?? null
  if (scenario === 'insufficientHistory') {
    historyStart = addDays(asOf, -12)
    daily = daily.map((day) => ({ ...day, chronic: null, acwr: null, band: null }))
  } else if (scenario === 'noChronicLoad') {
    daily = daily.map((day) => ({ ...day, chronic: 0, acwr: null, band: null }))
  }
  const last = daily.at(-1)
  const inPeriod = daily.filter((day) => day.date >= from)
  const lastPoint = last ?? { date: asOf, load: 0, acute: 0, chronic: null, acwr: null, band: null }
  const reason: S['AcwrUnavailableReasonDto'] =
    lastPoint.acwr !== null ? null : scenario === 'noChronicLoad' ? 'noChronicLoad' : 'insufficientHistory'
  const peakDay = inPeriod.reduce<DailyLoad | null>(
    (top, day) => (day.acwr !== null && (top === null || day.acwr > top.acwr!) ? day : top),
    null,
  )
  const point = (day: DailyLoad, unavailable: S['AcwrUnavailableReasonDto']) => ({
    date: day.date,
    value: day.acwr,
    band: day.band,
    acute: day.acute,
    chronic: day.chronic,
    unavailableReason: unavailable,
  })
  const load: InsightsReportDto['load'] = {
    historyStart,
    atAsOf: point(lastPoint, reason),
    peak: peakDay ? point(peakDay, null) : null,
    bandDays: {
      low: inPeriod.filter((day) => day.band === 'low').length,
      optimal: inPeriod.filter((day) => day.band === 'optimal').length,
      high: inPeriod.filter((day) => day.band === 'high').length,
      unavailable: inPeriod.filter((day) => day.band === null).length,
    },
    daily,
  }

  const paceWeeks = (base?.paceTrend.weeks ?? []).filter(
    (week) => week.start >= startOfIsoWeek(from) && week.start <= to,
  )
  const quality =
    scenario === 'estimated'
      ? {
          completedSessions: totals.loadSessionCount,
          sessionsWithEstimatedRpe: 6,
          estimatedLoadShare: 0.18,
          sessionsWithoutDuration: 1,
          runningSessionsWithoutDistance: 0,
        }
      : (base?.dataQuality ?? {
          completedSessions: 0,
          sessionsWithEstimatedRpe: 0,
          estimatedLoadShare: 0,
          sessionsWithoutDuration: 0,
          runningSessionsWithoutDistance: 0,
        })

  return {
    metricsVersion: 1,
    today,
    period,
    thresholds: demoReport.thresholds,
    totals,
    planned,
    comparison,
    load,
    weeks,
    months,
    typeDistribution: totals.runCount > 0 && base ? base.typeDistribution : [],
    paceTrend: {
      runTypeCodes: ['easy'],
      slopeSecondsPerKmPerWeek: paceWeeks.length >= 4 ? (base?.paceTrend.slopeSecondsPerKmPerWeek ?? null) : null,
      excludedRunCount: 0,
      weeks: paceWeeks,
    },
    races: (base?.races ?? []).filter((race) => race.date >= from && race.date <= to),
    dataQuality: quality,
  }
}

function isoParts(date: string): { isoYear: number; isoWeek: number } {
  const thursday = addDays(date, 3)
  const { year } = monthOf(thursday)
  return { isoYear: year, isoWeek: Math.floor(diffDays(firstOfMonth(year, 1), thursday) / 7) + 1 }
}
