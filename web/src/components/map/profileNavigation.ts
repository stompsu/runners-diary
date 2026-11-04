import type { ProfileSample } from '@/features/tracks/trackGeoJson'

/** Index of the sample whose distance is closest to `distanceMeters` (samples are sorted by distance). */
export function indexAtDistance(profile: readonly ProfileSample[], distanceMeters: number): number {
  if (profile.length === 0) return 0
  let low = 0
  let high = profile.length - 1
  while (low < high) {
    const mid = (low + high) >> 1
    if (profile[mid]![0] < distanceMeters) low = mid + 1
    else high = mid
  }
  const before = profile[low - 1]
  if (before && distanceMeters - before[0] < profile[low]![0] - distanceMeters) return low - 1
  return low
}

/**
 * The slider-style keyboard model of the profile: arrows step one sample, Page keys a tenth of the track,
 * Home/End jump to the ends. Returns the new index, or null when the key is not handled.
 * With no current position, the first key press starts at the beginning (or the end for ArrowLeft/End).
 */
export function nextProfileIndex(key: string, current: number | null, length: number): number | null {
  if (length === 0) return null
  const last = length - 1
  const page = Math.max(1, Math.round(length / 10))
  const clamp = (value: number) => Math.min(last, Math.max(0, value))
  switch (key) {
    case 'ArrowRight':
    case 'ArrowUp':
      return current === null ? 0 : clamp(current + 1)
    case 'ArrowLeft':
    case 'ArrowDown':
      return current === null ? last : clamp(current - 1)
    case 'PageUp':
      return clamp((current ?? 0) + page)
    case 'PageDown':
      return clamp((current ?? 0) - page)
    case 'Home':
      return 0
    case 'End':
      return last
    default:
      return null
  }
}
