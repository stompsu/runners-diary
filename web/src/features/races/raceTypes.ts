import type { components } from '@/api/generated/schema'

export type RaceKind = components['schemas']['RaceKind']
export type RaceInterestsDto = components['schemas']['RaceInterestsResponse']
export type UpdateRaceInterestsRequest = components['schemas']['UpdateRaceInterestsRequest']
export type RacesClientConfig = components['schemas']['RacesClientConfig']

/** Display order of the kind checkboxes. */
export const RACE_KINDS = [
  'Road',
  'Trail',
  'Mountain',
  'Ultra',
  'CrossCountry',
  'Ocr',
  'Other',
] as const satisfies readonly RaceKind[]
