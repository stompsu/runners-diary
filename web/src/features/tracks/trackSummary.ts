/**
 * What the track view shows in text: the `summary` member of `TrackDto` (plan §3.2, null unless processed) plus its
 * `warnings`. Structurally compatible with the generated type that arrives with the track endpoints, so step 5 passes
 * the DTO fields straight in.
 */
export type TrackWarning = 'timestampsIgnored' | 'noElevation' | 'multipleSegments' | 'routeConverted'

export interface TrackSummary {
  pointCount: number
  distanceMeters: number
  elevationGainMeters: number | null
  elevationLossMeters: number | null
  minElevationMeters: number | null
  maxElevationMeters: number | null
  elapsedSeconds: number | null
  movingSeconds: number | null
}
