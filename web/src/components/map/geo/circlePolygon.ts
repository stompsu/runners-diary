import type { LatLon } from '../types'

/** Mean Earth radius in metres (same sphere as the server's haversine). */
const EARTH_RADIUS_M = 6_371_008.8
const RAD = Math.PI / 180

/**
 * A geodesic circle on a sphere as a closed GeoJSON ring of `[lon, lat]` (first vertex repeated at the end, so
 * `vertices + 1` positions). Vertex i lies on bearing `i * 360 / vertices` at exactly `radiusMeters` from the centre.
 */
export function circlePolygon(center: LatLon, radiusMeters: number, vertices = 64): [number, number][] {
  const lat1 = center.latitude * RAD
  const lon1 = center.longitude * RAD
  const d = radiusMeters / EARTH_RADIUS_M
  const ring: [number, number][] = []
  for (let i = 0; i < vertices; i++) {
    const bearing = (2 * Math.PI * i) / vertices
    const lat2 = Math.asin(Math.sin(lat1) * Math.cos(d) + Math.cos(lat1) * Math.sin(d) * Math.cos(bearing))
    const lon2 =
      lon1 + Math.atan2(Math.sin(bearing) * Math.sin(d) * Math.cos(lat1), Math.cos(d) - Math.sin(lat1) * Math.sin(lat2))
    ring.push([((((lon2 / RAD + 180) % 360) + 360) % 360) - 180, lat2 / RAD])
  }
  ring.push(ring[0]!)
  return ring
}

/** `[west, south, east, north]` of a ring (circles crossing the antimeridian are not handled; Poland-scale use). */
export function ringBounds(ring: [number, number][]): [number, number, number, number] {
  const lons = ring.map((p) => p[0])
  const lats = ring.map((p) => p[1])
  return [Math.min(...lons), Math.min(...lats), Math.max(...lons), Math.max(...lats)]
}
