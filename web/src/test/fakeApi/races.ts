import type { components } from '../../api/generated/schema.d.ts'
import { addDays, compare, diffDays, isValid } from '../../lib/dates/plainDate.ts'
import { json, notFound, problem, validation, type Account } from './handlers.ts'

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

// ---------- catalogue, proposals, details, state (M8 plan §4.1) ----------

export type RaceProposalDto = S['RaceProposalDto']
export type RaceDetailsDto = S['RaceDetailsDto']
/** A catalogue race: what the importer stores, without the caller's view of it. */
export type CatalogueRace = Omit<RaceDetailsDto, 'state' | 'linkedRunId' | 'isNew' | 'dateChanged'>

const id = (n: number) => `11111111-1111-4111-8111-${String(n).padStart(12, '0')}`
/** Stable ids of the demo catalogue, so tests and specs can address a race directly. */
export const DEMO_RACE_IDS = {
  night10k: id(1),
  silesianTrail: id(2),
  katowiceHalf: id(3),
  rybnik5k: id(4),
  tychy10k: id(5),
  warsaw10k: id(6),
  night10kDuplicate: id(7),
} as const

/**
 * A small synthetic catalogue relative to `today` (the real one is `seed/demo/races.json`): three Silesian races in
 * the next weeks (one with three distances), a date-changed one, a removed one, one outside Silesia and a duplicate.
 */
export function demoCatalogue(today: string, nowIso: string): CatalogueRace[] {
  const base = {
    countryCode: 'PL',
    source: 'running-life',
    timeZoneId: 'Europe/Warsaw',
    surface: null,
    elevationGainMeters: null,
    price: null,
    signupDeadline: null,
    organizerName: null,
    signupUrl: null,
    eventUrl: null,
    sourceUrl: null,
    removed: false,
    previousDate: null,
    canonicalId: null,
    lastSeenUtc: nowIso,
    updatedUtc: nowIso,
  } satisfies Partial<CatalogueRace>
  return [
    {
      ...base,
      raceId: DEMO_RACE_IDS.night10k,
      name: 'Demo Gliwice Night 10K',
      date: addDays(today, 9),
      startTime: '19:30',
      locationName: 'Gliwice',
      regionCode: 'PL-24',
      location: { latitude: 50.2945, longitude: 18.6714 },
      kind: 'Road',
      distancesMeters: [10000],
      surface: 'Road',
      elevationGainMeters: 45,
      price: { amount: 60, currency: 'PLN' },
      signupDeadline: addDays(today, 5),
      organizerName: 'Demo Gliwice Runners',
      signupUrl: 'https://example.org/gliwice-night/signup',
      eventUrl: 'https://example.org/gliwice-night',
      sourceUrl: 'https://running.life/events/demo-gliwice-night-10k',
    },
    {
      ...base,
      raceId: DEMO_RACE_IDS.silesianTrail,
      name: 'Demo Silesian Trail Weekend',
      date: addDays(today, 18),
      startTime: '09:00',
      locationName: 'Szczyrk',
      regionCode: 'PL-24',
      location: { latitude: 49.7188, longitude: 19.0286 },
      kind: 'Trail',
      distancesMeters: [10000, 21097.5, 42195],
      surface: 'Trail',
      elevationGainMeters: 1200,
      price: { amount: 120, currency: 'PLN' },
      signupDeadline: addDays(today, 14),
      signupUrl: 'https://example.org/trail/signup',
      sourceUrl: 'https://running.life/events/demo-silesian-trail-weekend',
    },
    {
      ...base,
      raceId: DEMO_RACE_IDS.katowiceHalf,
      name: 'Demo Katowice Half Marathon',
      date: addDays(today, 26),
      startTime: '10:00',
      locationName: 'Katowice',
      regionCode: 'PL-24',
      location: { latitude: 50.2649, longitude: 19.0238 },
      kind: 'Road',
      distancesMeters: [21097.5],
      signupUrl: 'https://example.org/katowice-half/signup',
      sourceUrl: 'https://running.life/events/demo-katowice-half',
    },
    {
      ...base,
      raceId: DEMO_RACE_IDS.rybnik5k,
      name: 'Demo Rybnik 5K',
      date: addDays(today, 38),
      previousDate: addDays(today, 31),
      startTime: null,
      locationName: 'Rybnik',
      regionCode: 'PL-24',
      location: { latitude: 50.0971, longitude: 18.5463 },
      kind: 'Road',
      distancesMeters: [5000],
      sourceUrl: 'https://running.life/events/demo-rybnik-5k',
    },
    {
      ...base,
      raceId: DEMO_RACE_IDS.tychy10k,
      name: 'Demo Tychy 10K',
      date: addDays(today, 30),
      startTime: '11:00',
      locationName: 'Tychy',
      regionCode: 'PL-24',
      location: null,
      kind: 'Road',
      distancesMeters: [10000],
      removed: true,
    },
    {
      ...base,
      raceId: DEMO_RACE_IDS.warsaw10k,
      name: 'Demo Warsaw 10K',
      date: addDays(today, 12),
      startTime: '09:30',
      locationName: 'Warsaw',
      regionCode: 'PL-14',
      location: { latitude: 52.2297, longitude: 21.0122 },
      kind: 'Road',
      distancesMeters: [10000],
      signupUrl: 'https://example.org/warsaw/signup',
    },
    {
      ...base,
      raceId: DEMO_RACE_IDS.night10kDuplicate,
      name: 'Demo Gliwice Night 10K (duplicate listing)',
      date: addDays(today, 9),
      startTime: '19:30',
      locationName: 'Gliwice',
      regionCode: 'PL-24',
      location: { latitude: 50.2945, longitude: 18.6714 },
      kind: 'Road',
      distancesMeters: [10000],
      canonicalId: DEMO_RACE_IDS.night10k,
    },
  ]
}

