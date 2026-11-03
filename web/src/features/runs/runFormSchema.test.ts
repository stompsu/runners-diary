import { describe, expect, it } from 'vitest'
import { RUN_TYPES, typeId } from '@/test/fakeApi'
import { defaultFormValues, emptyRace } from './runFormMapping'
import { createRunFormSchema } from './runFormSchema'
import type { RunFormValues } from './runFormTypes'

const schema = createRunFormSchema(RUN_TYPES)
const easy = typeId('easy')
const race = typeId('roadRace')
const rest = typeId('rest')

const base = (patch: Partial<RunFormValues> = {}): RunFormValues => ({
  ...defaultFormValues({ date: '2026-11-03', runTypeId: easy }),
  ...patch,
})

/** field path -> i18n message key, for every issue. */
function issues(values: RunFormValues): Record<string, string> {
  const result = schema.safeParse(values)
  if (result.success) return {}
  return Object.fromEntries(result.error.issues.map((issue) => [issue.path.join('.'), issue.message]))
}

describe('run form schema', () => {
  it('accepts a minimal planned run and a complete completed run', () => {
    expect(issues(base())).toEqual({})
    expect(
      issues(
        base({
          status: 'Completed',
          distanceKm: '10,5',
          duration: '52:30',
          elevationGain: '120',
          avgHeartRate: '150',
          maxHeartRate: '175',
          rpe: '7',
          startTime: '07:30',
          surface: 'Road',
          rating: { stars: 4, comment: 'Good' },
        }),
      ),
    ).toEqual({})
  })

  describe('field rules', () => {
    it.each([
      ['date', { date: '2026-02-30' }, 'validation.dateInvalid'],
      ['date', { date: '1949-12-31' }, 'validation.dateRange'],
      ['date', { date: '2101-01-01' }, 'validation.dateRange'],
      ['startTime', { startTime: '25:00' }, 'validation.startTime'],
      ['timeZoneId', { timeZoneId: 'Central Standard Time' }, 'validation.timeZone'],
      ['distanceKm', { distanceKm: '0' }, 'validation.distance'],
      ['distanceKm', { distanceKm: '1001' }, 'validation.distance'],
      ['distanceKm', { distanceKm: 'abc' }, 'validation.distance'],
      ['duration', { duration: '1:75' }, 'validation.duration'],
      ['duration', { duration: '0:00' }, 'validation.duration'],
      ['duration', { duration: '45' }, 'validation.duration'],
      ['elevationGain', { elevationGain: '-5' }, 'validation.elevation'],
      ['elevationLoss', { elevationLoss: '100001' }, 'validation.elevation'],
      ['avgHeartRate', { avgHeartRate: '29' }, 'validation.heartRate'],
      ['maxHeartRate', { maxHeartRate: '251' }, 'validation.heartRate'],
      ['rpe', { rpe: '0' }, 'validation.rpe'],
      ['rpe', { rpe: '11' }, 'validation.rpe'],
      ['title', { title: 'x'.repeat(121) }, 'validation.titleLength'],
      ['notes', { notes: 'x'.repeat(4001) }, 'validation.notesLength'],
      ['gear', { gear: 'x'.repeat(101) }, 'validation.gearLength'],
      ['weather', { weather: 'x'.repeat(201) }, 'validation.weatherLength'],
    ] as const)('%s: %j -> %s', (field, patch, message) => {
      expect(issues(base(patch))[field]).toBe(message)
    })

    it('rating comment is limited to 500 characters', () => {
      expect(
        issues(base({ status: 'Completed', rating: { stars: 3, comment: 'x'.repeat(501) } }))['rating.comment'],
      ).toBe('validation.commentLength')
    })
  })

  describe('cross-field rules', () => {
    it('a rating needs a completed run', () => {
      expect(issues(base({ rating: { stars: 3, comment: '' } }))['rating.stars']).toBe('validation.ratingCompletedOnly')
      expect(issues(base({ status: 'Completed', rating: { stars: 3, comment: '' } }))).toEqual({})
    })

    it('max heart rate must be at least the average', () => {
      expect(issues(base({ avgHeartRate: '160', maxHeartRate: '150' })).maxHeartRate).toBe('validation.maxBelowAvg')
      expect(issues(base({ avgHeartRate: '150', maxHeartRate: '150' }))).toEqual({})
    })

    it('race details only for race types, and a race type needs a named race', () => {
      expect(issues(base({ race: { ...emptyRace(), name: 'X' } })).runTypeId).toBe('validation.raceTypeOnly')
      expect(issues(base({ runTypeId: race, race: null }))['race.name']).toBe('validation.raceNameRequired')
      expect(issues(base({ runTypeId: race, race: { ...emptyRace(), name: 'X' } }))).toEqual({})
      expect(issues(base({ runTypeId: race, race: { ...emptyRace(), name: '  ' } }))['race.name']).toBe(
        'validation.raceNameRequired',
      )
    })

    it('race result and places only for completed runs', () => {
      const withResult = { ...emptyRace(), name: 'X', resultTime: '1:30:00', overallPlace: '5', categoryPlace: '2' }
      const planned = issues(base({ runTypeId: race, race: withResult }))
      expect(planned['race.resultTime']).toBe('validation.resultCompletedOnly')
      expect(planned['race.overallPlace']).toBe('validation.resultCompletedOnly')
      expect(planned['race.categoryPlace']).toBe('validation.resultCompletedOnly')
      expect(issues(base({ status: 'Completed', runTypeId: race, race: withResult }))).toEqual({})
    })

    it('race fields: times, places, URL scheme and lengths', () => {
      const bad = {
        ...emptyRace(),
        name: 'X',
        targetTime: '1:99:00',
        officialDistanceKm: '-1',
        bibNumber: 'x'.repeat(21),
        categoryName: 'x'.repeat(51),
        resultsUrl: 'javascript:alert(1)',
        overallPlace: '0',
      }
      const result = issues(base({ status: 'Completed', runTypeId: race, race: bad }))
      expect(result['race.targetTime']).toBe('validation.duration')
      expect(result['race.officialDistanceKm']).toBe('validation.distance')
      expect(result['race.bibNumber']).toBe('validation.bibLength')
      expect(result['race.categoryName']).toBe('validation.categoryNameLength')
      expect(result['race.resultsUrl']).toBe('validation.resultsUrl')
      expect(result['race.overallPlace']).toBe('validation.place')
    })

    it('a rest day has no metrics, race or rating', () => {
      expect(issues(base({ runTypeId: rest }))).toEqual({})
      const result = issues(
        base({ runTypeId: rest, status: 'Completed', distanceKm: '5', rpe: '3', rating: { stars: 2, comment: '' } }),
      )
      expect(result.distanceKm).toBe('validation.restDayNoMetrics')
      expect(result.rpe).toBe('validation.restDayNoMetrics')
      expect(result['rating.stars']).toBe('validation.restDayNoRating')
    })

    it('reports cross-field problems together with field problems', () => {
      const result = issues(base({ distanceKm: 'abc', avgHeartRate: '160', maxHeartRate: '150' }))
      expect(result.distanceKm).toBe('validation.distance')
      expect(result.maxHeartRate).toBe('validation.maxBelowAvg')
    })

    it('an unknown run type is rejected', () => {
      expect(issues(base({ runTypeId: 999 })).runTypeId).toBe('validation.runType')
    })
  })
})

describe('validation messages', () => {
  it('every validation key used by the schema has an English text', async () => {
    const { default: runs } = await import('@/i18n/en/runs.json')
    const { default: source } = await import('./runFormSchema.ts?raw')
    const keys = [...source.matchAll(/'validation\.(\w+)'/g)].map((match) => match[1]!)
    expect(keys.length).toBeGreaterThan(20)
    const missing = [...new Set(keys)].filter((key) => !(key in runs.validation))
    expect(missing).toEqual([])
  })
})
