import { formatKmInput, kmToMeters, parseKm } from '@/lib/format/units'
import { mapFieldErrors, type FieldErrors } from '@/lib/api/problem'
import { POLISH_REGION_CODES } from './polishRegions'
import { keywordTerms, type RaceInterestsFormValues } from './raceInterestsSchema'
import { RACE_KINDS, type RaceInterestsDto, type UpdateRaceInterestsRequest } from './raceTypes'

const kmText = (meters: number | null | undefined): string => (meters == null ? '' : formatKmInput(meters))

/** The response (display units: km, strings) as form values. `configured: false` already carries the defaults. */
export function toFormValues(dto: RaceInterestsDto): RaceInterestsFormValues {
  return {
    kinds: [...dto.kinds],
    regionCodes: POLISH_REGION_CODES.filter((code) => dto.regionCodes.includes(code)),
    center: dto.center
      ? { latitude: dto.center.latitude, longitude: dto.center.longitude, name: dto.center.name ?? '' }
      : null,
    radiusKm: dto.center ? kmText(dto.radiusMeters) : '',
    minDistanceKm: kmText(dto.minDistanceMeters),
    maxDistanceKm: kmText(dto.maxDistanceMeters),
    horizonDays: dto.horizonDays,
    keywords: dto.keywords ?? '',
    showInCalendar: dto.showInCalendar,
  }
}

const metresOrNull = (text: string): number | null => {
  const km = parseKm(text)
  return km === null ? null : kmToMeters(km)
}

/** Form values (validated) as the PUT body: SI units, canonical order, a radius only together with a place. */
export function toWriteRequest(values: RaceInterestsFormValues): UpdateRaceInterestsRequest {
  const keywords = keywordTerms(values.keywords).join(' ')
  return {
    kinds: RACE_KINDS.filter((kind) => values.kinds.includes(kind)),
    regionCodes: POLISH_REGION_CODES.filter((code) => values.regionCodes.includes(code)),
    center: values.center
      ? {
          latitude: values.center.latitude,
          longitude: values.center.longitude,
          name: values.center.name.trim() === '' ? null : values.center.name.trim(),
        }
      : null,
    radiusMeters: values.center ? metresOrNull(values.radiusKm) : null,
    minDistanceMeters: metresOrNull(values.minDistanceKm),
    maxDistanceMeters: metresOrNull(values.maxDistanceKm),
    horizonDays: values.horizonDays,
    keywords: keywords === '' ? null : keywords,
    showInCalendar: values.showInCalendar,
  }
}

/** Wire path -> form field (ADR 0018 §3): SI keys to display-unit fields; place errors land on the place section. */
export const RACE_INTERESTS_KEY_MAP: Readonly<Record<string, string>> = {
  radiusMeters: 'radiusKm',
  minDistanceMeters: 'minDistanceKm',
  maxDistanceMeters: 'maxDistanceKm',
  centerName: 'center.name',
  'center.latitude': 'center',
  'center.longitude': 'center',
}

export const RACE_INTERESTS_FIELDS: ReadonlySet<string> = new Set([
  'kinds',
  'regionCodes',
  'center',
  'center.name',
  'radiusKm',
  'minDistanceKm',
  'maxDistanceKm',
  'horizonDays',
  'keywords',
])

export function mapRaceInterestsErrors(fieldErrors: FieldErrors) {
  return mapFieldErrors(fieldErrors, {
    keyMap: RACE_INTERESTS_KEY_MAP,
    isField: (name) => RACE_INTERESTS_FIELDS.has(name),
  })
}
