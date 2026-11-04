import { describe, expect, it } from 'vitest'
import {
  deltaDirection,
  formatDeltaAmount,
  formatDeltaPercent,
  formatLoad,
  formatPaceClock,
  formatPaceOrDash,
  formatRatio,
  formatShare,
  hasDataQualityIssues,
  lastCompleteWeek,
  loggedDays,
  sortRaces,
} from './insightsFormat'

describe('delta formatting', () => {
  it('writes signed absolute values in the unit of the metric', () => {
    expect(formatDeltaAmount('distance', 47_686, 'en')).toBe('+47.7 km')
    expect(formatDeltaAmount('distance', -1_250, 'en')).toBe('-1.3 km')
    expect(formatDeltaAmount('duration', 18_943, 'en')).toBe('+5:15:43')
    expect(formatDeltaAmount('duration', -300, 'en')).toBe('-0:05:00')
    expect(formatDeltaAmount('elevation', 904, 'en')).toBe('+904 m')
    expect(formatDeltaAmount('load', 1_715.5, 'en')).toBe('+1,716 AU')
    expect(formatDeltaAmount('count', 2, 'en')).toBe('+2')
    expect(formatDeltaAmount('pace', -6.2, 'en')).toBe('-6 s/km')
  })

  it('shows a dash instead of a percentage when the previous period was zero', () => {
    expect(formatDeltaPercent(14.2, 'en')).toBe('+14.2%')
    expect(formatDeltaPercent(-3, 'en')).toBe('-3%')
    expect(formatDeltaPercent(null, 'en')).toBe('—')
  })

  it('states the direction, treating a sub-second pace change as no change', () => {
    expect(deltaDirection('distance', { absolute: 5, percent: 1 })).toBe('up')
    expect(deltaDirection('distance', { absolute: -5, percent: -1 })).toBe('down')
    expect(deltaDirection('distance', { absolute: 0, percent: 0 })).toBe('same')
    expect(deltaDirection('pace', { absolute: 0.4, percent: 0.1 })).toBe('same')
    expect(deltaDirection('pace', { absolute: -6, percent: -2 })).toBe('down')
  })
})

describe('values', () => {
  it('formats load, ratios, shares and paces in one place', () => {
    expect(formatLoad(11_676.6, 'en')).toBe('11,677 AU')
    expect(formatRatio(0.8, 'en')).toBe('0.80')
    expect(formatRatio(1.305, 'en')).toBe('1.31')
    expect(formatShare(0.18, 'en')).toBe('18%')
    expect(formatPaceClock(351.3)).toBe('5:51')
    expect(formatPaceOrDash(null)).toBe('—')
    expect(formatPaceOrDash(351.3)).toBe('5:51 /km')
  })

  it('counts logged days inclusively', () => {
    expect(loggedDays(null, '2026-11-04')).toBe(0)
    expect(loggedDays('2026-11-04', '2026-11-04')).toBe(1)
    expect(loggedDays('2026-10-23', '2026-11-04')).toBe(13)
  })
})

describe('report helpers', () => {
  it('finds the last complete week', () => {
    const week = (start: string, complete: boolean) => ({ start, complete }) as never
    expect(lastCompleteWeek([week('a', true), week('b', true), week('c', false)])).toMatchObject({ start: 'b' })
    expect(lastCompleteWeek([week('c', false)])).toBeNull()
    expect(lastCompleteWeek([])).toBeNull()
  })

  it('sorts races newest first without mutating the input', () => {
    const races = [{ date: '2026-09-01' }, { date: '2026-10-01' }, { date: '2026-08-01' }] as never[]
    expect(sortRaces(races).map((race: { date: string }) => race.date)).toEqual([
      '2026-10-01',
      '2026-09-01',
      '2026-08-01',
    ])
    expect((races[0] as unknown as { date: string }).date).toBe('2026-09-01')
  })

  it('flags data quality issues only when something is imprecise', () => {
    const clean = {
      completedSessions: 3,
      sessionsWithEstimatedRpe: 0,
      estimatedLoadShare: 0,
      sessionsWithoutDuration: 0,
      runningSessionsWithoutDistance: 0,
    }
    expect(hasDataQualityIssues(clean)).toBe(false)
    expect(hasDataQualityIssues({ ...clean, estimatedLoadShare: 0.1 })).toBe(true)
    expect(hasDataQualityIssues({ ...clean, sessionsWithoutDuration: 1 })).toBe(true)
    expect(hasDataQualityIssues({ ...clean, runningSessionsWithoutDistance: 1 })).toBe(true)
  })
})
