import { z } from 'zod'
import { isValid as isValidDate } from '@/lib/dates/plainDate'
import { parseDuration } from '@/lib/format/duration'
import { parseKm } from '@/lib/format/units'
import { parseNonNegative, parseWholeNumber } from './runFormMapping'
import { RACE_CATEGORIES, SURFACES, rulesOf, type RunFormValues, type RunTypeDto } from './runFormTypes'

// The production CSP has no 'unsafe-eval' (ADR 0029), and zod's JIT probes `new Function('')`. This is the only module
// that imports zod, so switching to the interpreter here keeps zod in its lazy chunk.
z.config({ jitless: true })

/**
 * Client-side mirror of the API's run rules (ADR 0018 §2, docs/api-contract.md "Domain rules", data-model limits).
 * Instant feedback only: the server stays authoritative, and a drift degrades to a server field error. Messages are
 * `runs` namespace i18n keys under `validation.*`; the form translates them when it shows them.
 */

export type ValidationKey = `validation.${string}`

const MAX_DISTANCE_M = 1_000_000
const MAX_DURATION_S = 604_800
const MAX_ELEVATION_M = 100_000
const MIN_DATE = '1950-01-01'
const MAX_DATE = '2100-12-31'
const TIME_PATTERN = /^([01]\d|2[0-3]):[0-5]\d$/
const ZONE_PATTERN = /^(UTC|GMT|[A-Za-z][A-Za-z0-9_+-]*(\/[A-Za-z0-9_+-]+)+)$/

const blank = (value: string) => value.trim() === ''

/** Optional text field: empty is fine, otherwise `valid`. */
const optional = (valid: (value: string) => boolean, message: ValidationKey) =>
  z.string().refine((value) => blank(value) || valid(value), { message })

const maxLength = (max: number, message: ValidationKey) => z.string().max(max, { message })

const durationValid = (value: string) => {
  const seconds = parseDuration(value)
  return seconds !== null && seconds >= 1 && seconds <= MAX_DURATION_S
}
const kmValid = (value: string) => {
  const km = parseKm(value)
  return km !== null && km * 1000 <= MAX_DISTANCE_M
}
const elevationValid = (value: string) => {
  const metres = parseNonNegative(value)
  return metres !== null && metres <= MAX_ELEVATION_M
}
const heartRateValid = (value: string) => {
  const bpm = parseWholeNumber(value)
  return bpm !== null && bpm >= 30 && bpm <= 250
}
const placeValid = (value: string) => {
  const place = parseWholeNumber(value)
  return place !== null && place >= 1 && place <= 1_000_000
}
const httpUrlValid = (value: string) => {
  if (value.trim().length > 2048) return false
  try {
    const url = new URL(value.trim())
    return url.protocol === 'http:' || url.protocol === 'https:'
  } catch {
    return false
  }
}

const raceSchema = z.object({
  name: z
    .string()
    .refine((value) => !blank(value), { message: 'validation.raceNameRequired' satisfies ValidationKey })
    .refine((value) => value.trim().length <= 150, { message: 'validation.raceNameLength' satisfies ValidationKey }),
  distanceCategory: z.enum(RACE_CATEGORIES as [string, ...string[]]),
  officialDistanceKm: optional(kmValid, 'validation.distance'),
  bibNumber: maxLength(20, 'validation.bibLength'),
  targetTime: optional(durationValid, 'validation.duration'),
  resultTime: optional(durationValid, 'validation.duration'),
  overallPlace: optional(placeValid, 'validation.place'),
  categoryPlace: optional(placeValid, 'validation.place'),
  categoryName: maxLength(50, 'validation.categoryNameLength'),
  resultsUrl: optional(httpUrlValid, 'validation.resultsUrl'),
})

const locationSchema = z.object({
  latitude: z.number().refine((value) => Number.isFinite(value) && value >= -90 && value <= 90, {
    message: 'validation.latitude' satisfies ValidationKey,
  }),
  longitude: z.number().refine((value) => Number.isFinite(value) && value >= -180 && value <= 180, {
    message: 'validation.longitude' satisfies ValidationKey,
  }),
  name: maxLength(200, 'validation.locationNameLength'),
})

