import { z } from 'zod'
import { addDays, diffDays, isValid } from '@/lib/dates/plainDate'
import { formatDuration, parseDuration } from '@/lib/format/duration'
import i18n from '@/i18n'
import { formatKmInput, kmToMeters, parseKm } from '@/lib/format/units'
import type { GoalDto, GoalStatus, GoalType, GoalWriteRequest, Surface } from './goalTypes'

// Same CSP reason as the run form schema (ADR 0029): zod's JIT needs 'unsafe-eval'.
z.config({ jitless: true })

export const PRESET_IDS = ['fiveK', 'tenK', 'half', 'marathon', 'trail25', 'trail44', 'custom'] as const
export type PresetId = (typeof PRESET_IDS)[number]

export interface Preset {
  distanceMeters: number
  elevationGainMeters: number | null
  surface: Surface | null
}

/** Plan §5 presets; `custom` changes nothing. */
export const PRESETS: Record<Exclude<PresetId, 'custom'>, Preset> = {
  fiveK: { distanceMeters: 5000, elevationGainMeters: null, surface: null },
  tenK: { distanceMeters: 10_000, elevationGainMeters: null, surface: null },
  half: { distanceMeters: 21_098, elevationGainMeters: null, surface: null },
  marathon: { distanceMeters: 42_195, elevationGainMeters: null, surface: null },
  trail25: { distanceMeters: 25_000, elevationGainMeters: 800, surface: 'Trail' },
  trail44: { distanceMeters: 44_000, elevationGainMeters: 1000, surface: 'Trail' },
}

/** The preset that matches a goal's numbers, else `custom` (edit form). */
export function presetOf(distanceMeters: number, elevation: number | null, surface: Surface | null): PresetId {
  const hit = (Object.entries(PRESETS) as [Exclude<PresetId, 'custom'>, Preset][]).find(
    ([, p]) => p.distanceMeters === distanceMeters && p.elevationGainMeters === elevation && p.surface === surface,
  )
  return hit ? hit[0] : 'custom'
}

export interface GoalFormValues {
  preset: PresetId
  type: GoalType
  title: string
  targetDate: string
  distanceKm: string
  elevationGain: string
  surface: '' | Surface
  targetTime: string
  eventName: string
  raceRunId: string
  notes: string
  status: GoalStatus
}

export function defaultGoalValues(today: string): GoalFormValues {
  return {
    preset: 'tenK',
    type: 'Performance',
    title: '10K',
    targetDate: addDays(today, 70),
    distanceKm: '10',
    elevationGain: '',
    surface: '',
    targetTime: '',
    eventName: '',
    raceRunId: '',
    notes: '',
    status: 'Active',
  }
}

export function toGoalFormValues(goal: GoalDto): GoalFormValues {
  return {
    preset: presetOf(goal.distanceMeters, goal.elevationGainMeters, goal.surface),
    type: goal.type,
    title: goal.title,
    targetDate: goal.targetDate,
    distanceKm: formatKmInput(goal.distanceMeters, i18n.language),
    elevationGain: goal.elevationGainMeters == null ? '' : String(goal.elevationGainMeters),
    surface: goal.surface ?? '',
    targetTime: goal.targetDurationSeconds == null ? '' : formatDuration(goal.targetDurationSeconds),
    eventName: goal.eventName ?? '',
    raceRunId: goal.raceRunId ?? '',
    notes: goal.notes ?? '',
    status: goal.status,
  }
}

const blank = (value: string) => value.trim() === ''

/** Parsed numbers of the form, `null` where a field is empty or not a number. */
export function parseGoalNumbers(values: Pick<GoalFormValues, 'distanceKm' | 'elevationGain' | 'targetTime'>) {
  const km = parseKm(values.distanceKm)
  const elevation = blank(values.elevationGain) ? null : Number(values.elevationGain.trim().replace(',', '.'))
  return {
    distanceMeters: km === null ? null : kmToMeters(km),
    elevationGainMeters: elevation !== null && Number.isFinite(elevation) ? elevation : null,
    targetDurationSeconds: blank(values.targetTime) ? null : parseDuration(values.targetTime),
  }
}

