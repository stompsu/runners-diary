import { addDays, parse } from '../../lib/dates/plainDate.ts'
import {
  buildRun,
  calendarMonth,
  json,
  notFound,
  problem,
  sortRuns,
  unauthorized,
  validateWrite,
  validation,
  isValidZone,
  type Account,
  type ClientConfigResponse,
  type Limits,
  type MeResponse,
  type RunDto,
  type RunWriteRequest,
} from './handlers.ts'
import { matches, type Fault } from './faults.ts'
import { buildInsightsReport, validateInsightsQuery, type InsightsScenario } from './insights.ts'
import {
  defaultRaceInterests,
  demoCatalogue,
  demoRaceInterests,
  demoRaceStates,
  distanceCategory,
  FEATURE_UNAVAILABLE,
  findRace,
  getRace,
  linkedRun,
  listProposals,
  markSeen,
  raceAlreadyLinked,
  raceRunTypeId,
  setRaceState,
  userView,
  validateRaceInterests,
  validateSignup,
  type CatalogueRace,
  type RaceInterestsDto,
  type RaceRouteContext,
} from './races.ts'
import { demoRuns, RUN_TYPES } from './seed.ts'
import { createSummaryService, type SummaryOptions, type SummaryPostOutcome, type SummaryScenario } from './summary.ts'
import {
  inspectGpx,
  processFakeGpx,
  readUpload,
  REJECTION_MESSAGES,
  sampleGpx,
  searchFakePlaces,
  toRunTrack,
  utcToLocal,
  type GpxTrackKind,
  type TrackDto,
  type TrackSummaryDto,
  type TrackWarningDto,
} from './tracks.ts'

export { faults, type Fault } from './faults.ts'
export { RUN_TYPES, demoRuns, typeId } from './seed.ts'
export type { ClientConfigResponse, RunDto, RunWriteRequest } from './handlers.ts'
export { sampleGpx, type TrackDto } from './tracks.ts'
export { GUEST_LIMIT, USER_LIMIT, sampleSummaryContent } from './summary.ts'
export type { InsightsSummaryDto, SummaryOptions, SummaryPostOutcome, SummaryScenario } from './summary.ts'
export type { CatalogueRace, RaceInterestsDto } from './races.ts'
export { DEMO_RACE_IDS } from './races.ts'
export { DEMO_REPORT_TODAY, demoReport, type InsightsReportDto, type InsightsScenario } from './insights.ts'

/** A track to put straight into an account (test setup; the upload path is `POST /api/runs/{id}/tracks`). */
export interface TrackSeed {
  kind: GpxTrackKind
  /** Default `Processed`. `Pending` = a legacy row without summary or GeoJSON (404 on both). */
  status?: TrackDto['status']
  fileName?: string
  summary?: Partial<TrackSummaryDto>
  warnings?: TrackWarningDto[]
  gpx?: string
}

export interface FakeApiOptions {
  /** The user's "today" (`me.today`), YYYY-MM-DD. Default 2026-11-03. */
  today?: string
  /** Default time zone of new accounts. Default Europe/Warsaw (what guests get). */
  zone?: string
  /** `GET /api/client-config` payload. Default `null` (Entra disabled). */
  entra?: ClientConfigResponse['entra']
  /** Runs created for each new guest. Default `demoRuns(today)`; pass `[]` for an empty account. */
  guestSeed?: RunWriteRequest[]
  /** Give each new guest a processed recorded track on the demo race, like the real demo seed. Default false. */
  demoTracks?: boolean
  /** `client-config.maps.geocodingEnabled` and the availability of `/api/geocoding/search`. Default false. */
  geocodingEnabled?: boolean
  /** What `GET /api/insights/report` answers with (see `InsightsScenario`). Default `demo`. */
  insights?: InsightsScenario
  /** `GET/POST /api/insights/summary` behaviour (see `SummaryScenario`, `SummaryPostOutcome`). Default `none` / `ok`. */
  summary?: SummaryOptions
  /** `client-config.races.enabled`. Default true. */
  racesEnabled?: boolean
  /** Give each new guest the demo race interest profile (M8 plan §3.2). Default true; false = no profile saved. */
  guestRaceProfile?: boolean
  /** The race catalogue. Default `demoCatalogue(today)`; pass `[]` for an empty one. */
  raceCatalogue?: CatalogueRace[]
  /** `catalogueUpdatedUtc` of the proposals response. Default = creation time. */
  catalogueUpdatedUtc?: string | null
  limits?: Partial<Limits>
  /** Clock in ms, for token expiry and timestamps. Default `Date.now`. */
  now?: () => number
  guestTtlMs?: number
}

