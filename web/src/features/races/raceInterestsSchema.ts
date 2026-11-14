import { z } from 'zod'
import { kmToMeters, parseKm } from '@/lib/format/units'
import { POLISH_REGION_CODES } from './polishRegions'
import { RACE_KINDS } from './raceTypes'

// The production CSP has no 'unsafe-eval' (ADR 0029); zod's JIT probes `new Function('')`. Same switch as runFormSchema.
z.config({ jitless: true })

/**
 * Client-side mirror of the server's interest rules (ADR 0037 §1, Domain `RaceInterestRules`). Instant feedback only:
 * the server stays authoritative and a drift degrades to a server field error. Messages are `races` namespace keys
 * under `interests.validation.*`, written here as `validation.*` (the page prefixes them).
 */
export type ValidationKey = `validation.${string}`

export const RADIUS_KM = { min: 5, max: 300, default: 50 } as const
export const DISTANCE_KM = { min: 1, max: 250 } as const
export const HORIZON_DAYS = { min: 7, max: 365 } as const
export const HORIZON_PRESETS = [30, 60, 90, 120, 180, 365] as const
export const KEYWORDS = { maxLength: 100, maxTerms: 5, minTermLength: 2, maxTermLength: 30 } as const
export const CENTER_NAME_MAX = 200

const blank = (value: string) => value.trim() === ''

/** Whitespace-separated terms, like the server's `KeywordTerms`. */
export function keywordTerms(keywords: string): string[] {
  return keywords.split(/\s+/).filter((term) => term !== '')
}

/** Metres of a km text, or null when it is not a positive number. */
const metresOf = (value: string): number | null => {
  const km = parseKm(value)
  return km === null ? null : kmToMeters(km)
}

const distanceValid = (value: string) => {
  if (blank(value)) return true
  const metres = metresOf(value)
  return metres !== null && metres >= DISTANCE_KM.min * 1000 && metres <= DISTANCE_KM.max * 1000
}

const message = (key: ValidationKey) => ({ message: key })

export const raceInterestsSchema = z
  .object({
    kinds: z.array(z.enum(RACE_KINDS)).min(1, message('validation.kindsRequired')),
    regionCodes: z
      .array(z.enum(POLISH_REGION_CODES, message('validation.regionUnknown')))
      .max(POLISH_REGION_CODES.length, message('validation.regionUnknown'))
      .refine((codes) => new Set(codes).size === codes.length, message('validation.regionUnknown')),
    center: z
      .object({
        latitude: z.number().refine((v) => Number.isFinite(v) && v >= -90 && v <= 90, message('validation.latitude')),
        longitude: z
          .number()
          .refine((v) => Number.isFinite(v) && v >= -180 && v <= 180, message('validation.longitude')),
        name: z.string().max(CENTER_NAME_MAX, message('validation.centerNameLength')),
      })
      .nullable(),
    /** km; only meaningful (and only checked) while a place is chosen. */
    radiusKm: z.string(),
    minDistanceKm: z.string().refine(distanceValid, message('validation.distanceRange')),
    maxDistanceKm: z.string().refine(distanceValid, message('validation.distanceRange')),
    horizonDays: z
      .number()
      .int()
      .min(HORIZON_DAYS.min, message('validation.horizonRange'))
      .max(HORIZON_DAYS.max, message('validation.horizonRange')),
    keywords: z.string(),
    showInCalendar: z.boolean(),
  })
  .superRefine((values, ctx) => {
    const issue = (path: string, key: ValidationKey) => ctx.addIssue({ code: 'custom', path: [path], message: key })

    if (values.center) {
      const metres = metresOf(values.radiusKm)
      if (blank(values.radiusKm)) issue('radiusKm', 'validation.radiusRequired')
      else if (metres === null || metres < RADIUS_KM.min * 1000 || metres > RADIUS_KM.max * 1000) {
        issue('radiusKm', 'validation.radiusRange')
      }
    }

    const min = blank(values.minDistanceKm) ? null : metresOf(values.minDistanceKm)
    const max = blank(values.maxDistanceKm) ? null : metresOf(values.maxDistanceKm)
    if (min !== null && max !== null && distanceValid(values.minDistanceKm) && distanceValid(values.maxDistanceKm)) {
      if (max < min) issue('maxDistanceKm', 'validation.distanceOrder')
    }

    const terms = keywordTerms(values.keywords)
    if (terms.join(' ').length > KEYWORDS.maxLength) issue('keywords', 'validation.keywordsLength')
    else if (terms.length > KEYWORDS.maxTerms) issue('keywords', 'validation.keywordsTerms')
    else if (terms.some((t) => t.length < KEYWORDS.minTermLength || t.length > KEYWORDS.maxTermLength)) {
      issue('keywords', 'validation.keywordsTermLength')
    }
  })

export type RaceInterestsFormValues = z.infer<typeof raceInterestsSchema>
