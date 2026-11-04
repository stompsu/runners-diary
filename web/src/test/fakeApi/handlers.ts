import type { components, operations } from '../../api/generated/schema.d.ts'
import {
  addDays,
  compare,
  firstOfMonth,
  isValid,
  isoWeek,
  isoWeekday,
  lastOfMonth,
  parse,
  startOfIsoWeek,
} from '../../lib/dates/plainDate.ts'
import { RUN_TYPES } from './seed.ts'
import type { StoredTrack } from './tracks.ts'

type S = components['schemas']
export type RunDto = S['RunDto']
export type RunWriteRequest = S['RunWriteRequest']
export type MeResponse = S['MeResponse']
export type TotalsDto = S['TotalsDto']

export type ClientConfigResponse = S['ClientConfigResponse']

/** Compile-time proof that each handled operation exists in the generated contract. */
export type HandledOperations = Pick<
  operations,
  | 'getApiHealth'
  | 'getClientConfig'
  | 'createGuestSession'
  | 'getMe'
  | 'updateMe'
  | 'listRunTypes'
  | 'getCalendarMonth'
  | 'listRuns'
  | 'getRun'
  | 'createRun'
  | 'updateRun'
  | 'deleteRun'
  | 'uploadTrack'
  | 'getTrack'
  | 'getTrackGeoJson'
  | 'downloadTrackGpx'
  | 'deleteTrack'
  | 'searchPlaces'
  | 'getInsightsReport'
>

export interface Account {
  id: string
  kind: 'User' | 'Guest'
  displayName: string | null
  timeZoneId: string
  timeZoneIsDefault: boolean
  unitSystem: 'Metric' | 'Imperial'
  expiresAtMs: number | null
  runs: Map<string, RunDto>
  /** Tracks of all the account's runs by track id (each knows its run). */
  tracks: Map<string, StoredTrack>
}

export interface Limits {
  maxRunsPerAccount: number
  maxRunsPerDate: number
  /** Tracks per account (the real API: guests 16, users 2 000). Default 16 so the limit path is easy to reach. */
  maxTracksPerAccount: number
  /** Largest accepted GPX file in bytes: guests / users (the real API: 5 MiB / 20 MiB). */
  maxGuestFileBytes: number
  maxUserFileBytes: number
}

export const MAX_DATE = '2100-12-31'
export const MIN_DATE = '1950-01-01'

// ---------- problem helpers ----------

const RFC = {
  400: 'https://tools.ietf.org/html/rfc9110#section-15.5.1',
  401: 'https://tools.ietf.org/html/rfc9110#section-15.5.2',
  404: 'https://tools.ietf.org/html/rfc9110#section-15.5.5',
  413: 'https://tools.ietf.org/html/rfc9110#section-15.5.14',
} as const

export function json(body: unknown, status = 200, headers: Record<string, string> = {}): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json', ...headers } })
}

export function problem(
  status: number,
  title: string,
  extra: {
    type?: string
    errors?: Record<string, string[]>
    headers?: Record<string, string>
    /** Extra problem members (the `invalid-gpx` problem carries `code`). */
    members?: Record<string, unknown>
  } = {},
): Response {
  const type = extra.type ?? RFC[status as keyof typeof RFC]
  const body = {
    ...(type ? { type } : {}),
    title,
    status,
    ...(extra.errors ? { errors: extra.errors } : {}),
    ...extra.members,
  }
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/problem+json', ...extra.headers },
  })
}

export const unauthorized = () => problem(401, 'Unauthorized', { headers: { 'WWW-Authenticate': 'Bearer' } })
export const notFound = () => problem(404, 'Not Found')
export const validation = (errors: Record<string, string[]>) =>
  problem(400, 'One or more validation errors occurred.', { errors })

// ---------- time zones ----------

