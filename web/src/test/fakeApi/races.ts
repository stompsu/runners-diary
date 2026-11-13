import type { components } from '../../api/generated/schema.d.ts'

type S = components['schemas']
export type RaceInterestsDto = S['RaceInterestsResponse']
export type UpdateRaceInterestsRequest = S['UpdateRaceInterestsRequest']

const KINDS: readonly S['RaceKind'][] = ['Road', 'Trail', 'Mountain', 'Ultra', 'CrossCountry', 'Ocr', 'Other']
/** ISO 3166-2:PL, `PL-02` ... `PL-32` (the Domain `PolishRegions` table). */
const REGION_CODES = Array.from({ length: 16 }, (_, i) => `PL-${String(2 + i * 2).padStart(2, '0')}`)

/** `GET /api/races/interests` without a saved profile: `configured: false` plus the defaults of ADR 0037 §1. */
export function defaultRaceInterests(): RaceInterestsDto {
  return {
    configured: false,
    kinds: ['Road', 'Trail'],
    regionCodes: [],
    center: null,
    radiusMeters: null,
    minDistanceMeters: 5000,
    maxDistanceMeters: 42195,
    horizonDays: 120,
    keywords: null,
    showInCalendar: true,
    updatedAt: null,
  }
}

/** The guest demo profile (M8 plan §3.2): Silesia, Gliwice with 60 km, 5 km to a marathon, 120 days. */
export function demoRaceInterests(updatedAt: string): RaceInterestsDto {
  return {
    configured: true,
    kinds: ['Road', 'Trail', 'Mountain'],
    regionCodes: ['PL-24'],
    center: { latitude: 50.2945, longitude: 18.6714, name: 'Gliwice' },
    radiusMeters: 60_000,
    minDistanceMeters: 5000,
    maxDistanceMeters: 42195,
    horizonDays: 120,
    keywords: null,
    showInCalendar: true,
    updatedAt,
  }
}

const MEMBERS = new Set([
  'kinds',
  'regionCodes',
  'center',
  'radiusMeters',
  'minDistanceMeters',
  'maxDistanceMeters',
  'horizonDays',
  'keywords',
  'showInCalendar',
])

const isNumber = (value: unknown): value is number => typeof value === 'number' && Number.isFinite(value)

/** Keyword terms like the Domain `KeywordTerms`: whitespace separated. */
const termsOf = (keywords: string) => keywords.split(/\s+/).filter((term) => term !== '')

/**
 * The server rules of ADR 0037 §1 / `RaceInterestRules`, with the same error keys (camelCase path; the Domain uses
 * `centerName` for the place name). Unknown members are rejected, as the real API does.
 */
export function validateRaceInterests(
  body: unknown,
): { errors: Record<string, string[]> } | { value: UpdateRaceInterestsRequest } {
  const errors: Record<string, string[]> = {}
  const add = (key: string, text: string) => (errors[key] ??= []).push(text)
  if (typeof body !== 'object' || body === null || Array.isArray(body)) return { errors: { body: ['Invalid body.'] } }
  const v = body as Record<string, unknown>
  for (const key of Object.keys(v)) if (!MEMBERS.has(key)) add(key, 'Unknown member.')

  const kinds = v.kinds
  if (!Array.isArray(kinds) || kinds.length === 0) add('kinds', 'Select at least one kind.')
  else if (kinds.some((kind) => !KINDS.includes(kind as S['RaceKind']))) add('kinds', 'Unknown kind.')

  const codes = v.regionCodes
  if (!Array.isArray(codes)) add('regionCodes', 'Required (may be empty).')
  else {
    if (codes.length > REGION_CODES.length) add('regionCodes', 'At most 16 regions.')
    if (codes.some((code) => typeof code !== 'string' || !REGION_CODES.includes(code))) {
      add('regionCodes', 'Unknown region code (expected ISO 3166-2 such as PL-24).')
    } else if (new Set(codes).size !== codes.length) add('regionCodes', 'Duplicate region code.')
  }

  const center = v.center ?? null
  const radius = v.radiusMeters ?? null
  if (center !== null && radius === null) add('radiusMeters', 'Required together with center.')
  if (center === null && radius !== null) add('center', 'Required together with radiusMeters.')
  if (center !== null) {
    const c = center as Record<string, unknown>
    if (typeof center !== 'object' || !isNumber(c.latitude) || c.latitude < -90 || c.latitude > 90) {
      add('center', 'Latitude must be between -90 and 90.')
    } else if (!isNumber(c.longitude) || c.longitude < -180 || c.longitude > 180) {
      add('center', 'Longitude must be between -180 and 180.')
    }
    if (typeof c.name === 'string' && c.name.length > 200) add('centerName', 'Must be at most 200 characters.')
  }
  if (radius !== null && (!Number.isInteger(radius) || (radius as number) < 5000 || (radius as number) > 300_000)) {
    add('radiusMeters', 'Must be between 5000 and 300000.')
  }

  const distance = (key: 'minDistanceMeters' | 'maxDistanceMeters'): number | null => {
    const value = v[key] ?? null
    if (value === null) return null
    if (!isNumber(value) || value < 1000 || value > 250_000) {
      add(key, 'Must be between 1000 and 250000.')
      return null
    }
    return value
  }
  const min = distance('minDistanceMeters')
  const max = distance('maxDistanceMeters')
  if (min !== null && max !== null && max < min) {
    add('maxDistanceMeters', 'Must be greater than or equal to minDistanceMeters.')
  }

  const horizon = v.horizonDays
  if (!Number.isInteger(horizon) || (horizon as number) < 7 || (horizon as number) > 365) {
    add('horizonDays', 'Must be between 7 and 365.')
  }

  const keywords = v.keywords ?? null
  if (keywords !== null) {
    if (typeof keywords !== 'string') add('keywords', 'Must be text.')
    else {
      const terms = termsOf(keywords)
      if (terms.join(' ').length > 100) add('keywords', 'Must be at most 100 characters.')
      else if (terms.length > 5) add('keywords', 'At most 5 terms.')
      else if (terms.some((term) => term.length < 2 || term.length > 30)) {
        add('keywords', 'Each term must be 2 to 30 characters.')
      }
    }
  }
  if (typeof v.showInCalendar !== 'boolean') add('showInCalendar', 'Required.')

  if (Object.keys(errors).length > 0) return { errors }
  const c = center as { latitude: number; longitude: number; name?: string | null } | null
  const text = typeof keywords === 'string' ? termsOf(keywords).join(' ') : ''
  return {
    value: {
      kinds: v.kinds as S['RaceKind'][],
      regionCodes: v.regionCodes as string[],
      center: c ? { latitude: c.latitude, longitude: c.longitude, name: c.name?.trim() ? c.name.trim() : null } : null,
      radiusMeters: radius as number | null,
      minDistanceMeters: min,
      maxDistanceMeters: max,
      horizonDays: horizon as number,
      keywords: text === '' ? null : text,
      showInCalendar: v.showInCalendar as boolean,
    },
  }
}
