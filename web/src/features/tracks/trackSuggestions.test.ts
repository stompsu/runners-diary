import { describe, expect, it } from 'vitest'
import type { RunDto } from '@/features/runs/runFormTypes'
import { selectedPatch, trackSuggestions, type Suggestion } from './trackSuggestions'
import type { TrackDto, TrackSummaryDto } from './trackTypes'

const summary: TrackSummaryDto = {
  pointCount: 893,
  distanceMeters: 10_004.8,
  elevationGainMeters: 15,
  elevationLossMeters: 14.6,
  minElevationMeters: 213.6,
  maxElevationMeters: 231.4,
  elapsedSeconds: 2970,
  movingSeconds: 2905,
  startUtc: '2026-11-14T07:30:00Z',
  endUtc: '2026-11-14T08:19:30Z',
  start: { latitude: 50.294862, longitude: 18.676642 },
  finish: { latitude: 50.294858, longitude: 18.676642 },
  startLocal: { date: '2026-11-14', time: '08:30', timeZoneId: 'Europe/Warsaw' },
}

const track = (overrides: Partial<TrackDto> = {}, over: Partial<TrackSummaryDto> = {}): TrackDto => ({
  id: 't1',
  runId: 'r1',
  kind: 'RecordedTrack',
  status: 'Processed',
  originalFileName: 'run.gpx',
  sizeBytes: 1000,
  createdAt: '2026-11-14T12:00:00Z',
  summary: { ...summary, ...over },
  warnings: [],
  ...overrides,
})

const emptyRun: RunDto = {
  id: 'r1',
  date: '2026-11-14',
  startTime: null,
  timeZoneId: 'Europe/Warsaw',
  startUtc: null,
  catalogRaceId: null,
  status: 'Completed',
  runTypeId: 20,
  title: null,
  notes: null,
  location: null,
  distanceMeters: null,
  durationSeconds: null,
  elevationGainMeters: null,
  elevationLossMeters: null,
  avgHeartRate: null,
  maxHeartRate: null,
  rpe: null,
  gear: null,
  weather: null,
  surface: null,
  race: null,
  rating: null,
  version: 'v',
  tracks: [],
  createdAt: '2026-11-01T00:00:00Z',
  updatedAt: '2026-11-01T00:00:00Z',
}
const rules = { isRace: false, isRestDay: false }
const byId = (rows: Suggestion[]) => Object.fromEntries(rows.map((row) => [row.id, row]))

