import { describe, expect, it } from 'vitest'
import {
  applyRunType,
  applyStatus,
  defaultFormValues,
  emptyRace,
  toFormValues,
  toWriteRequest,
  withAllWriteMembers,
} from './runFormMapping'
import type { RunDto } from './runFormTypes'

/** A complete DTO: every member the API returns, including read-only ones and the `location` the form never shows. */
const fullRun: RunDto = {
  id: '00000000-0000-4000-8000-000000000001',
  date: '2026-11-14',
  startTime: '08:30',
  timeZoneId: 'Europe/Warsaw',
  startUtc: '2026-11-14T07:30:00Z',
  status: 'Completed',
  runTypeId: 1,
  title: 'Silesia Half',
  notes: 'Cold and windy',
  location: { latitude: 50.2945, longitude: 18.6714, name: 'Gliwice' },
  distanceMeters: 21097,
  durationSeconds: 5880,
  elevationGainMeters: 90,
  elevationLossMeters: 85.5,
  avgHeartRate: 168,
  maxHeartRate: 181,
  rpe: 8,
  gear: 'Nike Vaporfly',
  weather: '6 C, wind',
  surface: 'Road',
  race: {
    name: 'Silesia Half Marathon',
    distanceCategory: 'HalfMarathon',
    officialDistanceMeters: 21097.5,
    bibNumber: '1234',
    targetTimeSeconds: 6000,
    resultTimeSeconds: 5880,
    overallPlace: 120,
    categoryPlace: 14,
    categoryName: 'M35',
    resultsUrl: 'https://example.org/results',
  },
  rating: { stars: 5, comment: 'PB' },
  version: 'AAAAAAAAB9E=',
  tracks: [{ id: 't1', kind: 'RecordedTrack', status: 'Processed' }],
  createdAt: '2026-11-01T10:00:00Z',
  updatedAt: '2026-11-14T12:00:00Z',
}

/** The members a PUT may carry: the DTO minus read-only ones, with `timeZoneId` omitted (the run keeps its zone). */
function writableSubset(dto: RunDto) {
  const {
    id: _id,
    startUtc: _utc,
    tracks: _tracks,
    createdAt: _created,
    updatedAt: _updated,
    timeZoneId: _zone,
    ...rest
  } = dto
  return rest
}

describe('toFormValues', () => {
  it('shows display units: km, h:mm:ss, metres', () => {
    const values = toFormValues(fullRun)
    expect(values).toMatchObject({
      distanceKm: '21.097',
      duration: '1:38:00',
      elevationGain: '90',
      elevationLoss: '85.5',
      startTime: '08:30',
      timeZoneId: '',
      surface: 'Road',
      rating: { stars: 5, comment: 'PB' },
    })
    expect(values.race).toMatchObject({ officialDistanceKm: '21.098', targetTime: '1:40:00', resultTime: '1:38:00' })
  })

  it('maps null members to empty strings and no race', () => {
    const values = toFormValues({
      ...fullRun,
      startTime: null,
      title: null,
      distanceMeters: null,
      durationSeconds: null,
      race: null,
      rating: null,
      surface: null,
    })
    expect(values).toMatchObject({ startTime: '', title: '', distanceKm: '', duration: '', surface: '', race: null })
    expect(values.rating).toEqual({ stars: 0, comment: '' })
  })
})

