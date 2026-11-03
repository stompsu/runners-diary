import { describe, expect, it } from 'vitest'
import { currentDiaryMonth, diaryPath, parseDiaryMonth } from './diaryMonth'

describe('parseDiaryMonth', () => {
  it.each([
    ['2026', '11', { year: 2026, month: 11 }],
    ['2026', '1', { year: 2026, month: 1 }],
    ['2026', '01', { year: 2026, month: 1 }],
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
  it('uses the local calendar month (1-based)', () => {
    expect(currentDiaryMonth(new Date(2026, 0, 31, 23, 30))).toEqual({ year: 2026, month: 1 })
  })
})
