import type { components } from '../../api/generated/schema.d.ts'
import { addDays, firstOfMonth, monthOf } from '../../lib/dates/plainDate.ts'

type RunTypeDto = components['schemas']['RunTypeDto']
type RunWriteRequest = components['schemas']['RunWriteRequest']

/** The fixed 20-type catalogue (docs/data-model.md; ids are contracts). */
export const RUN_TYPES: RunTypeDto[] = [
  ['roadRace', 1, 'Race'],
  ['trailRace', 2, 'Race'],
  ['ultra', 3, 'Race'],
  ['trackRace', 4, 'Race'],
  ['crossCountry', 5, 'Race'],
  ['virtualRace', 6, 'Race'],
  ['easy', 20, 'Training'],
  ['recovery', 21, 'Training'],
  ['long', 22, 'Training'],
  ['tempo', 23, 'Training'],
  ['intervals', 24, 'Training'],
  ['fartlek', 25, 'Training'],
  ['hillRepeats', 26, 'Training'],
  ['progression', 27, 'Training'],
  ['racePace', 28, 'Training'],
  ['stridesDrills', 29, 'Training'],
  ['trailTraining', 30, 'Training'],
  ['treadmill', 31, 'Training'],
  ['crossTraining', 40, 'Other'],
  ['rest', 41, 'Other'],
].map(([code, id, category], index) => ({
  id: id as number,
  code: code as string,
  category: category as RunTypeDto['category'],
  countsTowardRunningDistance: category !== 'Other',
  countsTowardLoad: code !== 'rest',
  isRestDay: code === 'rest',
  sortOrder: (index + 1) * 10,
}))

export const typeId = (code: string): number => {
  const t = RUN_TYPES.find((r) => r.code === code)
  if (!t) throw new Error(`Unknown run type ${code}`)
  return t.id
}

/**
 * A small demo month around `today` (the fake's analogue of the real guest seed): completed easy/long/tempo runs,
 * a rated one, a race, planned runs and a rest day. Dates are relative to the first of today's month.
 */
export function demoRuns(today: string): RunWriteRequest[] {
  const { year, month } = monthOf(today)
  const day = (n: number) => addDays(firstOfMonth(year, month), n - 1)
  return [
    {
      date: day(2),
      status: 'Completed',
      runTypeId: typeId('easy'),
      title: 'Easy morning',
      distanceMeters: 8000,
      durationSeconds: 2640,
      elevationGainMeters: 40,
      startTime: '07:15',
    },
    {
      date: day(4),
      status: 'Completed',
      runTypeId: typeId('tempo'),
      title: 'Tempo 3x2km',
      distanceMeters: 10000,
      durationSeconds: 3000,
      elevationGainMeters: 60,
      rating: { stars: 4, comment: 'Felt strong' },
    },
    {
      date: day(6),
      status: 'Completed',
      runTypeId: typeId('long'),
      title: 'Long run',
      distanceMeters: 21000,
      durationSeconds: 7560,
      elevationGainMeters: 180,
    },
    {
      date: day(7),
      status: 'Completed',
      runTypeId: typeId('recovery'),
      distanceMeters: 5000,
      durationSeconds: 1800,
      elevationGainMeters: 10,
    },
    { date: day(9), status: 'Completed', runTypeId: typeId('crossTraining'), title: 'Bike', durationSeconds: 3600 },
    { date: day(11), status: 'Completed', runTypeId: typeId('rest'), notes: 'Sore calf' },
    {
      date: day(14),
      status: 'Completed',
      runTypeId: typeId('roadRace'),
      title: 'Silesia Half',
      distanceMeters: 21097,
      durationSeconds: 5880,
      elevationGainMeters: 90,
      race: {
        name: 'Silesia Half Marathon',
        distanceCategory: 'HalfMarathon',
        resultTimeSeconds: 5880,
        targetTimeSeconds: 6000,
        overallPlace: 120,
      },
      rating: { stars: 5, comment: 'PB' },
    },
    { date: day(18), status: 'Planned', runTypeId: typeId('easy'), distanceMeters: 8000 },
    {
      date: day(21),
      status: 'Planned',
      runTypeId: typeId('intervals'),
      title: '6x800 m',
      distanceMeters: 9000,
      startTime: '18:00',
    },
    { date: day(25), status: 'Planned', runTypeId: typeId('long'), distanceMeters: 24000 },
  ]
}