/** The marks a new guest starts with: the removed race and the date-changed one are Interested (so they show). */
export function demoRaceStates(catalogue: readonly CatalogueRace[]): Account['raceStates'] {
  const states: Account['raceStates'] = new Map()
  for (const race of catalogue) {
    if (race.raceId === DEMO_RACE_IDS.tychy10k)
      states.set(race.raceId, { state: 'Interested', dateWhenMarked: race.date })
    if (race.raceId === DEMO_RACE_IDS.rybnik5k) {
      states.set(race.raceId, { state: 'Interested', dateWhenMarked: race.previousDate ?? race.date })
    }
  }
  return states
}

export const FEATURE_UNAVAILABLE = 'urn:runnersdiary:problem:feature-unavailable'
export const RACE_ALREADY_LINKED = 'urn:runnersdiary:problem:race-already-linked'
export const PROPOSAL_STATE_LIMIT = 'urn:runnersdiary:problem:proposal-state-limit'
const GUEST_STATE_LIMIT = 200
const USER_STATE_LIMIT = 2000

const fold = (text: string) =>
  text
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .replace(/ł/g, 'l')
    .toLowerCase()

function haversineMeters(a: { latitude: number; longitude: number }, b: { latitude: number; longitude: number }) {
  const rad = (deg: number) => (deg * Math.PI) / 180
  const dLat = rad(b.latitude - a.latitude)
  const dLon = rad(b.longitude - a.longitude)
  const h = Math.sin(dLat / 2) ** 2 + Math.cos(rad(a.latitude)) * Math.cos(rad(b.latitude)) * Math.sin(dLon / 2) ** 2
  return 2 * 6_371_000 * Math.asin(Math.sqrt(h))
}

/** The proposal builder's filters (ADR 0037 §2): kinds, region or radius, distance overlap, keywords. */
export function matchesProfile(race: CatalogueRace, profile: RaceInterestsDto): boolean {
  const longest = Math.max(0, ...race.distancesMeters)
  const kindOk = profile.kinds.includes(race.kind) || (profile.kinds.includes('Ultra') && longest > 42195 * 1.01)
  if (!kindOk) return false

  const hasRegions = profile.regionCodes.length > 0
  const hasCenter = profile.center !== null && profile.radiusMeters !== null
  if (hasRegions || hasCenter) {
    const inRegion = hasRegions && race.regionCode !== null && profile.regionCodes.includes(race.regionCode)
    const inRadius =
      hasCenter && race.location !== null && haversineMeters(profile.center!, race.location) <= profile.radiusMeters!
    if (!inRegion && !inRadius) return false
  }

  const min = profile.minDistanceMeters
  const max = profile.maxDistanceMeters
  if ((min !== null || max !== null) && race.distancesMeters.length > 0) {
    const overlaps = race.distancesMeters.some((d) => (min === null || d >= min) && (max === null || d <= max))
    if (!overlaps) return false
  }

  if (profile.keywords) {
    const haystack = fold(`${race.name} ${race.locationName ?? ''}`)
    if (!profile.keywords.split(/\s+/).every((term) => haystack.includes(fold(term)))) return false
  }
  return true
}

function linkedRun(account: Account, raceId: string) {
  for (const run of account.runs.values()) if (run.catalogRaceId === raceId) return run
  return undefined
}

