import { formatDuration, parseDuration } from '@/lib/format/duration'
import { formatKmInput, kmToMeters, parseKm } from '@/lib/format/units'
import type { RaceFormValues, RunDto, RunFormValues, RunTypeRules, RunWriteRequest, SurfaceValue } from './runFormTypes'

/**
 * Form model <-> wire model (ADR 0018 §1, plan §3.5). Both directions are pure and unit-tested.
 * The form shows km, h:mm:ss and metres; the wire carries SI. `toWriteRequest` is a full replace for PUT, so every
 * member the form does not render (today only `location`) is copied from the loaded run.
 */

export const emptyRace = (): RaceFormValues => ({
  name: '',
  distanceCategory: 'HalfMarathon',
  officialDistanceKm: '',
  bibNumber: '',
  targetTime: '',
  resultTime: '',
  overallPlace: '',
  categoryPlace: '',
  categoryName: '',
  resultsUrl: '',
})

export function defaultFormValues(options: { date: string; runTypeId: number }): RunFormValues {
  return {
    date: options.date,
    startTime: '',
    timeZoneId: '',
    status: 'Planned',
    runTypeId: options.runTypeId,
    title: '',
    notes: '',
    distanceKm: '',
    duration: '',
    elevationGain: '',
    elevationLoss: '',
    avgHeartRate: '',
    maxHeartRate: '',
    rpe: '',
    gear: '',
    weather: '',
    surface: '',
    race: null,
    rating: { stars: 0, comment: '' },
  }
}

const text = (value: string | null | undefined): string => value ?? ''
const intText = (value: number | null | undefined): string => (value == null ? '' : String(value))
const metresText = (value: number | null | undefined): string => (value == null ? '' : formatMetres(value))
const kmText = (value: number | null | undefined): string => (value == null ? '' : formatKmInput(value))
const durationText = (value: number | null | undefined): string => (value == null ? '' : formatDuration(value))

/** Metres as typed in a field: at most one decimal, '.' separator. */
export function formatMetres(value: number): string {
  return String(Number(value.toFixed(1)))
}

/** Parses a non-negative decimal ("12", "12,5", "12.5"); null when blank or invalid. */
export function parseNonNegative(input: string): number | null {
  const normalized = input.trim().replace(',', '.')
  if (!/^\d+(\.\d+)?$/.test(normalized)) return null
  const value = Number(normalized)
  return Number.isFinite(value) ? value : null
}

export function parseWholeNumber(input: string): number | null {
  const normalized = input.trim()
  return /^\d+$/.test(normalized) ? Number(normalized) : null
}

export function toFormValues(dto: RunDto): RunFormValues {
  return {
    date: dto.date,
    startTime: text(dto.startTime),
    // The zone is shown as "unchanged"; the run keeps its zone unless the user picks another (PUT omits it).
    timeZoneId: '',
    status: dto.status,
    runTypeId: dto.runTypeId,
    title: text(dto.title),
    notes: text(dto.notes),
    distanceKm: kmText(dto.distanceMeters),
    duration: durationText(dto.durationSeconds),
    elevationGain: metresText(dto.elevationGainMeters),
    elevationLoss: metresText(dto.elevationLossMeters),
    avgHeartRate: intText(dto.avgHeartRate),
    maxHeartRate: intText(dto.maxHeartRate),
    rpe: intText(dto.rpe),
    gear: text(dto.gear),
    weather: text(dto.weather),
    surface: dto.surface ?? '',
    race: dto.race
      ? {
          name: dto.race.name,
          distanceCategory: dto.race.distanceCategory,
          officialDistanceKm: kmText(dto.race.officialDistanceMeters),
          bibNumber: text(dto.race.bibNumber),
          targetTime: durationText(dto.race.targetTimeSeconds),
          resultTime: durationText(dto.race.resultTimeSeconds),
          overallPlace: intText(dto.race.overallPlace),
          categoryPlace: intText(dto.race.categoryPlace),
          categoryName: text(dto.race.categoryName),
          resultsUrl: text(dto.race.resultsUrl),
        }
      : null,
    rating: { stars: dto.rating?.stars ?? 0, comment: text(dto.rating?.comment) },
  }
}

const orNull = (value: string): string | null => (value.trim() === '' ? null : value.trim())

/**
 * A number the user did not touch keeps the loaded value exactly (a GPX-derived 10 234.567 m is shown as 10.235 km;
 * writing 10 235 back would silently change it). Otherwise the typed text is converted.
 */
function numberOrBase(
  shown: string,
  baseValue: number | null | undefined,
  formatForField: (value: number) => string,
  convert: (shown: string) => number | null,
): number | null {
  if (shown.trim() === '') return null
  if (baseValue != null && formatForField(baseValue) === shown.trim()) return baseValue
  return convert(shown)
}