describe('trackSuggestions', () => {
  it('a recorded track on an empty completed run proposes everything and pre-checks it', () => {
    const rows = byId(trackSuggestions(emptyRun, track(), rules))
    expect(Object.keys(rows)).toEqual([
      'startTime',
      'distance',
      'duration',
      'elevationGain',
      'elevationLoss',
      'location',
    ])
    for (const row of Object.values(rows)) expect([row.id, row.checked]).toEqual([row.id, true])
    expect(rows.distance).toMatchObject({ current: null, proposed: '10.005 km', patch: { distanceKm: '10.005' } })
    expect(rows.duration).toMatchObject({ proposed: '0:49:30', patch: { duration: '0:49:30' } })
    expect(rows.elevationGain!.patch).toEqual({ elevationGain: '15' })
    expect(rows.elevationLoss!.patch).toEqual({ elevationLoss: '14.6' })
    expect(rows.startTime!.patch).toEqual({ date: '2026-11-14', startTime: '08:30' })
    expect(rows.startTime!.dateMismatch).toBeUndefined()
    expect(rows.location!.patch).toEqual({
      location: { latitude: 50.294862, longitude: 18.676642, name: '' },
    })
  })

  it('duration defaults to the elapsed time and offers the moving time as the alternative', () => {
    const [row] = trackSuggestions(emptyRun, track(), rules).filter((r) => r.id === 'duration')
    expect(row!.durationOptions?.map((o) => [o.key, o.text])).toEqual([
      ['elapsed', '0:49:30'],
      ['moving', '0:48:25'],
    ])
    const patch = selectedPatch([row!], new Set(['duration']))
    expect(patch).toEqual({ duration: '0:49:30' })
    expect(selectedPatch([row!], new Set(['duration']), 'moving')).toEqual({ duration: '0:48:25' })
  })

  it('has no choice when moving and elapsed time coincide', () => {
    const [row] = trackSuggestions(emptyRun, track({}, { movingSeconds: 2970 }), rules).filter(
      (r) => r.id === 'duration',
    )
    expect(row!.durationOptions).toBeUndefined()
  })

  it('differing values are listed but not pre-checked', () => {
    const run = { ...emptyRun, distanceMeters: 9500, durationSeconds: 3000, elevationGainMeters: 40 }
    const rows = byId(trackSuggestions(run, track(), rules))
    expect(rows.distance).toMatchObject({ checked: false, current: '9.5 km' })
    expect(rows.duration).toMatchObject({ checked: false, current: '0:50:00' })
    expect(rows.elevationGain).toMatchObject({ checked: false, current: '40 m' })
    // Fields that are still empty stay pre-checked next to them.
    expect(rows.elevationLoss!.checked).toBe(true)
  })

  it('omits rows whose values are already equal (compared as displayed)', () => {
    const run = {
      ...emptyRun,
      startTime: '08:30',
      distanceMeters: 10_005,
      durationSeconds: 2970,
      elevationGainMeters: 15,
      elevationLossMeters: 14.6,
      location: { latitude: 50.294862, longitude: 18.676642, name: 'Gliwice' },
    }
    expect(trackSuggestions(run, track(), rules)).toEqual([])
  })

  it('a different date is flagged and unchecked, and applies date and time together', () => {
    const run = { ...emptyRun, date: '2026-11-03' }
    const row = byId(trackSuggestions(run, track(), rules)).startTime!
    expect(row.checked).toBe(false)
    expect(row.dateMismatch).toEqual({ trackDate: '2026-11-14', runDate: '2026-11-03' })
    expect(row.patch).toEqual({ date: '2026-11-14', startTime: '08:30' })
  })

  it('an existing start time on the same day is not overwritten by default', () => {
    const row = byId(trackSuggestions({ ...emptyRun, startTime: '08:00' }, track(), rules)).startTime!
    expect(row).toMatchObject({ checked: false, current: '08:00' })
    expect(row.dateMismatch).toBeUndefined()
  })

  it('a planned course proposes distance, elevation and location only', () => {
    const rows = trackSuggestions(emptyRun, track({ kind: 'PlannedCourse' }), rules)
    expect(rows.map((row) => row.id)).toEqual(['distance', 'elevationGain', 'elevationLoss', 'location'])
  })

  it('a recorded track on a Planned run adds a pre-checked Mark as completed', () => {
    const rows = trackSuggestions({ ...emptyRun, status: 'Planned' }, track(), rules)
    expect(rows[0]).toMatchObject({ id: 'complete', checked: true, patch: { status: 'Completed' } })
    // A planned course does not complete the run.
    expect(
      trackSuggestions({ ...emptyRun, status: 'Planned' }, track({ kind: 'PlannedCourse' }), rules).map((r) => r.id),
    ).not.toContain('complete')
  })

  it('an untimed track without elevation proposes only distance and location', () => {
    const bare = track(
      {},
      {
        elapsedSeconds: null,
        movingSeconds: null,
        startUtc: null,
        endUtc: null,
        startLocal: null,
        elevationGainMeters: null,
        elevationLossMeters: null,
        minElevationMeters: null,
        maxElevationMeters: null,
      },
    )
    expect(trackSuggestions(emptyRun, bare, rules).map((row) => row.id)).toEqual(['distance', 'location'])
  })

  it('keeps an existing place name and only moves the coordinates', () => {
    const run = { ...emptyRun, location: { latitude: 50.3, longitude: 18.7, name: 'Park' } }
    const row = byId(trackSuggestions(run, track(), rules)).location!
    expect(row.checked).toBe(false)
    expect(row.current).toBe('Park (50.30000, 18.70000)')
    expect(row.patch).toEqual({ location: { latitude: 50.294862, longitude: 18.676642, name: 'Park' } })
  })

  it('has nothing for rest days and for tracks without a summary', () => {
    expect(trackSuggestions(emptyRun, track(), { isRace: false, isRestDay: true })).toEqual([])
    expect(trackSuggestions(emptyRun, track({ status: 'Pending', summary: null }), rules)).toEqual([])
  })

  it('selectedPatch merges only the selected rows', () => {
    const rows = trackSuggestions(emptyRun, track(), rules)
    expect(selectedPatch(rows, new Set(['distance', 'location']))).toEqual({
      distanceKm: '10.005',
      location: { latitude: 50.294862, longitude: 18.676642, name: '' },
    })
    expect(selectedPatch(rows, new Set())).toEqual({})
  })
})
