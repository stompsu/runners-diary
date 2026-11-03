import { describe, expect, it } from 'vitest'
import { currentDiaryMonth, diaryPath, parseDiaryMonth, shiftMonth } from './diaryMonth'

describe('parseDiaryMonth', () => {
  it.each([
    ['2026', '11', { year: 2026, month: 11 }],
    ['2026', '1', { year: 2026, month: 1 }],
    ['2026', '01', { year: 2026, month: 1 }],
    ['1950', '1', { year: 1950, month: 1 }],
    ['2100', '12', { year: 2100, month: 12 }],
  ])('parses %s/%s', (year, month, expected) => {
    expect(parseDiaryMonth(year, month)).toEqual(expected)
  })

  it.each([
    ['2026', '13'],
    ['2026', '0'],
    ['abc', '1'],
    ['26', '1'],
    ['2026', '1.5'],
    ['2026', '-1'],
    ['1949', '12'],
    ['2101', '1'],
    [undefined, '1'],
    ['2026', undefined],
  ])('rejects %s/%s', (year, month) => {
    expect(parseDiaryMonth(year, month)).toBeNull()
  })
})

describe('diaryPath', () => {
  it('zero-pads the month', () => {
    expect(diaryPath({ year: 2026, month: 3 })).toBe('/diary/2026/03')
  })
})

describe('currentDiaryMonth', () => {
  it('comes from me.today, whatever the browser zone says', () => {
    expect(currentDiaryMonth({ today: '2026-01-31' })).toEqual({ year: 2026, month: 1 })
    expect(currentDiaryMonth({ today: '2027-12-01' })).toEqual({ year: 2027, month: 12 })
  })
})

describe('shiftMonth', () => {
  it('crosses year boundaries', () => {
    expect(shiftMonth({ year: 2026, month: 12 }, 1)).toEqual({ year: 2027, month: 1 })
    expect(shiftMonth({ year: 2026, month: 1 }, -1)).toEqual({ year: 2025, month: 12 })
  })

  it('returns null outside the supported range', () => {
    expect(shiftMonth({ year: 1950, month: 1 }, -1)).toBeNull()
    expect(shiftMonth({ year: 2100, month: 12 }, 1)).toBeNull()
  })
})