const kmToMetersOrNull = (shown: string): number | null => {
  const km = parseKm(shown)
  return km === null ? null : kmToMeters(km)
}

/**
 * `base` is the loaded run (PUT) and supplies `version`, `location` and the untouched numbers; without it the
 * request is a POST body. Read-only members (`id`, `startUtc`, `tracks`, `createdAt`, `updatedAt`) are never sent.
 */
export function toWriteRequest(values: RunFormValues, base?: RunDto): RunWriteRequest {
  const race = values.race
  const stars = values.rating.stars
  return withAllWriteMembers({
    date: values.date,
    startTime: orNull(values.startTime),
    ...(values.timeZoneId ? { timeZoneId: values.timeZoneId } : {}),
    status: values.status,
    runTypeId: values.runTypeId,
    title: orNull(values.title),
    notes: orNull(values.notes),
    location: base?.location ?? null,
    distanceMeters: numberOrBase(values.distanceKm, base?.distanceMeters, formatKmInput, kmToMetersOrNull),
    durationSeconds: numberOrBase(values.duration, base?.durationSeconds, formatDuration, parseDuration),
    elevationGainMeters: numberOrBase(values.elevationGain, base?.elevationGainMeters, formatMetres, parseNonNegative),
    elevationLossMeters: numberOrBase(values.elevationLoss, base?.elevationLossMeters, formatMetres, parseNonNegative),
    avgHeartRate: values.avgHeartRate.trim() === '' ? null : parseWholeNumber(values.avgHeartRate),
    maxHeartRate: values.maxHeartRate.trim() === '' ? null : parseWholeNumber(values.maxHeartRate),
    rpe: values.rpe.trim() === '' ? null : parseWholeNumber(values.rpe),
    gear: orNull(values.gear),
    weather: orNull(values.weather),
    surface: values.surface === '' ? null : (values.surface satisfies SurfaceValue),
    race: race
      ? {
          name: race.name.trim(),
          distanceCategory: race.distanceCategory,
          officialDistanceMeters: numberOrBase(
            race.officialDistanceKm,
            base?.race?.officialDistanceMeters,
            formatKmInput,
            kmToMetersOrNull,
          ),
          bibNumber: orNull(race.bibNumber),
          targetTimeSeconds: numberOrBase(
            race.targetTime,
            base?.race?.targetTimeSeconds,
            formatDuration,
            parseDuration,
          ),
          resultTimeSeconds: numberOrBase(
            race.resultTime,
            base?.race?.resultTimeSeconds,
            formatDuration,
            parseDuration,
          ),
          overallPlace: race.overallPlace.trim() === '' ? null : parseWholeNumber(race.overallPlace),
          categoryPlace: race.categoryPlace.trim() === '' ? null : parseWholeNumber(race.categoryPlace),
          categoryName: orNull(race.categoryName),
          resultsUrl: orNull(race.resultsUrl),
        }
      : null,
    rating: stars > 0 ? { stars, comment: orNull(values.rating.comment) } : null,
    ...(base ? { version: base.version } : {}),
  })
}

/**
 * Compile-time guard for the PUT full replace: every member of `RunWriteRequest` (optional ones included) must be
 * named in the object literal, so a field added to the API cannot be silently dropped. A missing member makes the
 * argument fail to type-check with the member's name in the message.
 */
export function withAllWriteMembers<T extends object>(
  request: T & Record<Exclude<keyof RunWriteRequest, keyof T>, never>,
): T {
  return request
}

export const METRIC_FIELDS = [
  'distanceKm',
  'duration',
  'elevationGain',
  'elevationLoss',
  'avgHeartRate',
  'maxHeartRate',
  'rpe',
] as const

/** Moving back to Planned drops what only a completed run can have (rating, race result and places). */
export function applyStatus(values: RunFormValues, status: RunFormValues['status']): RunFormValues {
  if (status === 'Completed') return { ...values, status }
  return {
    ...values,
    status,
    rating: { stars: 0, comment: '' },
    race: values.race ? { ...values.race, resultTime: '', overallPlace: '', categoryPlace: '' } : null,
  }
}

/** Picking a run type: race details exist only for race types, a rest day carries no metrics, rating or race. */
export function applyRunType(values: RunFormValues, runTypeId: number, rules: RunTypeRules): RunFormValues {
  let next: RunFormValues = { ...values, runTypeId }
  next = { ...next, race: rules.isRace ? (next.race ?? emptyRace()) : null }
  if (rules.isRestDay) {
    next = { ...next, rating: { stars: 0, comment: '' }, race: null }
    for (const field of METRIC_FIELDS) next = { ...next, [field]: '' }
  }
  return next
}
