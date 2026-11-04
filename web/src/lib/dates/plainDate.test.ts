import { describe, expect, it } from 'vitest'
import {
  addDays,
  addMonths,
  compare,
  dayLabel,
  daysInMonth,
  diffDays,
  format,
  isLeapYear,
  isoWeek,
  isoWeekday,
  isValid,
  lastOfMonth,
  monthLabel,
  monthName,
  monthOf,
  parse,
  rangeLabel,
  shortDayLabel,
  startOfIsoWeek,
  todayFromMe,
  weekdayLabels,
} from './plainDate'

describe('parse/format', () => {
  it('round-trips valid dates', () => {
    for (const d of ['2026-11-14', '2024-02-29', '1950-01-01', '2100-12-31', '0999-05-06']) {
      expect(format(parse(d)!)).toBe(d)
    }
  })

  it.each([
    '2026-02-30',
    '2025-02-29',
    '2026-13-01',
    '2026-00-10',
    '2026-11-31',
    '26-11-14',
    '2026-1-1',
    '',
    'x',
    '2026-11-14T00:00',
  ])('rejects %j', (value) => {
    expect(parse(value)).toBeNull()
    expect(isValid(value)).toBe(false)
  })

  it('throws on arithmetic with an invalid date', () => {
    expect(() => addDays('nope', 1)).toThrow(RangeError)
  })
})

describe('calendar arithmetic', () => {
  it('knows leap years and month lengths', () => {
    expect([2024, 2000, 2100, 1900].map(isLeapYear)).toEqual([true, true, false, false])
    expect(daysInMonth(2024, 2)).toBe(29)
    expect(daysInMonth(2026, 2)).toBe(28)
    expect(daysInMonth(2026, 4)).toBe(30)
    expect(daysInMonth(2026, 12)).toBe(31)
    expect(lastOfMonth(2026, 11)).toBe('2026-11-30')
  })

  it('adds days across month, year and leap boundaries', () => {
    expect(addDays('2026-11-30', 1)).toBe('2026-12-01')
    expect(addDays('2026-12-31', 1)).toBe('2027-01-01')
    expect(addDays('2024-02-28', 1)).toBe('2024-02-29')
    expect(addDays('2026-03-01', -1)).toBe('2026-02-28')
    expect(addDays('2026-11-14', 0)).toBe('2026-11-14')
    expect(addDays('2026-11-14', -365)).toBe('2025-11-14')
  })

  it('does not skip or repeat days around DST changes', () => {
    // US DST: 2026-03-08 and 2026-11-01; EU: 2026-03-29 and 2026-10-25.
    expect(addDays('2026-03-07', 1)).toBe('2026-03-08')
    expect(addDays('2026-03-08', 1)).toBe('2026-03-09')
    expect(addDays('2026-10-31', 1)).toBe('2026-11-01')
    expect(addDays('2026-11-01', 1)).toBe('2026-11-02')
    expect(addDays('2026-10-24', 2)).toBe('2026-10-26')
  })

  it('clamps addMonths to the target month length', () => {
    expect(addMonths('2026-01-31', 1)).toBe('2026-02-28')
    expect(addMonths('2024-01-31', 1)).toBe('2024-02-29')
    expect(addMonths('2026-03-31', -1)).toBe('2026-02-28')
    expect(addMonths('2026-11-14', 1)).toBe('2026-12-14')
    expect(addMonths('2026-12-15', 1)).toBe('2027-01-15')
    expect(addMonths('2026-01-15', -1)).toBe('2025-12-15')
    expect(addMonths('2026-11-14', -23)).toBe('2024-12-14')
    expect(addMonths('2026-11-14', 12)).toBe('2027-11-14')
  })

  it('computes ISO weekdays (Monday = 1)', () => {
    expect(isoWeekday('2026-11-02')).toBe(1) // Monday
    expect(isoWeekday('2026-11-08')).toBe(7) // Sunday
    expect(isoWeekday('2024-02-29')).toBe(4) // Thursday
    expect(isoWeekday('2000-01-01')).toBe(6) // Saturday
    expect(isoWeekday('1950-01-01')).toBe(7) // Sunday
  })

  it('finds the Monday of the ISO week', () => {
    expect(startOfIsoWeek('2026-11-01')).toBe('2026-10-26')
    expect(startOfIsoWeek('2026-11-02')).toBe('2026-11-02')
    expect(startOfIsoWeek('2026-11-08')).toBe('2026-11-02')
  })

  it('computes ISO week numbers and week-based years', () => {
    expect(isoWeek('2026-11-14')).toEqual({ isoYear: 2026, isoWeek: 46 })
    expect(isoWeek('2026-01-01')).toEqual({ isoYear: 2026, isoWeek: 1 })
    expect(isoWeek('2024-12-30')).toEqual({ isoYear: 2025, isoWeek: 1 })
    expect(isoWeek('2021-01-03')).toEqual({ isoYear: 2020, isoWeek: 53 })
    expect(isoWeek('2026-12-31')).toEqual({ isoYear: 2026, isoWeek: 53 })
    expect(isoWeek('2027-01-01')).toEqual({ isoYear: 2026, isoWeek: 53 })
  })

  it('diffs and compares', () => {
    expect(diffDays('2026-11-01', '2026-11-14')).toBe(13)
    expect(diffDays('2026-11-14', '2026-11-01')).toBe(-13)
    expect(diffDays('2026-03-07', '2026-03-09')).toBe(2)
    expect(compare('2026-11-01', '2026-11-02')).toBe(-1)
    expect(compare('2026-11-02', '2026-11-02')).toBe(0)
    expect(compare('2027-01-01', '2026-12-31')).toBe(1)
    expect(monthOf('2026-11-14')).toEqual({ year: 2026, month: 11 })
  })

  it('takes today from the API, validated', () => {
    expect(todayFromMe({ today: '2026-11-03' })).toBe('2026-11-03')
    expect(() => todayFromMe({ today: 'soon' })).toThrow(RangeError)
  })
})

