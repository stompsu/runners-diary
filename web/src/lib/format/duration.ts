/** Durations: SI seconds in the model, `h:mm:ss` in the UI (brief; ADR 0017). */

const pad = (n: number) => String(n).padStart(2, '0')

/** 3725 -> "1:02:05"; always hours:minutes:seconds. Rounds to whole seconds. */
export function formatDuration(totalSeconds: number): string {
  const s = Math.max(0, Math.round(totalSeconds))
  const h = Math.floor(s / 3600)
  const m = Math.floor((s % 3600) / 60)
  return `${h}:${pad(m)}:${pad(s % 60)}`
}

/**
 * Parses "h:mm:ss" or "mm:ss" (minutes may exceed 59 in the two-part form, e.g. "95:00").
 * Seconds must be 0-59; with an hours part minutes must be 0-59. Returns seconds, or null when invalid.
 */
export function parseDuration(input: string): number | null {
  const parts = input.trim().split(':')
  if (parts.length < 2 || parts.length > 3 || !parts.every((p) => /^\d{1,3}$/.test(p))) return null
  const nums = parts.map(Number)
  const seconds = nums[nums.length - 1]!
  const minutes = nums[nums.length - 2]!
  const hours = parts.length === 3 ? nums[0]! : 0
  if (seconds > 59) return null
  if (parts.length === 3 && minutes > 59) return null
  return hours * 3600 + minutes * 60 + seconds
}
