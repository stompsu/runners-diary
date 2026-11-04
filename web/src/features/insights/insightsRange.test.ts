import { describe, expect, it } from 'vitest'
import {
  changeView,
  customRange,
  defaultRange,
  parseInsightsParams,
  presetRange,
  serializeInsightsRange,
  shiftRange,
  spanDays,
  validateCustomRange,
} from './insightsRange'

// Wednesday. Presets come from the API's `me.today` (never the browser clock), so these must hold in any zone
// (`npm run test:tz` runs this file under America/Los_Angeles and Pacific/Kiritimati).
const TODAY = '2026-11-04'
const parse = (query: string, today = TODAY) => parseInsightsParams(new URLSearchParams(query), today)

describe('presets', () => {
  it('last 12 weeks: Monday of this week minus 77 days to this Sunday (the plan example)', () => {
    expect(presetRange('12w', TODAY)).toMatchObject({
      view: '12w',
      from: '2026-08-17',
      to: '2026-11-08',
      compare: 'previous',
    })
  })

  it('this week is the ISO week, this month the calendar month, ytd 1 Jan to today', () => {
    expect(presetRange('week', TODAY)).toMatchObject({ from: '2026-11-02', to: '2026-11-08', compare: 'previous' })
    expect(presetRange('month', TODAY)).toMatchObject({ from: '2026-11-01', to: '2026-11-30', compare: 'previous' })
    expect(presetRange('ytd', TODAY)).toMatchObject({ from: '2026-01-01', to: TODAY, compare: 'previousYear' })
  })

  it('handles ISO weeks that cross a year boundary and the Sunday edge', () => {
    expect(presetRange('week', '2027-01-01')).toMatchObject({ from: '2026-12-28', to: '2027-01-03' })
    expect(presetRange('week', '2026-11-08')).toMatchObject({ from: '2026-11-02', to: '2026-11-08' })
    expect(presetRange('week', '2026-11-09')).toMatchObject({ from: '2026-11-09', to: '2026-11-15' })
  })

  it('uses the given today at the edges of a year (Kiritimati and Los Angeles users see their own date)', () => {
    expect(defaultRange('2026-01-01')).toMatchObject({ from: '2025-10-13', to: '2026-01-04' })
    expect(presetRange('ytd', '2026-12-31')).toMatchObject({ from: '2026-01-01', to: '2026-12-31' })
  })

  it('the default 12-week span is 84 days', () => {
    const range = defaultRange(TODAY)
    expect(spanDays(range.from, range.to)).toBe(84)
  })
})

describe('URL parsing', () => {
  it('no params is the default view, already canonical', () => {
    expect(parse('')).toEqual({ range: defaultRange(TODAY), canonical: true })
  })

  it('reads view, at and compare', () => {
    const { range, canonical } = parse('view=month&at=2026-09-15&compare=none')
    expect(canonical).toBe(true)
    expect(range).toMatchObject({
      view: 'month',
      at: '2026-09-15',
      from: '2026-09-01',
      to: '2026-09-30',
      compare: 'none',
    })
  })

  it('ytd defaults to the previous year, others to the previous period', () => {
    expect(parse('view=ytd').range.compare).toBe('previousYear')
    expect(parse('view=week').range.compare).toBe('previous')
    expect(parse('view=ytd&compare=previous').range.compare).toBe('previous')
  })

  it('reads a custom range', () => {
    expect(parse('view=custom&from=2026-03-01&to=2026-04-15').range).toMatchObject({
      view: 'custom',
      from: '2026-03-01',
      to: '2026-04-15',
    })
  })

  it.each([
    'view=decade',
    'compare=sometimes',
    'view=month&at=2026-02-30',
    'view=month&at=yesterday',
    'view=week&at=1900-01-01',
    'view=12w&at=1950-01-05',
    'view=week&at=1950-01-01',
    'view=week&at=2100-12-31',
    'view=month&at=2101-01-01',
    'view=custom',
    'view=custom&from=2026-03-01',
    'view=custom&from=2026-04-15&to=2026-03-01',
    'view=custom&from=2025-01-01&to=2026-01-02',
    'view=custom&from=nope&to=2026-01-02',
    'from=2026-03-01&to=2026-04-15',
  ])('falls back to the default view for %s and asks for a replace', (query) => {
    const result = parse(query)
    expect(result.range).toEqual(defaultRange(TODAY))
    expect(result.canonical).toBe(false)
  })
})

describe('URL serialising', () => {
  const text = (query: string) => serializeInsightsRange(parse(query).range, TODAY).toString()

  it('omits everything that is a default', () => {
    expect(serializeInsightsRange(defaultRange(TODAY), TODAY).toString()).toBe('')
    expect(text('view=ytd')).toBe('view=ytd')
  })

  it('keeps at for week and month, a non-default comparison and a 12-week anchor', () => {
    expect(serializeInsightsRange(presetRange('month', TODAY), TODAY).toString()).toBe('view=month&at=2026-11-04')
    expect(text('view=week&at=2026-10-05&compare=previousYear')).toBe('view=week&at=2026-10-05&compare=previousYear')
    expect(text('at=2026-06-01')).toBe('at=2026-06-01')
  })

  it('round-trips every view', () => {
    for (const query of [
      '',
      'view=week&at=2026-10-05',
      'view=month&at=2026-02-10&compare=none',
      'view=ytd&compare=previous',
      'view=custom&from=2026-03-01&to=2026-04-15&compare=previousYear',
      'at=2026-06-01&compare=previousYear',
    ]) {
      const { range } = parse(query)
      expect(parseInsightsParams(serializeInsightsRange(range, TODAY), TODAY).range).toEqual(range)
    }
  })
})