export function isValidZone(zone: string): boolean {
  if (zone.length > 64) return false
  if (!/^(UTC|GMT|[A-Za-z][A-Za-z0-9_+-]*(\/[A-Za-z0-9_+-]+)+)$/.test(zone)) return false
  try {
    return new Intl.DateTimeFormat('en', { timeZone: zone }).resolvedOptions().timeZone !== ''
  } catch {
    return false
  }
}

/** Local wall-clock (date + HH:mm) in `zone` -> UTC ISO string. */
export function toStartUtc(date: string, time: string, zone: string): string {
  const d = parse(date)!
  const [hh, mm] = time.split(':').map(Number) as [number, number]
  const asUtc = Date.UTC(d.year, d.month - 1, d.day, hh, mm)
  const offsetAt = (ms: number) => {
    const parts = new Intl.DateTimeFormat('en-US', {
      timeZone: zone,
      hourCycle: 'h23',
      year: 'numeric',
      month: 'numeric',
      day: 'numeric',
      hour: 'numeric',
      minute: 'numeric',
    }).formatToParts(ms)
    const get = (type: string) => Number(parts.find((p) => p.type === type)!.value)
    return Date.UTC(get('year'), get('month') - 1, get('day'), get('hour'), get('minute')) - ms
  }
  const first = asUtc - offsetAt(asUtc)
  return new Date(asUtc - offsetAt(first)).toISOString()
}

// ---------- validation (a subset of RunRules that the UI relies on) ----------

const WRITE_KEYS = new Set([
  'date',
  'startTime',
  'timeZoneId',
  'status',
  'runTypeId',
  'title',
  'notes',
  'location',
  'distanceMeters',
  'durationSeconds',
  'elevationGainMeters',
  'elevationLossMeters',
  'avgHeartRate',
  'maxHeartRate',
  'rpe',
  'gear',
  'weather',
  'surface',
  'race',
  'rating',
  'version',
])
const RACE_KEYS = new Set([
  'name',
  'distanceCategory',
  'officialDistanceMeters',
  'bibNumber',
  'targetTimeSeconds',
  'resultTimeSeconds',
  'overallPlace',
  'categoryPlace',
  'categoryName',
  'resultsUrl',
])
const SURFACES = ['Road', 'Trail', 'Track', 'Treadmill', 'Mixed']
const CATEGORIES = ['FiveK', 'TenK', 'HalfMarathon', 'Marathon', 'Other']

const isObject = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null && !Array.isArray(v)

