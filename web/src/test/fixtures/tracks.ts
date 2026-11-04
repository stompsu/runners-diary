import type { TrackGeoJson } from '@/features/tracks/trackGeoJson'
import type { TrackSummary } from '@/features/tracks/trackSummary'
import gliwice10k from './gliwice-10k.track.json'

/**
 * A real processed track (ADR 0021 shape): `seed/demo/gpx/gliwice-10k.gpx` simplified (DP 3 m, 50 vertices), profile
 * resampled to 601 samples, 11 splits (the last one is a 4.8 m partial). Derived once by a throwaway script; not
 * regenerated automatically. Shared by Vitest and the Playwright harness.
 */
export const gliwice10kGeoJson = gliwice10k as unknown as TrackGeoJson

/** Matches the README values of the demo file (distance 10 004.8 m, +15 m/-15 m, 49:30). */
export const gliwice10kSummary: TrackSummary = {
  pointCount: 893,
  distanceMeters: 10_004.8,
  elevationGainMeters: 15,
  elevationLossMeters: 15,
  minElevationMeters: 213.6,
  maxElevationMeters: 231.4,
  elapsedSeconds: 2970,
  movingSeconds: 2965,
}

/** The same track as an untimed, elevation-less route (no profile, no times). */
export const bareRouteGeoJson: TrackGeoJson = {
  ...gliwice10kGeoJson,
  profile: null,
  splits: gliwice10kGeoJson.splits.map((s) => ({ ...s, elapsedSeconds: null, elevationDeltaMeters: null })),
}

export const bareRouteSummary: TrackSummary = {
  pointCount: 893,
  distanceMeters: 10_004.8,
  elevationGainMeters: null,
  elevationLossMeters: null,
  minElevationMeters: null,
  maxElevationMeters: null,
  elapsedSeconds: null,
  movingSeconds: null,
}