export interface RawRequest {
  method: string
  url: string
  headers: Record<string, string>
  body?: string | null
}
export interface RawResponse {
  status: number
  headers: Record<string, string>
  body: string
}
/** Structural subset of Playwright's `Route`, so this module needs no Playwright import. */
export interface RouteLike {
  request(): { method(): string; url(): string; headers(): Record<string, string>; postData(): string | null }
  fulfill(response: { status: number; headers: Record<string, string>; body: string }): Promise<void>
  abort(errorCode?: string): Promise<void>
}

export interface FakeApi {
  handle(request: Request): Promise<Response>
  handleRaw(raw: RawRequest): Promise<RawResponse>
  /** For Playwright: `await page.route('**\/api/**', (route) => fake.fulfillRoute(route))`. */
  fulfillRoute(route: RouteLike): Promise<void>
  /** Creates an account and returns its bearer token (what the real API would issue). */
  issueToken(kind: 'Guest' | 'User', overrides?: { displayName?: string; seed?: RunWriteRequest[] }): string
  addFault(fault: Fault): void
  clearFaults(): void
  setEntra(entra: ClientConfigResponse['entra']): void
  setToday(today: string): void
  /** Test inspection: the runs held by the account behind a token. */
  runsOf(token: string): RunDto[]
  /** Puts a track straight into the account behind `token`; returns its DTO. */
  addTrack(token: string, runId: string, seed: TrackSeed): TrackDto
  /** Test inspection: the tracks of a run (stored DTOs), newest first. */
  tracksOf(token: string, runId: string): TrackDto[]
  setGeocodingEnabled(enabled: boolean): void
  setRacesEnabled(enabled: boolean): void
  /** Test inspection: the saved race interests of the account behind a token (null = none saved). */
  raceInterestsOf(token: string): RaceInterestsDto | null
  /** Test inspection: the caller's state of a race (`None` when unmarked). */
  raceStateOf(token: string, raceId: string): 'None' | 'Interested' | 'Dismissed' | 'SignedUp'
  /** `ProposalsSeenUtc` of the account behind a token. */
  raceSeenOf(token: string): string | null
  /** Changes a catalogue race (as a source rescan would): a new date, a removal, another URL. */
  patchRace(raceId: string, patch: Partial<CatalogueRace>): void
  setCatalogueUpdated(utc: string | null): void
  setInsightsScenario(scenario: InsightsScenario): void
  /** Changes what the summary endpoints answer with; `outcome` also sets how the next POSTs end. */
  setSummaryScenario(scenario: SummaryScenario, outcome?: SummaryPostOutcome, options?: { slowMs?: number }): void
  /** Changes only how POST /api/insights/summary ends. */
  setSummaryOutcome(outcome: SummaryPostOutcome, options?: { slowMs?: number }): void
  /** Requests seen so far, `"METHOD /path"`. */
  readonly requests: string[]
}

const DEFAULT_LIMITS: Limits = {
  maxRunsPerAccount: 500,
  maxRunsPerDate: 20,
  maxTracksPerAccount: 16,
  maxGuestFileBytes: 5 * 1024 * 1024,
  maxUserFileBytes: 20 * 1024 * 1024,
}
const DAY_MS = 24 * 60 * 60 * 1000