export function validateWrite(
  raw: unknown,
  mode: 'create' | 'update',
): { errors: Record<string, string[]> } | { value: RunWriteRequest } {
  const errors: Record<string, string[]> = {}
  const add = (key: string, message: string) => (errors[key] ??= []).push(message)
  if (!isObject(raw)) return { errors: { body: ['The request body is invalid.'] } }

  for (const key of Object.keys(raw)) if (!WRITE_KEYS.has(key)) add('body', 'The request body is invalid.')
  if (isObject(raw.race))
    for (const key of Object.keys(raw.race)) if (!RACE_KEYS.has(key)) add('body', 'The request body is invalid.')
  if (errors.body) return { errors }

  const r = raw as RunWriteRequest
  if (typeof r.date !== 'string' || !isValid(r.date)) add('date', 'Date must be a valid date (yyyy-MM-dd).')
  else if (compare(r.date, MIN_DATE) < 0 || compare(r.date, MAX_DATE) > 0)
    add('date', 'Date must be between 1950-01-01 and 2100-12-31.')
  if (r.startTime != null && !/^([01]\d|2[0-3]):[0-5]\d$/.test(r.startTime))
    add('startTime', 'Start time must be HH:mm.')
  if (r.timeZoneId != null && !isValidZone(r.timeZoneId)) add('timeZoneId', 'Time zone must be a valid IANA id.')
  if (r.status !== 'Planned' && r.status !== 'Completed') add('status', 'Status must be Planned or Completed.')
  const type = RUN_TYPES.find((t) => t.id === r.runTypeId)
  if (!type) add('runTypeId', 'Run type is unknown.')
  if (r.title != null && r.title.length > 120) add('title', 'Title must be at most 120 characters.')
  if (r.notes != null && r.notes.length > 4000) add('notes', 'Notes must be at most 4000 characters.')
  if (r.gear != null && r.gear.length > 100) add('gear', 'Gear must be at most 100 characters.')
  if (r.weather != null && r.weather.length > 200) add('weather', 'Weather must be at most 200 characters.')
  if (r.surface != null && !SURFACES.includes(r.surface)) add('surface', 'Surface is invalid.')
  if (r.distanceMeters != null && !(r.distanceMeters > 0)) add('distanceMeters', 'Distance must be greater than 0.')
  if (r.durationSeconds != null && !(r.durationSeconds > 0)) add('durationSeconds', 'Duration must be greater than 0.')
  if (r.elevationGainMeters != null && r.elevationGainMeters < 0)
    add('elevationGainMeters', 'Elevation gain must not be negative.')
  if (r.elevationLossMeters != null && r.elevationLossMeters < 0)
    add('elevationLossMeters', 'Elevation loss must not be negative.')
  if (r.rpe != null && (r.rpe < 1 || r.rpe > 10)) add('rpe', 'RPE must be between 1 and 10.')
  if (r.avgHeartRate != null && (r.avgHeartRate < 30 || r.avgHeartRate > 250))
    add('avgHeartRate', 'Heart rate must be between 30 and 250.')
  if (r.maxHeartRate != null && (r.maxHeartRate < 30 || r.maxHeartRate > 250))
    add('maxHeartRate', 'Heart rate must be between 30 and 250.')
  if (r.avgHeartRate != null && r.maxHeartRate != null && r.maxHeartRate < r.avgHeartRate) {
    add('maxHeartRate', 'Max heart rate must be at least the average.')
  }
  if (r.location) {
    if (r.location.latitude < -90 || r.location.latitude > 90)
      add('location.latitude', 'Latitude must be between -90 and 90.')
    if (r.location.longitude < -180 || r.location.longitude > 180)
      add('location.longitude', 'Longitude must be between -180 and 180.')
  }
  if (r.rating) {
    if (r.status !== 'Completed') add('rating', 'A rating is only allowed for completed runs.')
    if (!(r.rating.stars >= 1 && r.rating.stars <= 5)) add('rating.stars', 'Stars must be between 1 and 5.')
    if (r.rating.comment != null && r.rating.comment.length > 500)
      add('rating.comment', 'Comment must be at most 500 characters.')
  }
  if (r.race) {
    if (type && type.category !== 'Race') add('race', 'Race details are only allowed for race run types.')
    if (!r.race.name) add('race.name', 'Race name is required.')
    else if (r.race.name.length > 150) add('race.name', 'Race name must be at most 150 characters.')
    if (!CATEGORIES.includes(r.race.distanceCategory)) add('race.distanceCategory', 'Distance category is invalid.')
    if (r.status !== 'Completed') {
      if (r.race.resultTimeSeconds != null)
        add('race.resultTimeSeconds', 'A result is only allowed for completed runs.')
      if (r.race.overallPlace != null) add('race.overallPlace', 'A place is only allowed for completed runs.')
      if (r.race.categoryPlace != null) add('race.categoryPlace', 'A place is only allowed for completed runs.')
    }
    if (r.race.resultsUrl != null && !/^https?:\/\/\S+$/i.test(r.race.resultsUrl))
      add('race.resultsUrl', 'Results URL must be an absolute http(s) URL.')
  }
  if (type?.isRestDay) {
    for (const key of [
      'distanceMeters',
      'durationSeconds',
      'elevationGainMeters',
      'elevationLossMeters',
      'avgHeartRate',
      'maxHeartRate',
      'rpe',
    ] as const) {
      if (r[key] != null) add(key, 'Rest days cannot have metrics.')
    }
    if (r.race) add('race', 'Rest days cannot have race details.')
    if (r.rating) add('rating', 'Rest days cannot have a rating.')
  }
  if (mode === 'update' && !r.version) add('version', 'Version is required.')

  return Object.keys(errors).length ? { errors } : { value: r }
}

