import type { components } from '@/api/generated/schema'

export type RunDto = components['schemas']['RunDto']
export type RunWriteRequest = components['schemas']['RunWriteRequest']
export type RunTypeDto = components['schemas']['RunTypeDto']
export type RunStatus = components['schemas']['RunStatus']
export type RaceDistanceCategory = components['schemas']['RaceDistanceCategory']
export type SurfaceValue = NonNullable<components['schemas']['Surface']>

export const SURFACES: readonly SurfaceValue[] = ['Road', 'Trail', 'Track', 'Treadmill', 'Mixed']
export const RACE_CATEGORIES: readonly RaceDistanceCategory[] = ['FiveK', 'TenK', 'HalfMarathon', 'Marathon', 'Other']

/** Race section of the form; `null` in `RunFormValues.race` means "this run has no race details". */
export interface RaceFormValues {
  name: string
  distanceCategory: RaceDistanceCategory
  /** km, only meaningful for the `Other` category (ultras) but always editable */
  officialDistanceKm: string
  bibNumber: string
  /** h:mm:ss */
  targetTime: string
  resultTime: string
  overallPlace: string
  categoryPlace: string
  categoryName: string
  resultsUrl: string
}

/**
 * The form model (ADR 0018): display units and strings, never the wire shape. Numbers the user types stay strings
 * until `toWriteRequest` converts them to SI; an empty string means "not set".
 */
export interface RunFormValues {
  date: string
  /** `HH:mm` or '' */
  startTime: string
  /** '' = not changed (POST: profile zone; PUT: the run keeps its zone) */
  timeZoneId: string
  status: RunStatus
  runTypeId: number
  title: string
  notes: string
  distanceKm: string
  /** h:mm:ss or mm:ss */
  duration: string
  elevationGain: string
  elevationLoss: string
  avgHeartRate: string
  maxHeartRate: string
  rpe: string
  gear: string
  weather: string
  surface: '' | SurfaceValue
  race: RaceFormValues | null
  /** `stars` 0 = no rating */
  rating: { stars: number; comment: string }
}

/** What the mapping needs to know about the chosen run type. */
export interface RunTypeRules {
  isRace: boolean
  isRestDay: boolean
}

export function rulesOf(type: RunTypeDto | undefined): RunTypeRules {
  return { isRace: type?.category === 'Race', isRestDay: type?.isRestDay === true }
}