/**
 * Client mirror of the server goal rules (ADR 0039 §2): instant feedback only, the server stays authoritative.
 * Messages are `goals:form.validation.*` keys. `previousTargetDate` is the unchanged date of an edited goal, which is
 * not re-checked against the 7-day minimum (the real rule).
 */
export function goalSchema(today: string, previousTargetDate?: string) {
  return z
    .object({
      preset: z.enum(PRESET_IDS),
      type: z.enum(['Race', 'Performance', 'Distance']),
      title: z.string(),
      targetDate: z.string(),
      distanceKm: z.string(),
      elevationGain: z.string(),
      surface: z.enum(['', 'Road', 'Trail', 'Track', 'Treadmill', 'Mixed']),
      targetTime: z.string(),
      eventName: z.string(),
      raceRunId: z.string(),
      notes: z.string(),
      status: z.enum(['Active', 'Achieved', 'Archived']),
    })
    .superRefine((values, ctx) => {
      const issue = (path: string, message: string) => ctx.addIssue({ code: 'custom', path: [path], message })
      if (blank(values.title)) issue('title', 'titleRequired')
      else if (values.title.length > 120) issue('title', 'titleTooLong')

      if (!isValid(values.targetDate)) issue('targetDate', 'dateRequired')
      else if (values.targetDate !== previousTargetDate) {
        const days = diffDays(today, values.targetDate)
        if (days < 7) issue('targetDate', 'dateTooSoon')
        else if (days > 730) issue('targetDate', 'dateTooFar')
      }

      const numbers = parseGoalNumbers(values)
      const { distanceMeters, targetDurationSeconds } = numbers
      if (distanceMeters === null || distanceMeters < 1000 || distanceMeters > 250_000) issue('distanceKm', 'distance')
      if (!blank(values.elevationGain)) {
        const e = numbers.elevationGainMeters
        if (e === null || e < 0 || e > 10_000) issue('elevationGain', 'elevation')
      }

      if (values.type === 'Distance') return
      if (blank(values.targetTime)) {
        if (values.type === 'Performance') issue('targetTime', 'targetTimeRequired')
      } else if (targetDurationSeconds === null || targetDurationSeconds < 1) {
        issue('targetTime', 'targetTime')
      } else if (distanceMeters !== null && distanceMeters >= 1000) {
        const pace = targetDurationSeconds / (distanceMeters / 1000)
        if (pace < 150 || pace > 900) issue('targetTime', 'targetPace')
      }

      if (values.type === 'Race') {
        if (blank(values.eventName)) issue('eventName', 'eventRequired')
        else if (values.eventName.length > 150) issue('eventName', 'eventTooLong')
      }
      if (values.notes.length > 1000) issue('notes', 'notesTooLong')
    })
}

const orNull = (value: string) => (blank(value) ? null : value.trim())

/** Display units in, SI out. `version` is set for an update. */
export function toGoalRequest(values: GoalFormValues, version?: string): GoalWriteRequest {
  const { distanceMeters, elevationGainMeters, targetDurationSeconds } = parseGoalNumbers(values)
  return {
    type: values.type,
    title: values.title.trim(),
    targetDate: values.targetDate,
    distanceMeters: distanceMeters ?? 0,
    elevationGainMeters,
    targetDurationSeconds: values.type === 'Distance' ? null : targetDurationSeconds,
    surface: values.surface === '' ? null : values.surface,
    eventName: values.type === 'Race' ? orNull(values.eventName) : null,
    raceRunId: values.raceRunId === '' ? null : values.raceRunId,
    notes: orNull(values.notes),
    status: values.status,
    ...(version ? { version } : {}),
  }
}

/** Wire member -> form field, where the form uses display units. */
export const GOAL_FIELD_KEY_MAP: Readonly<Record<string, string>> = {
  distanceMeters: 'distanceKm',
  elevationGainMeters: 'elevationGain',
  targetDurationSeconds: 'targetTime',
}

export const GOAL_FORM_FIELDS: ReadonlySet<string> = new Set([
  'type',
  'title',
  'targetDate',
  'distanceKm',
  'elevationGain',
  'surface',
  'targetTime',
  'eventName',
  'raceRunId',
  'notes',
  'status',
])