const baseShape = z.object({
  date: z
    .string()
    .refine((value) => isValidDate(value), { message: 'validation.dateInvalid' satisfies ValidationKey })
    .refine((value) => value >= MIN_DATE && value <= MAX_DATE, {
      message: 'validation.dateRange' satisfies ValidationKey,
    }),
  startTime: optional((value) => TIME_PATTERN.test(value), 'validation.startTime'),
  timeZoneId: optional((value) => value.length <= 64 && ZONE_PATTERN.test(value), 'validation.timeZone'),
  status: z.enum(['Planned', 'Completed']),
  runTypeId: z.number().int().positive(),
  title: maxLength(120, 'validation.titleLength'),
  notes: maxLength(4000, 'validation.notesLength'),
  distanceKm: optional(kmValid, 'validation.distance'),
  duration: optional(durationValid, 'validation.duration'),
  elevationGain: optional(elevationValid, 'validation.elevation'),
  elevationLoss: optional(elevationValid, 'validation.elevation'),
  avgHeartRate: optional(heartRateValid, 'validation.heartRate'),
  maxHeartRate: optional(heartRateValid, 'validation.heartRate'),
  rpe: optional((value) => {
    const rpe = parseWholeNumber(value)
    return rpe !== null && rpe >= 1 && rpe <= 10
  }, 'validation.rpe'),
  gear: maxLength(100, 'validation.gearLength'),
  weather: maxLength(200, 'validation.weatherLength'),
  surface: z.enum(['', ...SURFACES] as [string, ...string[]]),
  location: locationSchema.nullable(),
  race: raceSchema.nullable(),
  rating: z.object({
    stars: z.number().int().min(0).max(5),
    comment: maxLength(500, 'validation.commentLength'),
  }),
})

const METRICS = [
  'distanceKm',
  'duration',
  'elevationGain',
  'elevationLoss',
  'avgHeartRate',
  'maxHeartRate',
  'rpe',
] as const

/** `types` supplies the category and rest-day flag that the cross-field rules depend on. */
export function createRunFormSchema(types: readonly RunTypeDto[]): z.ZodType<RunFormValues, RunFormValues> {
  return baseShape.superRefine((values, ctx) => {
    const issue = (path: (string | number)[], message: ValidationKey) => ctx.addIssue({ code: 'custom', path, message })
    const type = types.find((candidate) => candidate.id === values.runTypeId)
    const rules = rulesOf(type)
    const completed = values.status === 'Completed'

    if (!type) issue(['runTypeId'], 'validation.runType')
    if (values.rating.stars > 0 && !completed) issue(['rating', 'stars'], 'validation.ratingCompletedOnly')
    if (values.race && !rules.isRace) issue(['runTypeId'], 'validation.raceTypeOnly')
    if (!values.race && rules.isRace) issue(['race', 'name'], 'validation.raceNameRequired')
    if (values.race && !completed) {
      if (!blank(values.race.resultTime)) issue(['race', 'resultTime'], 'validation.resultCompletedOnly')
      if (!blank(values.race.overallPlace)) issue(['race', 'overallPlace'], 'validation.resultCompletedOnly')
      if (!blank(values.race.categoryPlace)) issue(['race', 'categoryPlace'], 'validation.resultCompletedOnly')
    }
    if (rules.isRestDay) {
      for (const field of METRICS) if (!blank(values[field])) issue([field], 'validation.restDayNoMetrics')
      if (values.race) issue(['runTypeId'], 'validation.restDayNoRace')
      if (values.rating.stars > 0) issue(['rating', 'stars'], 'validation.restDayNoRating')
    }
    const avg = parseWholeNumber(values.avgHeartRate)
    const max = parseWholeNumber(values.maxHeartRate)
    if (avg !== null && max !== null && max < avg) issue(['maxHeartRate'], 'validation.maxBelowAvg')
  }) as unknown as z.ZodType<RunFormValues, RunFormValues>
}
