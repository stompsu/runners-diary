import { describe, expect, it } from 'vitest'
import { raceInterestsSchema, type RaceInterestsFormValues } from './raceInterestsSchema'

const valid: RaceInterestsFormValues = {
  kinds: ['Road'],
  regionCodes: [],
  center: null,
  radiusKm: '',
  minDistanceKm: '',
  maxDistanceKm: '',
  horizonDays: 120,
  keywords: '',
  showInCalendar: true,
}

/** Messages of every issue, keyed by dotted path. */
function issues(overrides: Partial<RaceInterestsFormValues>): Record<string, string> {
  const result = raceInterestsSchema.safeParse({ ...valid, ...overrides })
  if (result.success) return {}
  return Object.fromEntries(result.error.issues.map((issue) => [issue.path.join('.'), issue.message]))
}

const place = { latitude: 50.29, longitude: 18.67, name: 'Gliwice' }

describe('raceInterestsSchema (mirrors ADR 0037 §1)', () => {
  it('accepts the defaults and an empty profile with only a kind', () => {
    expect(raceInterestsSchema.safeParse(valid).success).toBe(true)
  })

  it('needs at least one kind, and only known ones', () => {
    expect(issues({ kinds: [] })).toEqual({ kinds: 'validation.kindsRequired' })
    expect(issues({ kinds: ['Marathon' as 'Road'] })).toHaveProperty('kinds.0')
    expect(
      raceInterestsSchema.safeParse({
        ...valid,
        kinds: ['Road', 'Trail', 'Mountain', 'Ultra', 'CrossCountry', 'Ocr', 'Other'],
      }).success,
    ).toBe(true)
  })

  it('accepts all 16 known region codes and rejects unknown or duplicate ones', () => {
    const all = Array.from({ length: 16 }, (_, i) => `PL-${String(2 + 2 * i).padStart(2, '0')}`)
    expect(raceInterestsSchema.safeParse({ ...valid, regionCodes: all }).success).toBe(true)
    expect(issues({ regionCodes: ['PL-03' as 'PL-02'] })).toHaveProperty(['regionCodes.0'])
    expect(issues({ regionCodes: ['PL-24', 'PL-24'] })).toEqual({ regionCodes: 'validation.regionUnknown' })
    expect(issues({ regionCodes: [...all, 'PL-24'] as 'PL-02'[] })).toHaveProperty('regionCodes')
  })

  it('takes a radius only with a place: required with one, 5 to 300 km', () => {
    expect(issues({ center: place, radiusKm: '' })).toEqual({ radiusKm: 'validation.radiusRequired' })
    expect(issues({ center: place, radiusKm: '4.9' })).toEqual({ radiusKm: 'validation.radiusRange' })
    expect(issues({ center: place, radiusKm: '300.1' })).toEqual({ radiusKm: 'validation.radiusRange' })
    expect(issues({ center: place, radiusKm: 'far' })).toEqual({ radiusKm: 'validation.radiusRange' })
    expect(issues({ center: place, radiusKm: '0' })).toEqual({ radiusKm: 'validation.radiusRange' })
    expect(issues({ center: place, radiusKm: '5' })).toEqual({})
    expect(issues({ center: place, radiusKm: '300' })).toEqual({})
    expect(issues({ center: place, radiusKm: '12,5' })).toEqual({})
    // Without a place the radius is ignored, even when it is nonsense.
    expect(issues({ center: null, radiusKm: 'far' })).toEqual({})
  })

  it('checks the place: latitude, longitude, name length', () => {
    expect(issues({ center: { ...place, latitude: 90.1 }, radiusKm: '50' })).toHaveProperty(['center.latitude'])
    expect(issues({ center: { ...place, longitude: -180.1 }, radiusKm: '50' })).toHaveProperty(['center.longitude'])
    expect(issues({ center: { ...place, name: 'x'.repeat(200) }, radiusKm: '50' })).toEqual({})
    expect(issues({ center: { ...place, name: 'x'.repeat(201) }, radiusKm: '50' })).toEqual({
      'center.name': 'validation.centerNameLength',
    })
  })

  it('takes distances of 1 to 250 km, each optional', () => {
    expect(issues({ minDistanceKm: '1', maxDistanceKm: '250' })).toEqual({})
    expect(issues({ minDistanceKm: '0.9' })).toEqual({ minDistanceKm: 'validation.distanceRange' })
    expect(issues({ maxDistanceKm: '250.1' })).toEqual({ maxDistanceKm: 'validation.distanceRange' })
    expect(issues({ minDistanceKm: 'ten' })).toEqual({ minDistanceKm: 'validation.distanceRange' })
    expect(issues({ minDistanceKm: '21,1' })).toEqual({})
    expect(issues({ minDistanceKm: '10' })).toEqual({})
    expect(issues({ maxDistanceKm: '10' })).toEqual({})
  })

  it('needs min <= max when both are set (equal is fine)', () => {
    expect(issues({ minDistanceKm: '21', maxDistanceKm: '10' })).toEqual({ maxDistanceKm: 'validation.distanceOrder' })
    expect(issues({ minDistanceKm: '10', maxDistanceKm: '10' })).toEqual({})
    // An out of range value reports only its own message.
    expect(issues({ minDistanceKm: '300', maxDistanceKm: '10' })).toEqual({ minDistanceKm: 'validation.distanceRange' })
  })

  it('takes a horizon of 7 to 365 days', () => {
    expect(issues({ horizonDays: 7 })).toEqual({})
    expect(issues({ horizonDays: 365 })).toEqual({})
    expect(issues({ horizonDays: 6 })).toEqual({ horizonDays: 'validation.horizonRange' })
    expect(issues({ horizonDays: 366 })).toEqual({ horizonDays: 'validation.horizonRange' })
    expect(issues({ horizonDays: 30.5 })).toHaveProperty('horizonDays')
  })

  it('takes up to 5 keywords of 2 to 30 characters, 100 characters in all', () => {
    expect(issues({ keywords: 'night gliwice' })).toEqual({})
    expect(issues({ keywords: '  night   gliwice  ' })).toEqual({})
    expect(issues({ keywords: 'a b c d e'.replace(/ /g, 'x ') })).toEqual({ keywords: 'validation.keywordsTermLength' })
    expect(issues({ keywords: 'aa bb cc dd ee' })).toEqual({})
    expect(issues({ keywords: 'aa bb cc dd ee ff' })).toEqual({ keywords: 'validation.keywordsTerms' })
    expect(issues({ keywords: 'a' })).toEqual({ keywords: 'validation.keywordsTermLength' })
    expect(issues({ keywords: 'x'.repeat(30) })).toEqual({})
    expect(issues({ keywords: 'x'.repeat(31) })).toEqual({ keywords: 'validation.keywordsTermLength' })
    // 5 terms of 20 characters + 4 spaces = 104 characters.
    expect(issues({ keywords: Array(5).fill('x'.repeat(20)).join(' ') })).toEqual({
      keywords: 'validation.keywordsLength',
    })
    // 5 x 19 + 4 = 99, and 5 x 19 + 5 = 100 when one term is 20: both fit.
    expect(issues({ keywords: Array(5).fill('x'.repeat(19)).join(' ') })).toEqual({})
    expect(issues({ keywords: [...Array(4).fill('x'.repeat(19)), 'x'.repeat(20)].join(' ') })).toEqual({})
  })
})
