// Relative on purpose: the fake API (also compiled for Playwright, without the `@` alias) imports this module.
import type { components } from '../../api/generated/schema.d.ts'

/**
 * The processed-track document (ADR 0021 §5): a valid RFC 7946 FeatureCollection with three foreign members
 * (`formatVersion`, `profile`, `splits`). This is the one precise TypeScript description of it. The generated
 * `TrackGeoJson` schema type is looser (`coordinates: unknown`, plain arrays); `toTrackGeoJson` narrows it and the
 * compile-time checks at the bottom fail the build when the two drift apart.
 */

/** `[longitude, latitude]`, WGS 84 (RFC 7946 order). */
export type LonLat = readonly [longitude: number, latitude: number]

/** `[west, south, east, north]` */
export type BBox = readonly [west: number, south: number, east: number, north: number]

export interface PointGeometry {
  type: 'Point'
  coordinates: LonLat
}

export interface MultiLineStringGeometry {
  type: 'MultiLineString'
  /** One array of positions per GPX segment. */
  coordinates: LonLat[][]
}

export interface TrackFeature<TGeometry, TRole extends string> {
  type: 'Feature'
  id: TRole
  properties: { role: TRole }
  geometry: TGeometry
}

export type TrackLineFeature = TrackFeature<MultiLineStringGeometry, 'line'>
export type TrackStartFeature = TrackFeature<PointGeometry, 'start'>
export type TrackFinishFeature = TrackFeature<PointGeometry, 'finish'>

/** `[distanceMeters (int), elevationMeters, longitude, latitude]` (ADR 0021 §4). */
export type ProfileSample = readonly [
  distanceMeters: number,
  elevationMeters: number,
  longitude: number,
  latitude: number,
]

export interface TrackSplit {
  /** 1-based; the last one may be a partial split (`distanceMeters` < 1000). */
  index: number
  distanceMeters: number
  /** Null when the track has no timestamps. */
  elapsedSeconds: number | null
  /** Null when the track has no elevation. */
  elevationDeltaMeters: number | null
}

export interface TrackGeoJson {
  type: 'FeatureCollection'
  formatVersion: number
  bbox: BBox
  features: [TrackLineFeature, TrackStartFeature, TrackFinishFeature]
  /** Null when the track has no elevation. */
  profile: ProfileSample[] | null
  splits: TrackSplit[]
}

/** Number of line vertices over all segments (also exposed as `data-track-vertices` on the map). */
export function vertexCount(track: TrackGeoJson): number {
  return track.features[0].geometry.coordinates.reduce((sum, segment) => sum + segment.length, 0)
}

export function profilePosition(sample: ProfileSample): LonLat {
  return [sample[2], sample[3]]
}

// ---------- generated schema <-> this module ----------

type GeneratedTrackGeoJson = components['schemas']['TrackGeoJson']

/**
 * Narrows the generated type (`coordinates: unknown`) after a structural sanity check of what the endpoint promises:
 * a FeatureCollection with the line, start and finish features in that order and a 4-number bbox. A document that
 * does not look like that (a future `formatVersion`, a proxy error page) yields null and the viewer falls back to text.
 */
export function toTrackGeoJson(document: GeneratedTrackGeoJson): TrackGeoJson | null {
  if (document.type !== 'FeatureCollection' || document.bbox.length !== 4) return null
  const roles = document.features.map((feature) => feature.id)
  if (roles.join() !== 'line,start,finish') return null
  const [line, start, finish] = document.features
  if (
    line?.geometry.type !== 'MultiLineString' ||
    start?.geometry.type !== 'Point' ||
    finish?.geometry.type !== 'Point'
  )
    return null
  if (!Array.isArray(line.geometry.coordinates)) return null
  return document as unknown as TrackGeoJson
}

type DeepMutable<T> = T extends readonly (infer U)[]
  ? DeepMutable<U>[]
  : T extends object
    ? { -readonly [K in keyof T]: DeepMutable<T[K]> }
    : T

type IsAssignable<A, B> = [A] extends [B] ? true : false
type Expect<T extends true> = T

// Compile-time compatibility with the generated contract (fails `tsc` on drift, costs nothing at runtime):
// every member the API sends is described here (no member missing on our side) ...
export type TrackGeoJsonHasEveryApiMember = Expect<IsAssignable<keyof GeneratedTrackGeoJson, keyof TrackGeoJson>>
// ... and our precise type is a subtype of the generated one (the API's shape is what we narrow).
export type TrackGeoJsonIsAnApiSubtype = Expect<IsAssignable<DeepMutable<TrackGeoJson>, GeneratedTrackGeoJson>>
// The splits are identical on both sides.
export type TrackSplitMatchesApi = Expect<IsAssignable<TrackSplit, components['schemas']['TrackGeoJsonSplit']>>
