import { describe, expect, it } from 'vitest'
import {
  defaultGoalValues,
  goalSchema,
  parseGoalNumbers,
  presetOf,
  PRESETS,
  toGoalFormValues,
  toGoalRequest,
  type GoalFormValues,
} from './goalSchema'
import type { GoalDto } from './goalTypes'

const TODAY = '2026-11-03'
const valid = (over: Partial<GoalFormValues> = {}): GoalFormValues => ({
  ...defaultGoalValues(TODAY),
  targetTime: '0:55:00',
  ...over,
})
const issues = (values: GoalFormValues, previous?: string) => {
  const result = goalSchema(TODAY, previous).safeParse(values)
  return result.success ? {} : Object.fromEntries(result.error.issues.map((i) => [String(i.path[0]), i.message]))
}

describe('goalSchema (mirror of the server rules)', () => {
  it('accepts the default preset with a target time', () => {
    expect(issues(valid())).toEqual({})
  })

  it('checks the date window: today+7 .. today+730, an unchanged date of an edit is exempt', () => {
    expect(issues(valid({ targetDate: '2026-11-09' })).targetDate).toBe('dateTooSoon')
    expect(issues(valid({ targetDate: '2026-11-10' })).targetDate).toBeUndefined()
    expect(issues(valid({ targetDate: '2028-11-02' })).targetDate).toBeUndefined()
    expect(issues(valid({ targetDate: '2028-11-03' })).targetDate).toBe('dateTooFar')
    expect(issues(valid({ targetDate: '2026-11-05' }), '2026-11-05').targetDate).toBeUndefined()
    expect(issues(valid({ targetDate: '' })).targetDate).toBe('dateRequired')
  })

  it('checks distance 1-250 km, elevation 0-10,000 m and the title', () => {
    expect(issues(valid({ distanceKm: '0.9' })).distanceKm).toBe('distance')
    expect(issues(valid({ distanceKm: '250.1' })).distanceKm).toBe('distance')
    expect(issues(valid({ distanceKm: '250', targetTime: '', type: 'Distance' })).distanceKm).toBeUndefined()
    expect(issues(valid({ elevationGain: '10001' })).elevationGain).toBe('elevation')
    expect(issues(valid({ elevationGain: '-1' })).elevationGain).toBe('elevation')
    expect(issues(valid({ title: ' ' })).title).toBe('titleRequired')
    expect(issues(valid({ title: 'x'.repeat(121) })).title).toBe('titleTooLong')
  })

  it('applies the type-specific rules', () => {
    expect(issues(valid({ type: 'Performance', targetTime: '' })).targetTime).toBe('targetTimeRequired')
    expect(issues(valid({ type: 'Race', targetTime: '', eventName: '' })).eventName).toBe('eventRequired')
    expect(issues(valid({ type: 'Race', targetTime: '', eventName: 'Gliwice 10K' }))).toEqual({})
    // A Distance goal never validates (or sends) a target time.
    expect(issues(valid({ type: 'Distance', targetTime: 'junk' }))).toEqual({})
    expect(toGoalRequest(valid({ type: 'Distance' })).targetDurationSeconds).toBeNull()
  })

  it('bounds the implied pace to 2:30-15:00 /km', () => {
    expect(
      issues(valid({ targetTime: '0:20:00' })).targetPace ?? issues(valid({ targetTime: '0:20:00' })).targetTime,
    ).toBe('targetPace')
    expect(issues(valid({ targetTime: '0:25:00' })).targetTime).toBeUndefined()
    expect(issues(valid({ targetTime: '2:30:01' })).targetTime).toBe('targetPace')
    expect(issues(valid({ targetTime: 'abc' })).targetTime).toBe('targetTime')
  })
})

describe('mapping', () => {
  const goal: GoalDto = {
    id: 'g',
    type: 'Performance',
    title: 'Trail',
    targetDate: '2027-01-10',
    distanceMeters: 25_000,
    elevationGainMeters: 800,
    targetDurationSeconds: 9000,
    surface: 'Trail',
    eventName: null,
    location: null,
    raceRunId: null,
    notes: null,
    status: 'Active',
    term: 'LongTerm',
    raceRunDate: null,
    plan: null,
    version: 'v1',
    createdAt: '',
    updatedAt: '',
  }

  it('turns display units into SI and back', () => {
    const values = toGoalFormValues(goal)
    expect(values).toMatchObject({ preset: 'trail25', distanceKm: '25', elevationGain: '800', targetTime: '2:30:00' })
    expect(toGoalRequest(values, 'v1')).toMatchObject({
      distanceMeters: 25_000,
      elevationGainMeters: 800,
      targetDurationSeconds: 9000,
      surface: 'Trail',
      version: 'v1',
      status: 'Active',
    })
    expect(parseGoalNumbers({ distanceKm: '10,5', elevationGain: '', targetTime: '' })).toEqual({
      distanceMeters: 10_500,
      elevationGainMeters: null,
      targetDurationSeconds: null,
    })
  })

  it('knows the presets and falls back to custom', () => {
    expect(PRESETS.half.distanceMeters).toBe(21_098)
    expect(presetOf(10_000, null, null)).toBe('tenK')
    expect(presetOf(10_000, 50, null)).toBe('custom')
  })
})