describe('labels (never shift with the browser time zone)', () => {
  it('formats month and day labels', () => {
    expect(monthLabel(2026, 11, 'en')).toBe('November 2026')
    expect(dayLabel('2026-11-14', 'en')).toBe('Saturday, November 14, 2026')
    expect(shortDayLabel('2026-11-14', 'en')).toBe('Nov 14')
    expect(shortDayLabel('2026-01-01', 'en')).toBe('Jan 1')
    expect(dayLabel('2026-12-31', 'en')).toBe('Thursday, December 31, 2026')
  })

  it('lists weekdays Monday-first', () => {
    expect(weekdayLabels('en')).toEqual(['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'])
    expect(weekdayLabels('en', 'long')[0]).toBe('Monday')
  })

  it('is locale-aware', () => {
    expect(monthLabel(2026, 11, 'pl')).toMatch(/listopad/i)
  })
})

describe('monthName', () => {
  it('names months in long and short form without zone drift', () => {
    expect(monthName(1, 'en')).toBe('January')
    expect(monthName(12, 'en', 'short')).toBe('Dec')
  })
})

describe('rangeLabel', () => {
  // ICU separates a range with thin spaces around the en dash; compare with plain spaces.
  const label = (from: string, to: string) => rangeLabel(from, to, 'en').replace(/\s/g, ' ')

  it('collapses shared parts and never shifts with the browser zone', () => {
    expect(label('2026-11-01', '2026-11-04')).toBe('Nov 1 – 4, 2026')
    expect(label('2026-08-17', '2026-11-08')).toBe('Aug 17 – Nov 8, 2026')
    expect(label('2026-12-28', '2027-01-03')).toBe('Dec 28, 2026 – Jan 3, 2027')
  })

  it('shows a single date when the range is one day', () => {
    expect(label('2026-11-04', '2026-11-04')).toBe('Nov 4, 2026')
  })
})