// ---------- run building ----------

export function buildRun(
  id: string,
  write: RunWriteRequest,
  account: Account,
  nowIso: string,
  version: number,
  previous?: RunDto,
): RunDto {
  const timeZoneId = write.timeZoneId ?? previous?.timeZoneId ?? account.timeZoneId
  const startTime = write.startTime ?? null
  return {
    id,
    date: write.date,
    startTime,
    timeZoneId,
    startUtc: startTime ? toStartUtc(write.date, startTime, timeZoneId) : null,
    status: write.status,
    runTypeId: write.runTypeId,
    title: write.title ?? null,
    notes: write.notes ?? null,
    location: write.location ?? null,
    distanceMeters: write.distanceMeters ?? null,
    durationSeconds: write.durationSeconds ?? null,
    elevationGainMeters: write.elevationGainMeters ?? null,
    elevationLossMeters: write.elevationLossMeters ?? null,
    avgHeartRate: write.avgHeartRate ?? null,
    maxHeartRate: write.maxHeartRate ?? null,
    rpe: write.rpe ?? null,
    gear: write.gear ?? null,
    weather: write.weather ?? null,
    surface: write.surface ?? null,
    race: write.race ?? null,
    rating: write.rating ?? null,
    version: encodeVersion(version),
    tracks: previous?.tracks ?? [],
    createdAt: previous?.createdAt ?? nowIso,
    updatedAt: nowIso,
  }
}

export const encodeVersion = (n: number): string => btoa(String(n).padStart(8, '0'))

export function sortRuns(runs: RunDto[]): RunDto[] {
  return [...runs].sort(
    (a, b) =>
      compare(a.date, b.date) ||
      (a.startUtc && b.startUtc ? compare(a.startUtc, b.startUtc) : a.startUtc ? -1 : b.startUtc ? 1 : 0) ||
      compare(a.createdAt, b.createdAt),
  )
}

// ---------- calendar ----------

const emptyTotals = (): TotalsDto => ({ runCount: 0, distanceMeters: 0, durationSeconds: 0, elevationGainMeters: 0 })

export function calendarMonth(year: number, month: number, runs: RunDto[]): S['CalendarMonthResponse'] {
  const gridStart = startOfIsoWeek(firstOfMonth(year, month))
  const last = lastOfMonth(year, month)
  const gridEnd = addDays(last, 7 - isoWeekday(last))
  const inGrid = sortRuns(runs.filter((r) => compare(r.date, gridStart) >= 0 && compare(r.date, gridEnd) <= 0))
  const counts = new Set(RUN_TYPES.filter((t) => t.countsTowardRunningDistance).map((t) => t.id))

  const weeks: S['CalendarWeekDto'][] = []
  for (let start = gridStart; compare(start, gridEnd) <= 0; start = addDays(start, 7)) {
    const end = addDays(start, 6)
    const completed = emptyTotals()
    const planned = emptyTotals()
    for (const run of inGrid) {
      if (compare(run.date, start) < 0 || compare(run.date, end) > 0 || !counts.has(run.runTypeId)) continue
      const totals = run.status === 'Completed' ? completed : planned
      totals.runCount += 1
      totals.distanceMeters += run.distanceMeters ?? 0
      totals.durationSeconds += run.durationSeconds ?? 0
      totals.elevationGainMeters += run.elevationGainMeters ?? 0
    }
    weeks.push({ ...isoWeek(start), start, end, completed, planned })
  }
  return { year, month, gridStart, gridEnd, runs: inGrid, weeks }
}
