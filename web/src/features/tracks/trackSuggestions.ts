import { formatDuration } from '@/lib/format/duration'
import { formatDistance, formatElevation, formatKmInput } from '@/lib/format/units'
import { formatMetres } from '@/features/runs/runFormMapping'
import type { FormPatch, RunDto, RunTypeRules } from '@/features/runs/runFormTypes'
import type { TrackDto } from './trackTypes'

/**
 * What a GPX track can propose for a run (ADR 0023 §3, plan §3.4). Pure: `run` and `track` in, rows out. Rows whose
 * values are already equal are omitted. Empty run fields are pre-checked, differing ones are not; a different date is
 * additionally flagged. The numbers go through the display format, so the run ends up with 1 m / 1 s / 0.1 m precision.
 */
export type SuggestionId =
  'complete' | 'startTime' | 'distance' | 'duration' | 'elevationGain' | 'elevationLoss' | 'location'

export interface DurationOption {
  key: 'elapsed' | 'moving'
  seconds: number
  text: string
  patch: FormPatch
}

export interface Suggestion {
  id: SuggestionId
  /** The run's value as shown to the user; null = the field is empty. */
  current: string | null
  /** The track's value as shown to the user. */
  proposed: string
  checked: boolean
  /** The track was recorded on another day than the run (`startTime` row only). */
  dateMismatch?: { trackDate: string; runDate: string }
  /** What Apply puts into the form when this row is selected. */
  patch: FormPatch
  /** `duration` only: elapsed (default) or moving time. Selecting an option replaces `patch`. */
  durationOptions?: DurationOption[]
}

const SAME_PLACE_DEGREES = 0.00001

const coordinates = (latitude: number, longitude: number) => `${latitude.toFixed(5)}, ${longitude.toFixed(5)}`
const blank = (value: string | null | undefined) => value == null || value === ''

export function trackSuggestions(run: RunDto, track: TrackDto, rules: RunTypeRules, locale = 'en'): Suggestion[] {
  const summary = track.summary
  if (!summary || rules.isRestDay) return []
  const recorded = track.kind === 'RecordedTrack'
  const rows: Suggestion[] = []

  if (recorded && run.status === 'Planned') {
    rows.push({
      id: 'complete',
      current: null,
      proposed: '',
      checked: true,
      patch: { status: 'Completed' },
    })
  }

  const local = summary.startLocal
  if (recorded && local) {
    const sameDate = local.date === run.date
    const sameTime = (run.startTime ?? '') === local.time
    if (!(sameDate && sameTime)) {
      rows.push({
        id: 'startTime',
        current: blank(run.startTime) ? null : run.startTime!,
        proposed: `${local.time} (${local.timeZoneId})`,
        checked: sameDate && blank(run.startTime),
        dateMismatch: sameDate ? undefined : { trackDate: local.date, runDate: run.date },
        patch: { date: local.date, startTime: local.time },
      })
    }
  }

  const distance = formatKmInput(summary.distanceMeters)
  if (run.distanceMeters == null || formatKmInput(run.distanceMeters) !== distance) {
    rows.push({
      id: 'distance',
      current: run.distanceMeters == null ? null : formatDistance(run.distanceMeters, locale, 3),
      proposed: formatDistance(summary.distanceMeters, locale, 3),
      checked: run.distanceMeters == null,
      patch: { distanceKm: distance },
    })
  }

  if (recorded && summary.elapsedSeconds != null && summary.elapsedSeconds >= 1) {
    const elapsed = summary.elapsedSeconds
    const moving = summary.movingSeconds
    const option = (key: DurationOption['key'], seconds: number): DurationOption => ({
      key,
      seconds,
      text: formatDuration(seconds),
      patch: { duration: formatDuration(seconds) },
    })
    const options = [option('elapsed', elapsed)]
    if (moving != null && moving >= 1 && formatDuration(moving) !== formatDuration(elapsed)) {
      options.push(option('moving', moving))
    }
    // Equal to the elapsed time (the default choice): nothing to propose. A run that equals only the moving time
    // still gets the row, so the user can see both.
    const current = run.durationSeconds
    if (current == null || formatDuration(current) !== formatDuration(elapsed)) {
      rows.push({
        id: 'duration',
        current: current == null ? null : formatDuration(current),
        proposed: formatDuration(elapsed),
        checked: current == null,
        patch: options[0]!.patch,
        durationOptions: options.length > 1 ? options : undefined,
      })
    }
  }

  const elevation = (
    id: 'elevationGain' | 'elevationLoss',
    proposed: number | null,
    current: number | null | undefined,
  ) => {
    if (proposed == null) return
    if (current != null && formatMetres(current) === formatMetres(proposed)) return
    rows.push({
      id,
      current: current == null ? null : formatElevation(current, locale),
      proposed: formatElevation(proposed, locale),
      checked: current == null,
      patch: { [id]: formatMetres(proposed) },
    })
  }
  elevation('elevationGain', summary.elevationGainMeters, run.elevationGainMeters)
  elevation('elevationLoss', summary.elevationLossMeters, run.elevationLossMeters)

  const place = run.location
  const samePlace =
    place != null &&
    Math.abs(place.latitude - summary.start.latitude) < SAME_PLACE_DEGREES &&
    Math.abs(place.longitude - summary.start.longitude) < SAME_PLACE_DEGREES
  if (!samePlace) {
    rows.push({
      id: 'location',
      current: place
        ? place.name
          ? `${place.name} (${coordinates(place.latitude, place.longitude)})`
          : coordinates(place.latitude, place.longitude)
        : null,
      proposed: coordinates(summary.start.latitude, summary.start.longitude),
      checked: place == null,
      // An existing place name is kept: it describes where the user ran, the GPX only knows coordinates.
      patch: {
        location: { latitude: summary.start.latitude, longitude: summary.start.longitude, name: place?.name ?? '' },
      },
    })
  }

  return rows
}

/** The patch for the selected rows; `durations` holds the chosen duration option per row (default: the first). */
export function selectedPatch(
  rows: readonly Suggestion[],
  selected: ReadonlySet<SuggestionId>,
  durationChoice: DurationOption['key'] = 'elapsed',
): FormPatch {
  let patch: FormPatch = {}
  for (const row of rows) {
    if (!selected.has(row.id)) continue
    const chosen = row.durationOptions?.find((option) => option.key === durationChoice)
    patch = { ...patch, ...(chosen ? chosen.patch : row.patch) }
  }
  return patch
}