describe('toWriteRequest', () => {
  it('round trip: toWriteRequest(toFormValues(dto), dto) equals the writable subset of dto', () => {
    expect(toWriteRequest(toFormValues(fullRun), fullRun)).toEqual(writableSubset(fullRun))
  })

  it('preserves location on edit even though the form never shows it', () => {
    const request = toWriteRequest({ ...toFormValues(fullRun), title: 'Renamed' }, fullRun)
    expect(request.location).toEqual({ latitude: 50.2945, longitude: 18.6714, name: 'Gliwice' })
    expect(request.title).toBe('Renamed')
  })

  it('never sends read-only members', () => {
    const request = toWriteRequest(toFormValues(fullRun), fullRun) as Record<string, unknown>
    for (const key of ['id', 'startUtc', 'tracks', 'createdAt', 'updatedAt']) expect(request).not.toHaveProperty(key)
  })

  it('takes the version from the base (PUT) and sends none on create', () => {
    expect(toWriteRequest(toFormValues(fullRun), { ...fullRun, version: 'NEW' }).version).toBe('NEW')
    const create = toWriteRequest(defaultFormValues({ date: '2026-11-03', runTypeId: 20 }))
    expect(create).not.toHaveProperty('version')
    expect(create.location).toBeNull()
  })

  it('omits timeZoneId unless the user picked one', () => {
    const values = toFormValues(fullRun)
    expect(toWriteRequest(values, fullRun)).not.toHaveProperty('timeZoneId')
    expect(toWriteRequest({ ...values, timeZoneId: 'Europe/London' }, fullRun).timeZoneId).toBe('Europe/London')
    expect(toWriteRequest(defaultFormValues({ date: '2026-11-03', runTypeId: 20 }))).not.toHaveProperty('timeZoneId')
  })

  it('converts typed values to SI (comma or dot, mm:ss or h:mm:ss) and empty to null', () => {
    const request = toWriteRequest({
      ...defaultFormValues({ date: '2026-11-03', runTypeId: 20 }),
      distanceKm: '10,5',
      duration: '52:30',
      elevationGain: '120',
      avgHeartRate: '150',
      rpe: '6',
      startTime: '',
      title: '   ',
    })
    expect(request).toMatchObject({
      distanceMeters: 10500,
      durationSeconds: 3150,
      elevationGainMeters: 120,
      elevationLossMeters: null,
      avgHeartRate: 150,
      maxHeartRate: null,
      rpe: 6,
      startTime: null,
      title: null,
      race: null,
      rating: null,
    })
  })

  it('rounds typed distances to a metre but keeps an untouched, more precise stored value', () => {
    const precise = { ...fullRun, distanceMeters: 10234.567, race: null }
    // Shown as 10.235 km; saving without touching it must not silently change the stored value.
    expect(toWriteRequest(toFormValues(precise), precise).distanceMeters).toBe(10234.567)
    expect(toWriteRequest({ ...toFormValues(precise), distanceKm: '10.2346' }, precise).distanceMeters).toBe(10235)
  })

  it('clears the rating when stars are 0 and keeps a comment-less rating', () => {
    const values = toFormValues({ ...fullRun, race: null })
    expect(toWriteRequest({ ...values, rating: { stars: 0, comment: 'x' } }).rating).toBeNull()
    expect(toWriteRequest({ ...values, rating: { stars: 3, comment: '' } }).rating).toEqual({ stars: 3, comment: null })
  })
})

describe('applyStatus', () => {
  it('moving back to Planned clears the rating and the race result and places', () => {
    const planned = applyStatus(toFormValues(fullRun), 'Planned')
    expect(planned.status).toBe('Planned')
    expect(planned.rating).toEqual({ stars: 0, comment: '' })
    expect(planned.race).toMatchObject({
      resultTime: '',
      overallPlace: '',
      categoryPlace: '',
      name: 'Silesia Half Marathon',
    })
    const request = toWriteRequest(planned, fullRun)
    expect(request.rating).toBeNull()
    expect(request.race?.resultTimeSeconds).toBeNull()
  })

  it('Completed keeps everything', () => {
    const values = toFormValues(fullRun)
    expect(applyStatus(values, 'Completed')).toEqual(values)
  })
})

describe('applyRunType', () => {
  it('creates an empty race section for race types and drops it for others', () => {
    const base = defaultFormValues({ date: '2026-11-03', runTypeId: 20 })
    const race = applyRunType(base, 1, { isRace: true, isRestDay: false })
    expect(race.race).toEqual(emptyRace())
    expect(applyRunType(race, 22, { isRace: false, isRestDay: false }).race).toBeNull()
  })

  it('a rest day carries no metrics, race or rating', () => {
    const values = { ...toFormValues(fullRun), distanceKm: '5', rpe: '3' }
    const rest = applyRunType(values, 41, { isRace: false, isRestDay: true })
    expect(rest).toMatchObject({ distanceKm: '', duration: '', rpe: '', avgHeartRate: '', race: null })
    expect(rest.rating.stars).toBe(0)
  })
})

describe('withAllWriteMembers (PUT must not silently drop API fields)', () => {
  it('rejects, at compile time, a request that leaves out a RunWriteRequest member', () => {
    // If this stops being a type error, a field added to the API would again be dropped by toWriteRequest.
    // @ts-expect-error missing members of RunWriteRequest
    const partial = withAllWriteMembers({ date: '2026-11-03' })
    expect(partial).toEqual({ date: '2026-11-03' })
  })

  it('accepts what toWriteRequest builds, including the conditionally spread members', () => {
    const request = toWriteRequest(defaultFormValues({ date: '2026-11-03', runTypeId: 1 }))
    expect(withAllWriteMembers(request as Required<typeof request>)).toBe(request)
  })
})