export function createFakeApi(options: FakeApiOptions = {}): FakeApi {
  let today = options.today ?? '2026-11-03'
  let entra: ClientConfigResponse['entra'] = options.entra ?? null
  const zone = options.zone ?? 'Europe/Warsaw'
  const limits: Limits = { ...DEFAULT_LIMITS, ...options.limits }
  const now = options.now ?? Date.now
  const guestTtlMs = options.guestTtlMs ?? DAY_MS
  let geocodingEnabled = options.geocodingEnabled ?? false
  let racesEnabled = options.racesEnabled ?? true
  let insightsScenario: InsightsScenario = options.insights ?? 'demo'

  const summaryService = createSummaryService({
    now,
    initial: options.summary,
    runsStamp: (account) =>
      [...account.runs.values()]
        .map((run) => `${run.id}:${run.version}`)
        .sort()
        .join(','),
  })

  let catalogue: CatalogueRace[] = options.raceCatalogue ?? demoCatalogue(today, new Date(now()).toISOString())
  let catalogueUpdatedUtc: string | null =
    options.catalogueUpdatedUtc === undefined ? new Date(now()).toISOString() : options.catalogueUpdatedUtc
  const raceContext = (): RaceRouteContext => ({ today, catalogue, catalogueUpdatedUtc })

  const accounts = new Map<string, Account>() // token -> account
  const faultList: Fault[] = []
  const requests: string[] = []
  let counter = 0
  let versionCounter = 0

  const nextId = () => `00000000-0000-4000-8000-${String(++counter).padStart(12, '0')}`

  function issueToken(
    kind: 'Guest' | 'User',
    overrides: { displayName?: string; seed?: RunWriteRequest[] } = {},
  ): string {
    const id = nextId()
    const token = `fake-${kind.toLowerCase()}-${id}`
    const account: Account = {
      id,
      kind,
      displayName: overrides.displayName ?? (kind === 'Guest' ? null : 'Test Runner'),
      timeZoneId: zone,
      timeZoneIsDefault: true,
      unitSystem: 'Metric',
      expiresAtMs: kind === 'Guest' ? now() + guestTtlMs : null,
      runs: new Map(),
      tracks: new Map(),
      raceInterests:
        kind === 'Guest' && (options.guestRaceProfile ?? true)
          ? demoRaceInterests(new Date(now()).toISOString())
          : null,
      raceStates: kind === 'Guest' && (options.guestRaceProfile ?? true) ? demoRaceStates(catalogue) : new Map(),
      raceSeenUtc: null,
    }
    accounts.set(token, account)
    const seed = overrides.seed ?? (kind === 'Guest' ? (options.guestSeed ?? demoRuns(today)) : [])
    for (const write of seed) {
      const runId = nextId()
      account.runs.set(runId, buildRun(runId, write, account, new Date(now()).toISOString(), ++versionCounter))
    }
    if (kind === 'Guest' && options.demoTracks) {
      const race = [...account.runs.values()].find((run) => run.race)
      if (race) {
        addTrack(account, race.id, {
          kind: 'RecordedTrack',
          fileName: 'gliwice-10k.gpx',
          summary: { startUtc: '2025-09-14T07:00:00Z', endUtc: '2025-09-14T07:49:30Z' },
        })
      }
    }
    return token
  }

  function addTrack(account: Account, runId: string, seed: TrackSeed): TrackDto {
    const run = account.runs.get(runId)
    if (!run) throw new Error(`No run ${runId}`)
    const processed = (seed.status ?? 'Processed') === 'Processed'
    const gpx = seed.gpx ?? sampleGpx({ times: ['2025-09-14T07:00:00Z', '2025-09-14T07:49:30Z'] })
    const inspected = inspectGpx(seed.fileName ?? 'track.gpx', gpx)
    const base = processFakeGpx(
      typeof inspected === 'string' ? { points: 2, hasElevation: true, times: [], routeOnly: false } : inspected,
      run.timeZoneId,
    )
    const summary: TrackSummaryDto = { ...base.summary, ...seed.summary }
    // A start instant given by the seed implies the local start unless that is given as well.
    if (seed.summary?.startUtc && !seed.summary.startLocal) {
      const local = utcToLocal(seed.summary.startUtc, run.timeZoneId)
      summary.startLocal = { ...local, timeZoneId: run.timeZoneId }
    }
    const dto: TrackDto = {
      id: nextId(),
      runId,
      kind: seed.kind,
      status: seed.status ?? 'Processed',
      originalFileName: seed.fileName ?? 'track.gpx',
      sizeBytes: new TextEncoder().encode(gpx).length,
      createdAt: new Date(now()).toISOString(),
      summary: processed ? summary : null,
      warnings: processed ? (seed.warnings ?? base.warnings) : [],
    }
    account.tracks.set(dto.id, { dto, geoJson: processed ? base.geoJson : null, gpx })
    syncRunTracks(account, runId)
    return dto
  }

  /** Tracks are a sub-resource: they change `RunDto.tracks[]` but never the run's `version` (ADR 0023 §2). */
  function syncRunTracks(account: Account, runId: string) {
    const run = account.runs.get(runId)
    if (!run) return
    const tracks = [...account.tracks.values()]
      .filter((track) => track.dto.runId === runId)
      .sort((a, b) => a.dto.kind.localeCompare(b.dto.kind))
      .map((track) => toRunTrack(track.dto))
    account.runs.set(runId, { ...run, tracks })
  }

  const me = (a: Account): MeResponse => ({
    id: a.id,
    kind: a.kind,
    displayName: a.displayName,
    timeZoneId: a.timeZoneId,
    timeZoneIsDefault: a.timeZoneIsDefault,
    unitSystem: a.unitSystem,
    today,
    guestExpiresAt: a.expiresAtMs === null ? null : new Date(a.expiresAtMs).toISOString(),
  })

  function authenticate(headers: Headers): Account | null {
    const value = headers.get('authorization')
    const token = value?.startsWith('Bearer ') ? value.slice(7) : null
    const account = token ? accounts.get(token) : undefined
    if (!account) return null
    if (account.expiresAtMs !== null && account.expiresAtMs <= now()) return null
    return account
  }

  async function readJson(request: Request): Promise<{ value: unknown } | { response: Response }> {
    const text = await request.text()
    if (text.length > 65_536) return { response: problem(413, 'Payload Too Large') }
    try {
      return { value: JSON.parse(text) as unknown }
    } catch {
      return { response: validation({ body: ['The request body is invalid.'] }) }
    }
  }

  async function applyFaults(method: string, path: string): Promise<Response | null> {
    for (const fault of [...faultList]) {
      if (!matches(fault, method, path)) continue
      if (fault.times !== undefined) {
        if (fault.times <= 0) continue
        fault.times -= 1
      }
      const a = fault.action
      if (a.type === 'delay') await new Promise((resolve) => setTimeout(resolve, a.ms))
      else if (a.type === 'network') throw new TypeError('Failed to fetch')
      else if (a.type === 'html') {
        return new Response('<html><body>Bad gateway</body></html>', {
          status: a.status,
          headers: { 'Content-Type': 'text/html' },
        })
      } else {
        return problem(a.status, 'Fault', {
          type: a.problemType,
          members: a.members,
          headers: a.retryAfterSeconds === undefined ? {} : { 'Retry-After': String(a.retryAfterSeconds) },
        })
      }
    }
    return null
  }

  async function route(request: Request, account: Account | null, path: string): Promise<Response> {
    const method = request.method.toUpperCase()

    // ---- anonymous ----
    if (method === 'GET' && path === '/api/health') return json({ status: 'Healthy', version: 'fake' })
    if (method === 'GET' && path === '/api/client-config') {
      return json(
        {
          entra,
          maps: {
            provider: 'MapLibre',
            mapLibreStyleUrl: 'https://tiles.openfreemap.org/styles/liberty',
            geocodingEnabled,
            azureMapsDomain: null,
          },
          gpx: { maxFileBytesUser: 20 * 1024 * 1024, maxFileBytesGuest: 5 * 1024 * 1024 },
          races: {
            enabled: racesEnabled,
            sources: [{ id: 'running-life', name: 'running.life', url: 'https://running.life/' }],
          },
          ai: { summaryGeneration: summaryService.generationEnabled() },
        } satisfies ClientConfigResponse,
        200,
        { 'Cache-Control': 'public, max-age=300' },
      )
    }
    if (method === 'POST' && path === '/api/guest-sessions') {
      const token = issueToken('Guest')
      const guest = accounts.get(token)!
      return json(
        {
          accessToken: token,
          tokenType: 'Bearer',
          expiresAt: new Date(guest.expiresAtMs!).toISOString(),
          userId: guest.id,
        },
        201,
        // A real server sends Date; the SPA uses it to correct for client clock skew (guest expiry).
        { 'Cache-Control': 'no-store', Date: new Date(now()).toUTCString() },
      )
    }

    // ---- authenticated ----
    if (!path.startsWith('/api/')) return notFound()
    if (!account) return unauthorized()

    if (path === '/api/me') {
      if (method === 'GET') return json(me(account))
      if (method === 'PUT') {
        const body = await readJson(request)
        if ('response' in body) return body.response
        const v = body.value as { displayName?: string | null; timeZoneId?: string; unitSystem?: string }
        const errors: Record<string, string[]> = {}
        if (!v || typeof v !== 'object') return validation({ body: ['The request body is invalid.'] })
        if (!v.timeZoneId || !isValidZone(v.timeZoneId)) errors.timeZoneId = ['Time zone must be a valid IANA id.']
        if (v.unitSystem !== 'Metric' && v.unitSystem !== 'Imperial') errors.unitSystem = ['Unit system is invalid.']
        if (v.displayName && v.displayName.length > 100)
          errors.displayName = ['Display name must be at most 100 characters.']
        if (Object.keys(errors).length) return validation(errors)
        account.timeZoneIsDefault = false
        account.timeZoneId = v.timeZoneId!
        account.unitSystem = v.unitSystem as Account['unitSystem']
        account.displayName = v.displayName ?? null
        return json(me(account))
      }
    }

    if (method === 'GET' && path === '/api/run-types') return json(RUN_TYPES)

    const calendar = /^\/api\/calendar\/months\/([^/]+)\/([^/]+)$/.exec(path)
    if (method === 'GET' && calendar) {
      const year = Number(calendar[1])
      const month = Number(calendar[2])
      if (!Number.isInteger(year) || year < 1950 || year > 2100)
        return validation({ year: ['Year must be between 1950 and 2100.'] })
      if (!Number.isInteger(month) || month < 1 || month > 12)
        return validation({ month: ['Month must be between 1 and 12.'] })
      return json(calendarMonth(year, month, [...account.runs.values()]))
    }

    if (path === '/api/runs') {
      if (method === 'GET') {
        const url = new URL(request.url)
        const from = url.searchParams.get('from') ?? ''
        const to = url.searchParams.get('to') ?? ''
        const errors: Record<string, string[]> = {}
        if (!parse(from)) errors.from = ['From must be a valid date.']
        if (!parse(to)) errors.to = ['To must be a valid date.']
        if (Object.keys(errors).length) return validation(errors)
        if (to < from) return validation({ to: ['To must not be before from.'] })
        if (addDays(from, 92) < to) return validation({ to: ['The range is at most 92 days.'] })
        const items = sortRuns([...account.runs.values()].filter((r) => r.date >= from && r.date <= to))
        return json({ from, to, items })
      }
      if (method === 'POST') {
        const body = await readJson(request)
        if ('response' in body) return body.response
        const result = validateWrite(body.value, 'create')
        if ('errors' in result) return validation(result.errors)
        const limit = limitProblem(account, result.value.date)
        if (limit) return limit
        const id = nextId()
        const run = buildRun(id, result.value, account, new Date(now()).toISOString(), ++versionCounter)
        account.runs.set(id, run)
        return json(run, 201, { Location: `/api/runs/${id}` })
      }
    }

    const one = /^\/api\/runs\/([^/]+)$/.exec(path)
    if (one) {
      const id = one[1]!
      const existing = account.runs.get(id)
      if (method === 'GET') return existing ? json(existing) : notFound()
      if (method === 'DELETE') {
        if (!existing) return notFound()
        account.runs.delete(id)
        for (const [trackId, track] of account.tracks) if (track.dto.runId === id) account.tracks.delete(trackId)
        return new Response(null, { status: 204 })
      }
      if (method === 'PUT') {
        if (!existing) return notFound()
        const body = await readJson(request)
        if ('response' in body) return body.response
        const result = validateWrite(body.value, 'update')
        if ('errors' in result) return validation(result.errors)
        if (result.value.version !== existing.version) {
          return problem(409, 'Concurrency conflict', {
            type: 'urn:runnersdiary:problem:concurrency-conflict',
          })
        }
        if (result.value.date !== existing.date) {
          const limit = limitProblem(account, result.value.date, true)
          if (limit) return limit
        }
        const run = buildRun(id, result.value, account, new Date(now()).toISOString(), ++versionCounter, existing)
        account.runs.set(id, run)
        return json(run)
      }
    }

    if (path.startsWith('/api/races/') || path === '/api/races') {
      // The real API answers 503 feature-unavailable for the whole /api/races group while Races:Enabled is false.
      if (!racesEnabled) {
        return problem(503, 'Race proposals are not available.', { type: FEATURE_UNAVAILABLE })
      }
      const raceResponse = await routeRaces(request, account, path)
      if (raceResponse) return raceResponse
    }

    if (method === 'GET' && path === '/api/insights/report') {
      const url = new URL(request.url)
      const query = {
        from: url.searchParams.get('from'),
        to: url.searchParams.get('to'),
        compare: url.searchParams.get('compare'),
      }
      const errors = validateInsightsQuery(query)
      if (errors) return validation(errors)
      return json(
        buildInsightsReport(
          { from: query.from!, to: query.to!, compare: query.compare ?? 'previous' },
          today,
          insightsScenario,
        ),
        200,
        { 'Cache-Control': 'private, no-store' },
      )
    }

    if (path === '/api/insights/summary') {
      if (method === 'GET') return summaryService.get(account, new URL(request.url))
      if (method === 'POST') {
        const body = await readJson(request)
        if ('response' in body) return body.response
        return summaryService.generate(account, request, body.value)
      }
    }

    const trackResponse = await routeTracks(request, account, path)
    if (trackResponse) return trackResponse

    if (method === 'GET' && path === '/api/geocoding/search') {
      if (!geocodingEnabled) {
        return problem(503, 'Place search is not available.', {
          type: 'urn:runnersdiary:problem:feature-unavailable',
        })
      }
      const url = new URL(request.url)
      const q = (url.searchParams.get('q') ?? '').trim()
      if (q.length < 2 || q.length > 100) return validation({ q: ['Must be 2 to 100 characters.'] })
      return json({ results: searchFakePlaces(q) })
    }

    return notFound()
  }

  async function routeRaces(request: Request, account: Account, path: string): Promise<Response | null> {
    const method = request.method.toUpperCase()
    const ctx = raceContext()

    if (path === '/api/races/interests') {
      if (method === 'GET') {
        return json(account.raceInterests ?? defaultRaceInterests(), 200, { 'Cache-Control': 'private, no-store' })
      }
      if (method === 'PUT') {
        const body = await readJson(request)
        if ('response' in body) return body.response
        const result = validateRaceInterests(body.value)
        if ('errors' in result) return validation(result.errors)
        account.raceInterests = {
          ...result.value,
          configured: true,
          updatedAt: new Date(now()).toISOString(),
        } as RaceInterestsDto
        return json(account.raceInterests)
      }
    }
    if (path === '/api/races/proposals' && method === 'GET') return listProposals(ctx, account, new URL(request.url))
    if (path === '/api/races/proposals/seen' && method === 'POST')
      return markSeen(account, new Date(now()).toISOString())

    const one = /^\/api\/races\/([^/]+)(?:\/(state|signup))?$/.exec(path)
    if (!one) return null
    const raceId = one[1]!
    const sub = one[2]
    if (!sub && method === 'GET') return getRace(ctx, account, raceId)
    if (sub === 'state' && method === 'PUT') {
      const body = await readJson(request)
      if ('response' in body) return body.response
      return setRaceState(ctx, account, raceId, body.value)
    }
    if (sub === 'signup' && method === 'POST') {
      const race = findRace(ctx, raceId)
      if (!race || race.removed || race.canonicalId) return notFound()
      const text = await request.text()
      let parsed: unknown = {}
      if (text.trim() !== '') {
        try {
          parsed = JSON.parse(text)
        } catch {
          return validation({ body: ['The request body is invalid.'] })
        }
      }
      const existing = linkedRun(account, race.raceId)
      if (existing) return raceAlreadyLinked(existing.id)
      const result = validateSignup(ctx, race, parsed)
      if ('errors' in result) return validation(result.errors)
      const { distanceMeters, startTime, targetTimeSeconds, runTypeId } = result.value
      const limit = limitProblem(account, race.date)
      if (limit) return limit
      const write: RunWriteRequest = {
        date: race.date,
        startTime: startTime ?? race.startTime,
        timeZoneId: race.timeZoneId ?? account.timeZoneId,
        status: 'Planned',
        runTypeId: runTypeId ?? raceRunTypeId(race, distanceMeters ?? null),
        title: null,
        notes: null,
        location: race.location ? { ...race.location, name: race.locationName } : null,
        distanceMeters: distanceMeters ?? null,
        surface: race.surface,
        elevationGainMeters: race.elevationGainMeters,
        race: {
          name: race.name,
          distanceCategory: distanceMeters ? distanceCategory(distanceMeters) : 'Other',
          officialDistanceMeters:
            distanceMeters && distanceCategory(distanceMeters) === 'Other' ? distanceMeters : null,
          targetTimeSeconds: targetTimeSeconds ?? null,
        },
      }
      const runId = nextId()
      const run = {
        ...buildRun(runId, write, account, new Date(now()).toISOString(), ++versionCounter),
        catalogRaceId: race.raceId,
      }
      account.runs.set(runId, run)
      return json(run, 201, { Location: `/api/runs/${runId}` })
    }
    return null
  }

  const INVALID_GPX = 'urn:runnersdiary:problem:invalid-gpx'

  async function routeTracks(request: Request, account: Account, path: string): Promise<Response | null> {
    const method = request.method.toUpperCase()
    const collection = /^\/api\/runs\/([^/]+)\/tracks$/.exec(path)
    const item = /^\/api\/runs\/([^/]+)\/tracks\/([^/]+)(?:\/(geojson|gpx))?$/.exec(path)

    if (collection && method === 'POST') {
      const run = account.runs.get(collection[1]!)
      if (!run) return notFound()
      const form = await readUpload(request)
      if (!form) return problem(415, 'Unsupported Media Type')
      if (form.fields.kind !== 'PlannedCourse' && form.fields.kind !== 'RecordedTrack') {
        return validation({ kind: ['Kind must be PlannedCourse or RecordedTrack.'] })
      }
      const kind: GpxTrackKind = form.fields.kind
      if (RUN_TYPES.find((type) => type.id === run.runTypeId)?.isRestDay) {
        return validation({ runId: ['Rest days have no tracks.'] })
      }
      if (!form.file) return validation({ file: ['A file is required.'] })
      const maxBytes = account.kind === 'Guest' ? limits.maxGuestFileBytes : limits.maxUserFileBytes
      if (form.file.size > maxBytes) return problem(413, 'Payload Too Large')
      const inspected = inspectGpx(form.file.name, form.file.text)
      if (typeof inspected === 'string') {
        return problem(400, 'Invalid GPX file', {
          type: INVALID_GPX,
          errors: { file: [REJECTION_MESSAGES[inspected]] },
          members: { code: inspected },
        })
      }
      const existing = [...account.tracks.values()].find((t) => t.dto.runId === run.id && t.dto.kind === kind)
      if (existing && form.fields.replace !== 'true') {
        return problem(409, 'A track of this kind already exists.', {
          type: 'urn:runnersdiary:problem:track-exists',
        })
      }
      if (!existing && account.tracks.size >= limits.maxTracksPerAccount) {
        return problem(409, 'Track limit reached', { type: 'urn:runnersdiary:problem:track-limit' })
      }
      if (existing) account.tracks.delete(existing.dto.id)
      const processed = processFakeGpx(inspected, run.timeZoneId)
      const dto: TrackDto = {
        id: nextId(),
        runId: run.id,
        kind,
        status: 'Processed',
        originalFileName: form.file.name,
        sizeBytes: form.file.size,
        createdAt: new Date(now()).toISOString(),
        summary: processed.summary,
        warnings: processed.warnings,
      }
      account.tracks.set(dto.id, { dto, geoJson: processed.geoJson, gpx: form.file.text })
      syncRunTracks(account, run.id)
      return json(dto, 201, { Location: `/api/runs/${run.id}/tracks/${dto.id}` })
    }

    if (item) {
      const [, runId, trackId, view] = item
      const track = account.tracks.get(trackId!)
      // 404 for a missing run, a missing track and a track of another run alike.
      if (!account.runs.has(runId!) || !track || track.dto.runId !== runId) return notFound()
      if (method === 'GET' && !view) return json(track.dto)
      if (method === 'GET' && view === 'geojson') {
        if (track.dto.status !== 'Processed' || !track.geoJson) return notFound()
        return new Response(JSON.stringify(track.geoJson), {
          headers: {
            'Content-Type': 'application/geo+json',
            'Cache-Control': 'private, max-age=86400, immutable',
            ETag: `"${track.dto.id}"`,
          },
        })
      }
      if (method === 'GET' && view === 'gpx') {
        if (track.dto.status !== 'Processed') return notFound()
        return new Response(track.gpx, {
          headers: {
            'Content-Type': 'application/gpx+xml',
            'Content-Disposition': `attachment; filename="${track.dto.originalFileName}"`,
            'X-Content-Type-Options': 'nosniff',
            'Cache-Control': 'private, no-store',
          },
        })
      }
      if (method === 'DELETE' && !view) {
        account.tracks.delete(track.dto.id)
        syncRunTracks(account, runId!)
        return new Response(null, { status: 204 })
      }
    }
    return null
  }

  function limitProblem(account: Account, date: string, moving = false): Response | null {
    const all = [...account.runs.values()]
    if (!moving && all.length >= limits.maxRunsPerAccount) {
      return problem(409, 'Run limit reached', { type: 'urn:runnersdiary:problem:run-limit' })
    }
    if (all.filter((r) => r.date === date).length >= limits.maxRunsPerDate) {
      return problem(409, 'Run limit reached', { type: 'urn:runnersdiary:problem:run-limit' })
    }
    return null
  }

  async function handle(request: Request): Promise<Response> {
    const url = new URL(request.url)
    const path = url.pathname
    requests.push(`${request.method.toUpperCase()} ${path}`)
    const faulted = await applyFaults(request.method, path)
    if (faulted) return faulted
    return route(request, authenticate(request.headers), path)
  }

  async function handleRaw(raw: RawRequest): Promise<RawResponse> {
    const hasBody = raw.body != null && !['GET', 'HEAD'].includes(raw.method.toUpperCase())
    const request = new Request(raw.url, {
      method: raw.method,
      headers: raw.headers,
      body: hasBody ? raw.body : undefined,
    })
    const response = await handle(request)
    const headers: Record<string, string> = {}
    response.headers.forEach((value, key) => (headers[key] = value))
    return { status: response.status, headers, body: await response.text() }
  }

  return {
    handle,
    handleRaw,
    async fulfillRoute(routeLike) {
      const r = routeLike.request()
      try {
        const out = await handleRaw({ method: r.method(), url: r.url(), headers: r.headers(), body: r.postData() })
        await routeLike.fulfill(out)
      } catch (error) {
        if (error instanceof TypeError) await routeLike.abort('failed')
        else throw error
      }
    },
    issueToken,
    addFault: (fault) => faultList.push({ ...fault }),
    clearFaults: () => void (faultList.length = 0),
    setEntra: (value) => void (entra = value),
    setToday: (value) => void (today = value),
    runsOf: (token) => sortRuns([...(accounts.get(token)?.runs.values() ?? [])]),
    addTrack: (token, runId, seed) => {
      const account = accounts.get(token)
      if (!account) throw new Error('Unknown token')
      return addTrack(account, runId, seed)
    },
    tracksOf: (token, runId) =>
      [...(accounts.get(token)?.tracks.values() ?? [])].filter((t) => t.dto.runId === runId).map((t) => t.dto),
    setGeocodingEnabled: (enabled) => void (geocodingEnabled = enabled),
    setRacesEnabled: (enabled) => void (racesEnabled = enabled),
    raceInterestsOf: (token) => accounts.get(token)?.raceInterests ?? null,
    raceStateOf: (token, raceId) => {
      const account = accounts.get(token)
      const race = findRace(raceContext(), raceId)
      return account && race ? userView(race, account).state : 'None'
    },
    raceSeenOf: (token) => accounts.get(token)?.raceSeenUtc ?? null,
    patchRace: (raceId, patch) => {
      catalogue = catalogue.map((race) => (race.raceId === raceId ? { ...race, ...patch } : race))
    },
    setCatalogueUpdated: (utc) => void (catalogueUpdatedUtc = utc),
    setInsightsScenario: (scenario) => void (insightsScenario = scenario),
    setSummaryScenario: (scenario, outcome, opts) => summaryService.set(scenario, outcome, opts),
    setSummaryOutcome: (outcome, opts) => summaryService.setPost(outcome, opts),
    requests,
  }
}