describe('earlier / later', () => {
  it('moves by a week, a month, 12 weeks or the custom length', () => {
    expect(shiftRange(presetRange('week', TODAY), -1, TODAY)).toMatchObject({ from: '2026-10-26', to: '2026-11-01' })
    expect(shiftRange(presetRange('month', TODAY), -1, TODAY)).toMatchObject({ from: '2026-10-01', to: '2026-10-31' })
    expect(shiftRange(presetRange('12w', TODAY), -1, TODAY)).toMatchObject({ from: '2026-05-25', to: '2026-08-16' })
    const custom = customRange('2026-03-01', '2026-03-10', TODAY)!
    expect(shiftRange(custom, 1, TODAY)).toMatchObject({ from: '2026-03-11', to: '2026-03-20' })
  })

  it('a month stepped back from 31 Jan or forward from 31 Mar keeps whole calendar months', () => {
    const jan = parse('view=month&at=2026-01-31').range
    expect(shiftRange(jan, 1, TODAY)).toMatchObject({ from: '2026-02-01', to: '2026-02-28' })
    const mar = parse('view=month&at=2026-03-31').range
    expect(shiftRange(mar, -1, TODAY)).toMatchObject({ from: '2026-02-01', to: '2026-02-28' })
  })

  it('crosses year boundaries by ISO week', () => {
    const week = parse('view=week&at=2027-01-01').range
    expect(shiftRange(week, -1, '2027-01-04')).toMatchObject({ from: '2026-12-21', to: '2026-12-27' })
  })

  it('is not offered outside 1950..2100', () => {
    expect(shiftRange(parse('view=week&at=1950-01-03').range, -1, TODAY)).toBeNull()
    expect(shiftRange(parse('view=month&at=1950-01-10').range, -1, TODAY)).toBeNull()
    expect(shiftRange(parse('view=week&at=1950-01-10').range, 1, TODAY)).not.toBeNull()
    expect(shiftRange(parse('view=week&at=2100-12-20').range, 1, '2100-12-30')).toBeNull()
  })

  it('ytd ignores at (always year to date of today) and asks for a replace', () => {
    const result = parse('view=ytd&at=2025-03-01')
    expect(result.range).toMatchObject({ view: 'ytd', at: TODAY, from: '2026-01-01', to: TODAY })
    expect(result.canonical).toBe(false)
    expect(parse('view=ytd').canonical).toBe(true)
  })

  it('is not offered for year to date, and not beyond one future period', () => {
    expect(shiftRange(presetRange('ytd', TODAY), -1, TODAY)).toBeNull()
    const future = parse('view=month&at=2026-12-10').range
    expect(shiftRange(future, 1, TODAY)).toBeNull()
    expect(shiftRange(presetRange('month', TODAY), 1, TODAY)).toMatchObject({ from: '2026-12-01' })
  })

  it('keeps the comparison choice', () => {
    const range = parse('view=week&compare=none').range
    expect(shiftRange(range, -1, TODAY)?.compare).toBe('none')
  })
})

describe('changing the view', () => {
  it('moves to the preset of today and uses the usual comparison unless one was chosen', () => {
    expect(changeView(defaultRange(TODAY), 'ytd', TODAY).compare).toBe('previousYear')
    expect(changeView(parse('compare=none').range, 'month', TODAY).compare).toBe('none')
  })
})

describe('custom range validation', () => {
  it('accepts up to 366 days inclusive', () => {
    expect(validateCustomRange('2026-01-01', '2026-12-31')).toBeNull()
    expect(validateCustomRange('2028-01-01', '2028-12-31')).toBeNull() // leap year: exactly 366 days
    expect(validateCustomRange('2026-01-01', '2027-01-01')).toBeNull() // 366 days
    expect(validateCustomRange('2026-01-01', '2027-01-02')).toBe('tooLong')
  })

  it('reports each problem', () => {
    expect(validateCustomRange('', '2026-01-01')).toBe('invalidFrom')
    expect(validateCustomRange('2026-01-01', '2026-13-01')).toBe('invalidTo')
    expect(validateCustomRange('2026-02-01', '2026-01-01')).toBe('toBeforeFrom')
    expect(validateCustomRange('1949-12-31', '1950-01-02')).toBe('outOfRange')
    expect(validateCustomRange('2100-12-30', '2101-01-02')).toBe('outOfRange')
    expect(customRange('2026-02-01', '2026-01-01', TODAY)).toBeNull()
  })
})