/** The caller's view of a race: derived SignedUp, the Interested / Dismissed mark, `dateChanged` and `isNew`. */
export function userView(race: CatalogueRace, account: Account) {
  const run = linkedRun(account, race.raceId)
  if (run) {
    return { state: 'SignedUp' as const, linkedRunId: run.id, isNew: false, dateChanged: run.date !== race.date }
  }
  const mark = account.raceStates.get(race.raceId)
  if (mark) {
    return {
      state: mark.state,
      linkedRunId: null,
      isNew: false,
      dateChanged: mark.state === 'Interested' && mark.dateWhenMarked !== race.date,
    }
  }
  const isNew = account.raceSeenUtc === null || race.updatedUtc > account.raceSeenUtc
  return { state: 'None' as const, linkedRunId: null, isNew, dateChanged: false }
}

const PROPOSAL_KEYS = [
  'raceId',
  'name',
  'date',
  'startTime',
  'locationName',
  'regionCode',
  'countryCode',
  'location',
  'kind',
  'distancesMeters',
  'source',
] as const

function toProposal(race: CatalogueRace, account: Account): RaceProposalDto {
  const out = { ...userView(race, account) } as Record<string, unknown>
  for (const key of PROPOSAL_KEYS) out[key] = race[key]
  return out as unknown as RaceProposalDto
}

/** Visible to the caller: not a duplicate; a removed race only while Interested or signed up. */
function visible(race: CatalogueRace, account: Account): boolean {
  if (race.canonicalId) return false
  if (!race.removed) return true
  return userView(race, account).state === 'Interested' || linkedRun(account, race.raceId) !== undefined
}

export interface RaceRouteContext {
  today: string
  catalogue: CatalogueRace[]
  catalogueUpdatedUtc: string | null
}

export function listProposals(ctx: RaceRouteContext, account: Account, url: URL): Response {
  const from = url.searchParams.get('from') ?? ''
  const to = url.searchParams.get('to') ?? ''
  const errors: Record<string, string[]> = {}
  const year = (value: string) => Number(value.slice(0, 4))
  if (!isValid(from) || year(from) < 1950 || year(from) > 2100) errors.from = ['From must be a date from 1950 to 2100.']
  if (!isValid(to) || year(to) < 1950 || year(to) > 2100) errors.to = ['To must be a date from 1950 to 2100.']
  const flag = url.searchParams.get('includeDismissed')
  if (flag !== null && flag !== 'true' && flag !== 'false') errors.includeDismissed = ['Must be true or false.']
  if (Object.keys(errors).length === 0) {
    if (compare(to, from) < 0) errors.to = ['To must not be before from.']
    else if (diffDays(from, to) > 92) errors.to = ['The range is at most 92 days.']
  }
  if (Object.keys(errors).length > 0) return validation(errors)

  const includeDismissed = flag === 'true'
  const profile = account.raceInterests
  const horizonEnd = addDays(ctx.today, profile?.horizonDays ?? 120)
  const effectiveFrom = compare(from, ctx.today) > 0 ? from : ctx.today
  const effectiveTo = compare(to, horizonEnd) < 0 ? to : horizonEnd

  const items: RaceProposalDto[] = []
  if (profile && compare(effectiveTo, effectiveFrom) >= 0) {
    for (const race of ctx.catalogue) {
      if (!visible(race, account)) continue
      const view = userView(race, account)
      const marked = view.state === 'SignedUp' || view.state === 'Interested'
      // Signed-up and Interested races surface regardless of the profile filters; the window still applies except for signed-up.
      const inWindow = compare(race.date, effectiveFrom) >= 0 && compare(race.date, effectiveTo) <= 0
      const inRange = compare(race.date, from) >= 0 && compare(race.date, to) <= 0
      if (!inRange) continue
      if (view.state !== 'SignedUp' && !inWindow) continue
      if (view.state === 'Dismissed' && !includeDismissed) continue
      if (!marked && view.state !== 'Dismissed' && !matchesProfile(race, profile)) continue
      items.push(toProposal(race, account))
    }
    items.sort((a, b) => a.date.localeCompare(b.date) || a.name.localeCompare(b.name))
  }
  return json(
    {
      from,
      to,
      effectiveFrom,
      effectiveTo,
      profileConfigured: profile !== null,
      catalogueUpdatedUtc: ctx.catalogueUpdatedUtc,
      truncated: false,
      items,
    } satisfies S['RaceProposalListResponse'],
    200,
    { 'Cache-Control': 'private, no-store' },
  )
}

export function markSeen(account: Account, nowIso: string): Response {
  if (account.raceInterests) account.raceSeenUtc = nowIso
  return new Response(null, { status: 204 })
}

