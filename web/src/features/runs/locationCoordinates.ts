import type { LatLon } from '@/components/map/types'

const LAST_PICK_KEY = 'rd.lastMapPick'

export const validLatitude = (value: unknown): value is number =>
  typeof value === 'number' && Number.isFinite(value) && value >= -90 && value <= 90
export const validLongitude = (value: unknown): value is number =>
  typeof value === 'number' && Number.isFinite(value) && value >= -180 && value <= 180

/** "50,2945" and "50.2945" are both fine; anything else (units, degree signs, empty) is not a coordinate. */
export function parseCoordinate(input: string): number | null {
  const normalized = input.trim().replace(',', '.')
  if (!/^-?\d+(\.\d+)?$/.test(normalized)) return null
  const value = Number(normalized)
  return Number.isFinite(value) ? value : null
}

/** For text fields: at most 6 decimals (about 0.1 m), no trailing zeros. */
export const coordinateText = (value: number): string => String(Number(value.toFixed(6)))

export const formatCoordinates = (latitude: number, longitude: number): string =>
  `${latitude.toFixed(5)}, ${longitude.toFixed(5)}`

/** The last place picked in this tab: where the next picker opens when the run has no pin yet. */
export function readLastPick(): LatLon | null {
  try {
    const raw: unknown = JSON.parse(sessionStorage.getItem(LAST_PICK_KEY) ?? 'null')
    if (typeof raw !== 'object' || raw === null) return null
    const { latitude, longitude } = raw as Partial<LatLon>
    return validLatitude(latitude) && validLongitude(longitude) ? { latitude, longitude } : null
  } catch {
    return null
  }
}

export function saveLastPick(position: LatLon): void {
  try {
    sessionStorage.setItem(LAST_PICK_KEY, JSON.stringify(position))
  } catch {
    // Storage may be unavailable (private mode); the pick itself still works.
  }
}
