import { describe, expect, it } from 'vitest'
import { bareRouteGeoJson, gliwice10kGeoJson } from '@/test/fixtures/tracks'
import type { components } from '@/api/generated/schema'
import { profilePosition, toTrackGeoJson, vertexCount } from './trackGeoJson'

describe('the processed-track fixture (ADR 0021 §5 invariants)', () => {
  const track = gliwice10kGeoJson

  it('is a FeatureCollection of line, start and finish, in that order, with matching endpoints', () => {
    expect(track.type).toBe('FeatureCollection')
    expect(track.formatVersion).toBe(1)
    expect(track.features.map((f) => f.id)).toEqual(['line', 'start', 'finish'])
    const segment = track.features[0].geometry.coordinates[0]!
    expect(track.features[1].geometry.coordinates).toEqual(segment[0])
    expect(track.features[2].geometry.coordinates).toEqual(segment.at(-1))
  })

  it('has a bbox that contains every vertex', () => {
    const [west, south, east, north] = track.bbox
    for (const [lon, lat] of track.features[0].geometry.coordinates.flat()) {
      expect(lon).toBeGreaterThanOrEqual(west)
      expect(lon).toBeLessThanOrEqual(east)
      expect(lat).toBeGreaterThanOrEqual(south)
      expect(lat).toBeLessThanOrEqual(north)
    }
  })

  it('has at most 601 profile samples with increasing distance and lon/lat on each', () => {
    const profile = track.profile!
    expect(profile.length).toBeLessThanOrEqual(601)
    for (let i = 1; i < profile.length; i++) expect(profile[i]![0]).toBeGreaterThan(profile[i - 1]![0])
    expect(profilePosition(profile[0]!)).toEqual(track.features[1].geometry.coordinates)
  })

  it('ends with a partial split', () => {
    expect(track.splits).toHaveLength(11)
    expect(track.splits.at(-1)!.distanceMeters).toBeLessThan(1000)
  })
})

describe('vertexCount', () => {
  it('sums the vertices of all segments', () => {
    expect(vertexCount(gliwice10kGeoJson)).toBe(50)
    expect(vertexCount(bareRouteGeoJson)).toBe(50)
    const twoSegments = {
      ...gliwice10kGeoJson,
      features: [
        {
          ...gliwice10kGeoJson.features[0],
          geometry: {
            type: 'MultiLineString' as const,
            coordinates: [
              [[1, 2]],
              [
                [3, 4],
                [5, 6],
              ],
            ] as [number, number][][],
          },
        },
        gliwice10kGeoJson.features[1],
        gliwice10kGeoJson.features[2],
      ] as typeof gliwice10kGeoJson.features,
    }
    expect(vertexCount(twoSegments)).toBe(3)
  })
})

describe('toTrackGeoJson (narrowing the generated type)', () => {
  const document = gliwice10kGeoJson as unknown as components['schemas']['TrackGeoJson']

  it('accepts what the endpoint documents', () => {
    expect(toTrackGeoJson(document)).toBe(gliwice10kGeoJson)
  })

  it('rejects a document that is not a line, start, finish collection', () => {
    expect(toTrackGeoJson({ ...document, type: 'Feature' })).toBeNull()
    expect(toTrackGeoJson({ ...document, bbox: [1, 2, 3] })).toBeNull()
    expect(toTrackGeoJson({ ...document, features: [...document.features].reverse() })).toBeNull()
    expect(toTrackGeoJson({ ...document, features: document.features.slice(0, 2) })).toBeNull()
    const badLine = { ...document.features[0]!, geometry: { type: 'Point', coordinates: [1, 2] } }
    expect(toTrackGeoJson({ ...document, features: [badLine, ...document.features.slice(1)] })).toBeNull()
  })
})
