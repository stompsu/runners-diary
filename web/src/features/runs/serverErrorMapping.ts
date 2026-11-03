import { mapFieldErrors, type FieldErrors } from '@/lib/api/problem'

/** Wire (camelCase JSON path) -> form field, where the form uses display units (ADR 0018 §3). */
export const RUN_FIELD_KEY_MAP: Readonly<Record<string, string>> = {
  distanceMeters: 'distanceKm',
  durationSeconds: 'duration',
  elevationGainMeters: 'elevationGain',
  elevationLossMeters: 'elevationLoss',
  'race.officialDistanceMeters': 'race.officialDistanceKm',
  'race.targetTimeSeconds': 'race.targetTime',
  'race.resultTimeSeconds': 'race.resultTime',
}

export const RUN_FORM_FIELDS: ReadonlySet<string> = new Set([
  'date',
  'startTime',
  'timeZoneId',
  'status',
  'runTypeId',
  'title',
  'notes',
  'distanceKm',
  'duration',
  'elevationGain',
  'elevationLoss',
  'avgHeartRate',
  'maxHeartRate',
  'rpe',
  'gear',
  'weather',
  'surface',
  'race.name',
  'race.distanceCategory',
  'race.officialDistanceKm',
  'race.bibNumber',
  'race.targetTime',
  'race.resultTime',
  'race.overallPlace',
  'race.categoryPlace',
  'race.categoryName',
  'race.resultsUrl',
  'rating.stars',
  'rating.comment',
])

export function mapRunFieldErrors(fieldErrors: FieldErrors) {
  return mapFieldErrors(fieldErrors, { keyMap: RUN_FIELD_KEY_MAP, isField: (name) => RUN_FORM_FIELDS.has(name) })
}
