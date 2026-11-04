import { describe, expect, it } from 'vitest'
import {
  applyPrefill,
  applyRunType,
  applyStatus,
  defaultFormValues,
  emptyRace,
  toFormValues,
  toWriteRequest,
  withAllWriteMembers,
} from './runFormMapping'
import type { RunDto, RunFormValues } from './runFormTypes'

/** A complete DTO: every member the API returns, including the read-only ones. */
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
  tracks: [
    {
      id: 't1',
      kind: 'RecordedTrack',
      status: 'Processed',
      originalFileName: 'run.gpx',
      createdAt: '2026-11-14T12:00:00Z',
    },
  ],
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

  it('an untouched location equals the loaded one, coordinates included, exactly', () => {
    const precise = { ...fullRun, location: { latitude: 50.294862123, longitude: 18.676642987, name: 'Gliwice' } }
    const request = toWriteRequest({ ...toFormValues(precise), title: 'Renamed' }, precise)
    expect(request.location).toEqual({ latitude: 50.294862123, longitude: 18.676642987, name: 'Gliwice' })
    expect(request.title).toBe('Renamed')
  })

  it('sends the edited location: a new pin, a new name, a blank name as null, and a cleared location as null', () => {
    const values = toFormValues(fullRun)
    expect(
      toWriteRequest({ ...values, location: { latitude: 50.1, longitude: 18.2, name: ' Park ' } }, fullRun).location,
    ).toEqual({ latitude: 50.1, longitude: 18.2, name: 'Park' })
    expect(
      toWriteRequest({ ...values, location: { latitude: 50.1, longitude: 18.2, name: '  ' } }, fullRun).location,
    ).toEqual({ latitude: 50.1, longitude: 18.2, name: null })
    expect(toWriteRequest({ ...values, location: null }, fullRun).location).toBeNull()
  })

  it('maps a location without a name to an empty name in the form', () => {
    const values = toFormValues({ ...fullRun, location: { latitude: 1, longitude: 2, name: null } })
    expect(values.location).toEqual({ latitude: 1, longitude: 2, name: '' })
    expect(toFormValues({ ...fullRun, location: null }).location).toBeNull()
  })

  it('every form field feeds a member of the write request (a new field must be wired here)', () => {
    // Exhaustive by type: adding a RunFormValues key without an entry fails to compile. Each entry names the write
    // member(s) it feeds, and the loop proves the request really changes when the field does.
    const feeds: Record<keyof RunFormValues, string> = {
      date: 'date',
      startTime: 'startTime',
      timeZoneId: 'timeZoneId',
      status: 'status',
      runTypeId: 'runTypeId',
      title: 'title',
      notes: 'notes',
      distanceKm: 'distanceMeters',
      duration: 'durationSeconds',
      elevationGain: 'elevationGainMeters',
      elevationLoss: 'elevationLossMeters',
      avgHeartRate: 'avgHeartRate',
      maxHeartRate: 'maxHeartRate',
      rpe: 'rpe',
      gear: 'gear',
      weather: 'weather',
      surface: 'surface',
      location: 'location',
      race: 'race',
      rating: 'rating',
    }
    const base = toFormValues(fullRun)
    const changed: Partial<RunFormValues> = {
      date: '2026-12-01',
      startTime: '09:00',
      timeZoneId: 'Europe/London',
      status: 'Planned',
      runTypeId: 22,
      title: 'x',
      notes: 'x',
      distanceKm: '1',
      duration: '10:00',
      elevationGain: '1',
      elevationLoss: '1',
      avgHeartRate: '100',
      maxHeartRate: '190',
      rpe: '3',
      gear: 'x',
      weather: 'x',
      surface: 'Trail',
      location: { latitude: 1, longitude: 2, name: 'x' },
      race: null,
      rating: { stars: 1, comment: '' },
    }
    const before = toWriteRequest(base, fullRun) as Record<string, unknown>
    for (const [field, member] of Object.entries(feeds)) {
      const after = toWriteRequest({ ...base, [field]: changed[field as keyof RunFormValues] }, fullRun) as Record<
        string,
        unknown
      >
      expect([field, after[member]]).not.toEqual([field, before[member]])
    }
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

describe('applyPrefill', () => {
  const values = defaultFormValues({ date: '2026-11-14', runTypeId: 20 })

  it('puts the proposed values into the form values and leaves the rest alone', () => {
    const next = applyPrefill(values, {
      distanceKm: '10.005',
      duration: '0:49:30',
      location: { latitude: 50.1, longitude: 18.2, name: '' },
    })
    expect(next).toMatchObject({ distanceKm: '10.005', duration: '0:49:30', date: '2026-11-14', title: '' })
    expect(next.location).toEqual({ latitude: 50.1, longitude: 18.2, name: '' })
    expect(values.distanceKm).toBe('')
  })

  it('changes date and start time together, and completes through applyStatus', () => {
    const next = applyPrefill(
      { ...values, rating: { stars: 0, comment: '' } },
      {
        date: '2026-11-15',
        startTime: '08:30',
        status: 'Completed',
      },
    )
    expect(next).toMatchObject({ date: '2026-11-15', startTime: '08:30', status: 'Completed' })
  })

  it('prefilled numbers reach the request in SI at display precision (1 m, 1 s, 0.1 m)', () => {
    const next = applyPrefill(values, { distanceKm: '10.005', duration: '0:49:30', elevationGain: '14.6' })
    expect(toWriteRequest(next)).toMatchObject({
      distanceMeters: 10005,
      durationSeconds: 2970,
      elevationGainMeters: 14.6,
    })
  })
})