function findRace(ctx: RaceRouteContext, raceId: string): CatalogueRace | undefined {
  return ctx.catalogue.find((race) => race.raceId === raceId)
}

export function getRace(ctx: RaceRouteContext, account: Account, raceId: string): Response {
  const race = findRace(ctx, raceId)
  if (!race || (race.removed && !visible({ ...race, canonicalId: null }, account))) return notFound()
  const details: RaceDetailsDto = { ...race, ...userView(race, account) }
  return json(details, 200, { 'Cache-Control': 'private, no-store' })
}

export function setRaceState(ctx: RaceRouteContext, account: Account, raceId: string, body: unknown): Response {
  const state = typeof body === 'object' && body !== null ? (body as { state?: unknown }).state : undefined
  if (state !== 'None' && state !== 'Interested' && state !== 'Dismissed') {
    return validation({ state: ['State must be None, Interested or Dismissed.'] })
  }
  const race = findRace(ctx, raceId)
  if (!race) return notFound()
  const view = userView(race, account)
  if ((race.removed || race.canonicalId) && state !== 'None') return notFound()
  if (state === 'None') account.raceStates.delete(raceId)
  else if (view.state !== 'SignedUp') {
    const limit = account.kind === 'Guest' ? GUEST_STATE_LIMIT : USER_STATE_LIMIT
    if (!account.raceStates.has(raceId) && account.raceStates.size >= limit) {
      return problem(409, 'Too many marked races', { type: PROPOSAL_STATE_LIMIT })
    }
    account.raceStates.set(raceId, { state, dateWhenMarked: race.date })
  }
  const after = userView(race, account)
  return json({
    raceId,
    state: after.state,
    linkedRunId: after.linkedRunId,
    dateChanged: after.dateChanged,
  } satisfies S['RaceUserStateDto'])
}

/** The Race distance category of ADR 0036 §5 with the +-1 % edges. */
export function distanceCategory(meters: number): S['RaceDistanceCategory'] {
  const near = (target: number) => Math.abs(meters - target) <= target * 0.01
  if (near(5000)) return 'FiveK'
  if (near(10000)) return 'TenK'
  if (near(21097.5)) return 'HalfMarathon'
  if (near(42195)) return 'Marathon'
  return 'Other'
}

/** Validates a signup body against the race; returns the chosen distance and the other fields or the 400. */
export function validateSignup(
  ctx: RaceRouteContext,
  race: CatalogueRace,
  body: unknown,
): { errors: Record<string, string[]> } | { value: S['SignUpForRaceRequest'] & { distanceMeters: number | null } } {
  const errors: Record<string, string[]> = {}
  const add = (key: string, text: string) => (errors[key] ??= []).push(text)
  const v = (typeof body === 'object' && body !== null ? body : {}) as S['SignUpForRaceRequest']
  if (compare(race.date, ctx.today) < 0) add('raceId', 'The race has already taken place.')
  const distance = v.distanceMeters ?? null
  if (distance === null) {
    if (race.distancesMeters.length > 1) add('distanceMeters', 'Choose one of the race distances.')
  } else if (!race.distancesMeters.includes(distance)) add('distanceMeters', 'Not one of the race distances.')
  if (v.startTime != null && !/^([01]\d|2[0-3]):[0-5]\d$/.test(v.startTime))
    add('startTime', 'Start time must be HH:mm.')
  if (
    v.targetTimeSeconds != null &&
    !(Number.isInteger(v.targetTimeSeconds) && v.targetTimeSeconds >= 1 && v.targetTimeSeconds <= 604_800)
  ) {
    add('targetTimeSeconds', 'Must be between 1 and 604800.')
  }
  if (v.runTypeId != null && ![1, 2, 3, 4, 5, 6].includes(v.runTypeId)) add('runTypeId', 'Must be a race run type.')
  if (Object.keys(errors).length > 0) return { errors }
  const only = race.distancesMeters.length === 1 ? race.distancesMeters[0]! : null
  return { value: { ...v, distanceMeters: distance ?? only } }
}

/** The run type of a signed-up race: Trail / Mountain -> trail race, Ultra or beyond a marathon -> ultra. */
export function raceRunTypeId(race: CatalogueRace, distance: number | null): number {
  if ((distance ?? 0) > 42195 * 1.01 || race.kind === 'Ultra') return 3
  if (race.kind === 'Trail' || race.kind === 'Mountain') return 2
  if (race.kind === 'CrossCountry') return 5
  return 1
}

export function raceAlreadyLinked(runId: string): Response {
  return problem(409, 'You already have a run for this race.', { type: RACE_ALREADY_LINKED, members: { runId } })
}

export { findRace, linkedRun, visible as raceVisible }
