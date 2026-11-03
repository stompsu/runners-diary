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
import { demoRuns, RUN_TYPES } from './seed.ts'

export { faults, type Fault } from './faults.ts'
export { RUN_TYPES, demoRuns, typeId } from './seed.ts'
export type { ClientConfigResponse, RunDto, RunWriteRequest } from './handlers.ts'

export interface FakeApiOptions {
  /** The user's "today" (`me.today`), YYYY-MM-DD. Default 2026-11-03. */
  today?: string
  /** Default time zone of new accounts. Default Europe/Warsaw (what guests get). */
  zone?: string
  /** `GET /api/client-config` payload. Default `null` (Entra disabled). */
  entra?: ClientConfigResponse['entra']
  /** Runs created for each new guest. Default `demoRuns(today)`; pass `[]` for an empty account. */
  guestSeed?: RunWriteRequest[]
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
  /** Requests seen so far, `"METHOD /path"`. */
  readonly requests: string[]
}

const DEFAULT_LIMITS: Limits = { maxRunsPerAccount: 500, maxRunsPerDate: 20 }
const DAY_MS = 24 * 60 * 60 * 1000

export function createFakeApi(options: FakeApiOptions = {}): FakeApi {
  let today = options.today ?? '2026-11-03'
  let entra: ClientConfigResponse['entra'] = options.entra ?? null
  const zone = options.zone ?? 'Europe/Warsaw'
  const limits: Limits = { ...DEFAULT_LIMITS, ...options.limits }
  const now = options.now ?? Date.now
  const guestTtlMs = options.guestTtlMs ?? DAY_MS

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
    }
    accounts.set(token, account)
    const seed = overrides.seed ?? (kind === 'Guest' ? (options.guestSeed ?? demoRuns(today)) : [])
    for (const write of seed) {
      const runId = nextId()
      account.runs.set(runId, buildRun(runId, write, account, new Date(now()).toISOString(), ++versionCounter))
    }
    return token
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
      return json({ entra } satisfies ClientConfigResponse, 200, { 'Cache-Control': 'public, max-age=300' })
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

    return notFound()
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
    requests,
  }
}
