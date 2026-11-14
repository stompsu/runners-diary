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

export type RaceProposal = components['schemas']['RaceProposalDto']
export type RaceProposalList = components['schemas']['RaceProposalListResponse']
export type RaceDetails = components['schemas']['RaceDetailsDto']
export type ProposalState = components['schemas']['ProposalState']
export type RaceUserState = components['schemas']['RaceUserStateDto']
export type SignUpForRaceRequest = components['schemas']['SignUpForRaceRequest']
/** What the user can set: `SignedUp` is derived from a run and never sent. */
export type SettableProposalState = components['schemas']['SetRaceProposalStateRequest']['state']
