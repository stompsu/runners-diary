/**
 * The one place that formats physical values. Values arrive in SI (metres, seconds); the UI shows
 * km, min/km and metres of elevation (metric only in M3). `/km` and unit symbols are not translated.
 */

export function metersToKm(meters: number): number {
  return meters / 1000
}

/** Rounded to 1 m, like the form mapping (plan §3.5). */
export function kmToMeters(km: number): number {
  return Math.round(km * 1000)
}

/** "12.35 km" (up to 2 decimals by default; compact views pass fewer). */
export function formatDistance(meters: number, locale: string, maxFractionDigits = 2): string {
  return new Intl.NumberFormat(locale, {
    style: 'unit',
    unit: 'kilometer',
    unitDisplay: 'short',
    minimumFractionDigits: 0,
    maximumFractionDigits: maxFractionDigits,
  }).format(metersToKm(meters))
}

/** Plain number for an input value: 12.35 (not localized, '.' decimal). */
export function formatKmInput(meters: number): string {
  return String(Number(metersToKm(meters).toFixed(3)))
}

/** Parses a user-entered km value ("12,5" or "12.5"); null when not a positive finite number. */
export function parseKm(input: string): number | null {
  const normalized = input.trim().replace(',', '.')
  if (!/^\d+(\.\d+)?$/.test(normalized)) return null
  const km = Number(normalized)
  return km > 0 && Number.isFinite(km) ? km : null
}

/** "120 m" */
export function formatElevation(meters: number, locale: string): string {
  return new Intl.NumberFormat(locale, {
    style: 'unit',
    unit: 'meter',
    unitDisplay: 'short',
    maximumFractionDigits: 0,
  }).format(meters)
}

/** Seconds per kilometre, or null when it cannot be computed. */
export function paceSecondsPerKm(durationSeconds: number, distanceMeters: number): number | null {
  if (!(distanceMeters > 0) || !(durationSeconds > 0)) return null
  return durationSeconds / (distanceMeters / 1000)
}

/** "5:30 /km"; 359.6 s rounds to 6:00, never "5:60". Null when distance or duration is missing/zero. */
export function formatPace(
  durationSeconds: number | null | undefined,
  distanceMeters: number | null | undefined,
): string | null {
  if (durationSeconds == null || distanceMeters == null) return null
  const pace = paceSecondsPerKm(durationSeconds, distanceMeters)
  if (pace === null) return null
  const total = Math.round(pace)
  return `${Math.floor(total / 60)}:${String(total % 60).padStart(2, '0')} /km`
}

/** "5:51 /km" from a pace in seconds per kilometre (the insights report ships paces, not durations). */
export function formatPaceValue(secondsPerKm: number): string {
  const total = Math.round(secondsPerKm)
  return `${Math.floor(total / 60)}:${String(total % 60).padStart(2, '0')} /km`
}

/** "+3 m" / "-2 m" / "0 m": a signed elevation change (splits table). */
export function formatElevationDelta(meters: number, locale: string): string {
  return new Intl.NumberFormat(locale, {
    style: 'unit',
    unit: 'meter',
    unitDisplay: 'short',
    maximumFractionDigits: 0,
    signDisplay: 'exceptZero',
  }).format(meters)
}
