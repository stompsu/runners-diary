import { describe, expect, it, vi } from 'vitest'
import { createFakeApi, faults, typeId, type RunDto, type RunWriteRequest } from '.'

const BASE = 'http://localhost'

function call(
  api: ReturnType<typeof createFakeApi>,
  method: string,
  path: string,
  options: { token?: string; body?: unknown } = {},
) {
  return api.handle(
    new Request(BASE + path, {
      method,
      headers: {
        ...(options.token ? { Authorization: `Bearer ${options.token}` } : {}),
        ...(options.body !== undefined ? { 'Content-Type': 'application/json' } : {}),
      },
      body: options.body === undefined ? undefined : JSON.stringify(options.body),
    }),
  )
}

const easy = (date: string, extra: Partial<RunWriteRequest> = {}): RunWriteRequest => ({
  date,
  status: 'Planned',
  runTypeId: typeId('easy'),
  ...extra,
})

describe('fake API', () => {
  it('serves anonymous endpoints without a token and protects the rest', async () => {
    const api = createFakeApi()
    expect((await call(api, 'GET', '/api/health')).status).toBe(200)
    const config = await call(api, 'GET', '/api/client-config')
    expect(await config.json()).toEqual({ entra: null })
    for (const path of [
      '/api/me',
      '/api/run-types',
      '/api/calendar/months/2026/11',
      '/api/runs?from=2026-11-01&to=2026-11-30',
    ]) {
      const res = await call(api, 'GET', path)
      expect(res.status).toBe(401)
      expect(res.headers.get('WWW-Authenticate')).toBe('Bearer')
    }
    expect((await call(api, 'GET', '/api/me', { token: 'garbage' })).status).toBe(401)
  })

  it('returns the configured Entra block', async () => {
    const entra = { clientId: 'c', authority: 'https://t.ciamlogin.com/', apiScope: 'api://a/access_as_user' }
    const api = createFakeApi({ entra })
    expect(await (await call(api, 'GET', '/api/client-config')).json()).toEqual({ entra })
    api.setEntra(null)
    expect(await (await call(api, 'GET', '/api/client-config')).json()).toEqual({ entra: null })
  })

  it('creates seeded guest sessions with today and expiry', async () => {
    const api = createFakeApi({ today: '2026-11-03', now: () => Date.parse('2026-11-03T10:00:00Z') })
    const res = await call(api, 'POST', '/api/guest-sessions')
    expect(res.status).toBe(201)
    const session = (await res.json()) as { accessToken: string; expiresAt: string }
    expect(session.expiresAt).toBe('2026-11-04T10:00:00.000Z')

    const me = (await (await call(api, 'GET', '/api/me', { token: session.accessToken })).json()) as Record<
      string,
      unknown
    >
    expect(me).toMatchObject({
      kind: 'Guest',
      today: '2026-11-03',
      timeZoneId: 'Europe/Warsaw',
      timeZoneIsDefault: true,
    })
    expect(me.guestExpiresAt).toBe(session.expiresAt)

    const types = (await (await call(api, 'GET', '/api/run-types', { token: session.accessToken })).json()) as unknown[]
    expect(types).toHaveLength(20)
    expect(api.runsOf(session.accessToken).length).toBeGreaterThanOrEqual(8)
  })

  it('rejects expired guest tokens', async () => {
    let now = Date.parse('2026-11-03T10:00:00Z')
    const api = createFakeApi({ now: () => now })
    const token = api.issueToken('Guest')
    expect((await call(api, 'GET', '/api/me', { token })).status).toBe(200)
    now += 25 * 60 * 60 * 1000
    expect((await call(api, 'GET', '/api/me', { token })).status).toBe(401)
  })

  it('isolates data per token (foreign runs are 404)', async () => {
    const api = createFakeApi()
    const a = api.issueToken('User')
    const b = api.issueToken('User')
    const created = (await (
      await call(api, 'POST', '/api/runs', { token: a, body: easy('2026-11-05') })
    ).json()) as RunDto
    expect((await call(api, 'GET', `/api/runs/${created.id}`, { token: a })).status).toBe(200)
    expect((await call(api, 'GET', `/api/runs/${created.id}`, { token: b })).status).toBe(404)
    expect((await call(api, 'DELETE', `/api/runs/${created.id}`, { token: b })).status).toBe(404)
    expect(api.runsOf(b)).toHaveLength(0)
  })

  it('updates the profile zone (users) and flips timeZoneIsDefault', async () => {
    const api = createFakeApi()
    const token = api.issueToken('User')
    const res = await call(api, 'PUT', '/api/me', {
      token,
      body: { timeZoneId: 'America/Los_Angeles', unitSystem: 'Metric', displayName: null },
    })
    expect(await res.json()).toMatchObject({ timeZoneId: 'America/Los_Angeles', timeZoneIsDefault: false })
    const bad = await call(api, 'PUT', '/api/me', { token, body: { timeZoneId: 'Not/AZone', unitSystem: 'Metric' } })
    expect(bad.status).toBe(400)
    expect(((await bad.json()) as { errors: object }).errors).toHaveProperty('timeZoneId')
  })

  describe('runs', () => {
    it('creates with the profile zone, bumps version on update and 409s on a stale one', async () => {
      const api = createFakeApi()
      const token = api.issueToken('User')
      const res = await call(api, 'POST', '/api/runs', { token, body: easy('2026-11-05', { startTime: '07:30' }) })
      expect(res.status).toBe(201)
      const run = (await res.json()) as RunDto
      expect(res.headers.get('Location')).toBe(`/api/runs/${run.id}`)
      expect(run).toMatchObject({ timeZoneId: 'Europe/Warsaw', startUtc: '2026-11-05T06:30:00.000Z' })

      const write = { ...easy('2026-11-05'), status: 'Completed' as const, distanceMeters: 5000, version: run.version }
      const updated = (await (await call(api, 'PUT', `/api/runs/${run.id}`, { token, body: write })).json()) as RunDto
      expect(updated.version).not.toBe(run.version)
      expect(updated.startTime).toBeNull() // full replace

      const stale = await call(api, 'PUT', `/api/runs/${run.id}`, { token, body: write })
      expect(stale.status).toBe(409)
      expect(((await stale.json()) as { type: string }).type).toBe('urn:runnersdiary:problem:concurrency-conflict')
    })

    it('computes startUtc in the run zone, not the browser zone', async () => {
      const api = createFakeApi()
      const token = api.issueToken('User')
      const res = await call(api, 'POST', '/api/runs', {
        token,
        body: easy('2026-07-01', { startTime: '06:00', timeZoneId: 'America/Los_Angeles' }),
      })
      expect(((await res.json()) as RunDto).startUtc).toBe('2026-07-01T13:00:00.000Z')
    })

    it('answers validation failures with camelCase keys', async () => {
      const api = createFakeApi()
      const token = api.issueToken('User')
      const post = async (body: unknown) => {
        const res = await call(api, 'POST', '/api/runs', { token, body })
        expect(res.status).toBe(400)
        expect(res.headers.get('Content-Type')).toContain('application/problem+json')
        return ((await res.json()) as { errors: Record<string, string[]> }).errors
      }
      expect(await post(easy('2026-11-05', { distanceMeters: 0 }))).toHaveProperty('distanceMeters')
      expect(await post(easy('2026-11-05', { rating: { stars: 3 } }))).toHaveProperty('rating')
      expect(
        await post(easy('2026-11-05', { status: 'Completed', race: { name: 'x', distanceCategory: 'TenK' } })),
      ).toHaveProperty('race')
      expect(await post({ ...easy('2026-11-05'), runTypeId: typeId('rest'), distanceMeters: 1000 })).toHaveProperty(
        'distanceMeters',
      )
      expect(await post(easy('2026-11-05', { avgHeartRate: 150, maxHeartRate: 140 }))).toHaveProperty('maxHeartRate')
      expect(await post({ ...easy('2026-11-05'), bogus: 1 })).toHaveProperty('body')
      expect(
        await post({
          ...easy('2026-11-05'),
          runTypeId: typeId('roadRace'),
          race: { name: 'R', distanceCategory: 'TenK', resultTimeSeconds: 3000 },
        }),
      ).toHaveProperty('race.resultTimeSeconds')
      expect(await post(easy('1900-01-01'))).toHaveProperty('date')
    })

    it('enforces the per-date and per-account run limits with the run-limit problem', async () => {
      const api = createFakeApi({ limits: { maxRunsPerDate: 2, maxRunsPerAccount: 3 } })
      const token = api.issueToken('User')
      for (const date of ['2026-11-05', '2026-11-05', '2026-11-06']) {
        expect((await call(api, 'POST', '/api/runs', { token, body: easy(date) })).status).toBe(201)
      }
      for (const date of ['2026-11-05', '2026-11-09']) {
        const res = await call(api, 'POST', '/api/runs', { token, body: easy(date) })
        expect(res.status).toBe(409)
        expect(((await res.json()) as { type: string }).type).toBe('urn:runnersdiary:problem:run-limit')
      }
    })

    it('deletes with 204 and then 404', async () => {
      const api = createFakeApi()
      const token = api.issueToken('User')
      const run = (await (await call(api, 'POST', '/api/runs', { token, body: easy('2026-11-05') })).json()) as RunDto
      expect((await call(api, 'DELETE', `/api/runs/${run.id}`, { token })).status).toBe(204)
      expect((await call(api, 'DELETE', `/api/runs/${run.id}`, { token })).status).toBe(404)
    })

    it('lists a range, ordered, and validates the span', async () => {
      const api = createFakeApi()
      const token = api.issueToken('User')
      for (const date of ['2026-11-09', '2026-11-02', '2026-12-30'])
        await call(api, 'POST', '/api/runs', { token, body: easy(date) })
      const list = (await (await call(api, 'GET', '/api/runs?from=2026-11-01&to=2026-11-30', { token })).json()) as {
        items: RunDto[]
      }
      expect(list.items.map((r) => r.date)).toEqual(['2026-11-02', '2026-11-09'])
      expect((await call(api, 'GET', '/api/runs?from=2026-01-01&to=2026-12-31', { token })).status).toBe(400)
    })
  })

  describe('calendar', () => {
    it('builds a Monday-first grid with ISO weeks and completed/planned totals', async () => {
      const api = createFakeApi()
      const token = api.issueToken('User')
      const post = (body: RunWriteRequest) => call(api, 'POST', '/api/runs', { token, body })
      await post(
        easy('2026-11-03', {
          status: 'Completed',
          distanceMeters: 10000,
          durationSeconds: 3000,
          elevationGainMeters: 50,
        }),
      )
      await post(
        easy('2026-11-05', {
          status: 'Completed',
          distanceMeters: 5000,
          durationSeconds: 1500,
          elevationGainMeters: 20,
        }),
      )
      await post(easy('2026-11-06', { distanceMeters: 8000 }))
      await post({ date: '2026-11-04', status: 'Completed', runTypeId: typeId('crossTraining'), durationSeconds: 3600 })
      await post(easy('2026-12-02')) // beyond the grid

      const month = (await (await call(api, 'GET', '/api/calendar/months/2026/11', { token })).json()) as {
        gridStart: string
        gridEnd: string
        runs: RunDto[]
        weeks: {
          isoWeek: number
          isoYear: number
          start: string
          completed: { runCount: number; distanceMeters: number; durationSeconds: number; elevationGainMeters: number }
          planned: { distanceMeters: number }
        }[]
      }
      expect(month.gridStart).toBe('2026-10-26')
      expect(month.gridEnd).toBe('2026-12-06')
      expect(month.weeks).toHaveLength(6)
      expect(month.weeks[0]).toMatchObject({ isoWeek: 44, isoYear: 2026, start: '2026-10-26' })
      expect(month.weeks[1]).toMatchObject({ isoWeek: 45, start: '2026-11-02' })
      expect(month.weeks[1]!.completed).toEqual({
        runCount: 2,
        distanceMeters: 15000,
        durationSeconds: 4500,
        elevationGainMeters: 70,
      })
      expect(month.weeks[1]!.planned.distanceMeters).toBe(8000)
      expect(month.runs.map((r) => r.date)).toEqual([
        '2026-11-03',
        '2026-11-04',
        '2026-11-05',
        '2026-11-06',
        '2026-12-02',
      ])
    })

    it('validates year and month', async () => {
      const api = createFakeApi()
      const token = api.issueToken('User')
      expect((await call(api, 'GET', '/api/calendar/months/2026/13', { token })).status).toBe(400)
      expect((await call(api, 'GET', '/api/calendar/months/1949/1', { token })).status).toBe(400)
      expect((await call(api, 'GET', '/api/calendar/months/2026/02', { token })).status).toBe(200)
    })
  })

  describe('faults', () => {
    it('answers 429 with Retry-After a limited number of times', async () => {
      const api = createFakeApi()
      api.addFault(faults.rateLimited({ method: 'POST', path: '/api/guest-sessions' }, 45, 1))
      const first = await call(api, 'POST', '/api/guest-sessions')
      expect(first.status).toBe(429)
      expect(first.headers.get('Retry-After')).toBe('45')
      expect(((await first.json()) as { type: string }).type).toBe('urn:runnersdiary:problem:rate-limited')
      expect((await call(api, 'POST', '/api/guest-sessions')).status).toBe(201)
    })

    it('answers guest-capacity 503, html 502 and network failures', async () => {
      const api = createFakeApi()
      api.addFault(faults.guestCapacity({ path: '/api/guest-sessions' }, 10, 1))
      const capacity = await call(api, 'POST', '/api/guest-sessions')
      expect(capacity.status).toBe(503)
      expect(capacity.headers.get('Retry-After')).toBe('10')

      api.clearFaults()
      api.addFault(faults.html({ path: /^\/api\/me$/ }, 502, 1))
      const html = await call(api, 'GET', '/api/me')
      expect(html.status).toBe(502)
      expect(html.headers.get('Content-Type')).toBe('text/html')

      api.addFault(faults.network({ path: '/api/health' }, 1))
      await expect(call(api, 'GET', '/api/health')).rejects.toBeInstanceOf(TypeError)
      expect((await call(api, 'GET', '/api/health')).status).toBe(200)
    })

    it('delays responses', async () => {
      vi.useFakeTimers()
      try {
        const api = createFakeApi()
        api.addFault(faults.delay({ path: '/api/health' }, 5000, 1))
        let done = false
        const pending = call(api, 'GET', '/api/health').then((r) => ((done = true), r))
        await vi.advanceTimersByTimeAsync(4999)
        expect(done).toBe(false)
        await vi.advanceTimersByTimeAsync(1)
        expect((await pending).status).toBe(200)
      } finally {
        vi.useRealTimers()
      }
    })
  })

  it('serves raw requests and Playwright-style routes', async () => {
    const api = createFakeApi()
    const raw = await api.handleRaw({ method: 'GET', url: `${BASE}/api/client-config`, headers: {} })
    expect(raw.status).toBe(200)
    expect(JSON.parse(raw.body)).toEqual({ entra: null })

    const fulfilled: { status: number }[] = []
    await api.fulfillRoute({
      request: () => ({
        method: () => 'GET',
        url: () => `${BASE}/api/health`,
        headers: () => ({}),
        postData: () => null,
      }),
      fulfill: (r) => void fulfilled.push(r) as unknown as Promise<void>,
      abort: () => Promise.resolve(),
    })
    expect(fulfilled[0]?.status).toBe(200)
  })
})
