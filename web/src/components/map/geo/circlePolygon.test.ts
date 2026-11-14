import { describe, expect, it } from 'vitest'
import { circlePolygon, ringBounds } from './circlePolygon'

const R = 6_371_008.8
const rad = (d: number) => (d * Math.PI) / 180
function haversine(aLat: number, aLon: number, bLat: number, bLon: number) {
  const h =
    Math.sin(rad(bLat - aLat) / 2) ** 2 +
    Math.cos(rad(aLat)) * Math.cos(rad(bLat)) * Math.sin(rad(bLon - aLon) / 2) ** 2
  return 2 * R * Math.asin(Math.sqrt(h))
}

describe('circlePolygon', () => {
  const center = { latitude: 50.29, longitude: 18.67 }

  it('has 64 vertices and a closed ring', () => {
    const ring = circlePolygon(center, 25_000)
    expect(ring).toHaveLength(65)
    expect(ring[64]).toEqual(ring[0])
  })

  it('places the cardinal vertices at the radius (haversine, within 1 m)', () => {
    const ring = circlePolygon(center, 25_000)
    for (const i of [0, 16, 32, 48]) {
      const [lon, lat] = ring[i]!
      expect(Math.abs(haversine(center.latitude, center.longitude, lat, lon) - 25_000)).toBeLessThan(1)
    }
    expect(ring[0]![1]).toBeGreaterThan(center.latitude) // bearing 0 = north
    expect(ring[16]![0]).toBeGreaterThan(center.longitude) // bearing 90 = east
  })

  it('keeps every vertex at the radius and gives bounds around the centre', () => {
    const ring = circlePolygon(center, 300_000)
    for (const [lon, lat] of ring) {
      expect(Math.abs(haversine(center.latitude, center.longitude, lat, lon) - 300_000)).toBeLessThan(1)
    }
    const [w, s, e, n] = ringBounds(ring)
    expect(w).toBeLessThan(center.longitude)
    expect(e).toBeGreaterThan(center.longitude)
    expect(s).toBeLessThan(center.latitude)
    expect(n).toBeGreaterThan(center.latitude)
  })
})
