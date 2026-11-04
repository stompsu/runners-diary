import { describe, expect, it, vi } from 'vitest'
import { createFakeApi, faults, sampleGpx, typeId, type RunDto, type RunWriteRequest, type TrackDto } from '.'

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
    expect(await config.json()).toMatchObject({ entra: null, maps: { provider: 'MapLibre', geocodingEnabled: false } })
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
    expect(await (await call(api, 'GET', '/api/client-config')).json()).toMatchObject({ entra })
    api.setEntra(null)
    expect(await (await call(api, 'GET', '/api/client-config')).json()).toMatchObject({
      entra: null,
      maps: { provider: 'MapLibre', geocodingEnabled: false },
    })
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
    expect(JSON.parse(raw.body)).toMatchObject({ entra: null, maps: { provider: 'MapLibre', geocodingEnabled: false } })

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

describe('fake API: tracks and geocoding (M4)', () => {
  /** A multipart upload built by hand, so the test does not depend on the environment's FormData/File classes. */
  function upload(
    api: ReturnType<typeof createFakeApi>,
    token: string,
    runId: string,
    parts: { kind?: string; file?: { name: string; text: string }; replace?: string },
  ) {
    const boundary = 'xBOUNDARYx'
    const field = (name: string, value: string) =>
      `--${boundary}\r\nContent-Disposition: form-data; name="${name}"\r\n\r\n${value}\r\n`
    const body =
      (parts.kind ? field('kind', parts.kind) : '') +
      (parts.replace ? field('replace', parts.replace) : '') +
      (parts.file
        ? `--${boundary}\r\nContent-Disposition: form-data; name="file"; filename="${parts.file.name}"\r\nContent-Type: application/gpx+xml\r\n\r\n${parts.file.text}\r\n`
        : '') +
      `--${boundary}--\r\n`
    return api.handle(
      new Request(`${BASE}/api/runs/${runId}/tracks`, {
        method: 'POST',
        headers: { Authorization: `Bearer ${token}`, 'Content-Type': `multipart/form-data; boundary=${boundary}` },
        body,
      }),
    )
  }
  const file = (text = sampleGpx({ times: ['2026-11-14T07:30:00Z', '2026-11-14T08:19:30Z'] }), name = 'a.gpx') => ({
    name,
    text,
  })
  const setup = () => {
    const api = createFakeApi({ guestSeed: [easy('2026-11-14'), { ...easy('2026-11-15'), runTypeId: typeId('rest') }] })
    const token = api.issueToken('Guest')
    const [run, restDay] = api.runsOf(token)
    return { api, token, run: run!, restDay: restDay! }
  }

  it('stores an upload as a processed track: DTO, run.tracks[], local start time in the run zone, no version change', async () => {
    const { api, token, run } = setup()
    const response = await upload(api, token, run.id, { kind: 'RecordedTrack', file: file() })
    expect(response.status).toBe(201)
    const track = (await response.json()) as TrackDto
    expect(response.headers.get('Location')).toBe(`/api/runs/${run.id}/tracks/${track.id}`)
    expect(track).toMatchObject({
      runId: run.id,
      kind: 'RecordedTrack',
      status: 'Processed',
      originalFileName: 'a.gpx',
      warnings: [],
      summary: {
        distanceMeters: 10004.8,
        elapsedSeconds: 2970,
        startUtc: '2026-11-14T07:30:00.000Z',
        startLocal: { date: '2026-11-14', time: '08:30', timeZoneId: 'Europe/Warsaw' },
      },
    })
    const updated = (await (await call(api, 'GET', `/api/runs/${run.id}`, { token })).json()) as RunDto
    expect(updated.tracks).toEqual([
      expect.objectContaining({ id: track.id, kind: 'RecordedTrack', originalFileName: 'a.gpx' }),
    ])
    expect(updated.version).toBe(run.version)
  })

  it("serves the track, its GeoJSON and the original file; 404 for anything that is not the caller's", async () => {
    const { api, token, run } = setup()
    const track = (await (await upload(api, token, run.id, { kind: 'PlannedCourse', file: file() })).json()) as TrackDto
    const base = `/api/runs/${run.id}/tracks/${track.id}`
    expect(((await (await call(api, 'GET', base, { token })).json()) as TrackDto).id).toBe(track.id)

    const geo = await call(api, 'GET', `${base}/geojson`, { token })
    expect(geo.headers.get('Content-Type')).toBe('application/geo+json')
    expect(geo.headers.get('ETag')).toBeTruthy()
    expect(await geo.json()).toMatchObject({ type: 'FeatureCollection', formatVersion: 1 })

    const gpx = await call(api, 'GET', `${base}/gpx`, { token })
    expect(gpx.headers.get('Content-Disposition')).toContain('a.gpx')
    expect(await gpx.text()).toContain('<gpx')

    const other = api.issueToken('Guest')
    for (const suffix of ['', '/geojson', '/gpx']) {
      expect((await call(api, 'GET', base + suffix, { token: other })).status).toBe(404)
    }
    expect((await call(api, 'GET', `/api/runs/${run.id}/tracks/nope`, { token })).status).toBe(404)
  })

  it('a legacy Pending track has no summary and no GeoJSON or file', async () => {
    const { api, token, run } = setup()
    const pending = api.addTrack(token, run.id, { kind: 'RecordedTrack', status: 'Pending' })
    expect(pending.summary).toBeNull()
    const base = `/api/runs/${run.id}/tracks/${pending.id}`
    expect((await call(api, 'GET', base, { token })).status).toBe(200)
    expect((await call(api, 'GET', `${base}/geojson`, { token })).status).toBe(404)
    expect((await call(api, 'GET', `${base}/gpx`, { token })).status).toBe(404)
  })

  it('rejects like the real API: every invalid-gpx code, kind, rest days, missing file, size, 415', async () => {
    const { api, token, run, restDay } = setup()
    const code = async (text: string, name = 'a.gpx') => {
      const response = await upload(api, token, run.id, { kind: 'RecordedTrack', file: file(text, name) })
      expect(response.status).toBe(400)
      const body = (await response.json()) as { type: string; code: string; errors: { file: string[] } }
      expect(body.type).toBe('urn:runnersdiary:problem:invalid-gpx')
      expect(body.errors.file).toHaveLength(1)
      return body.code
    }
    expect(await code(sampleGpx(), 'a.txt')).toBe('wrongExtension')
    expect(await code('')).toBe('malformedXml')
    expect(await code('{"not":"xml"}')).toBe('unsupportedFormat')
    expect(await code('<?xml version="1.0"?><!DOCTYPE gpx [<!ENTITY x "y">]><gpx></gpx>')).toBe('dtdProhibited')
    expect(await code('<kml></kml>')).toBe('notGpx')
    expect(await code(sampleGpx().replace('</gpx>', ''))).toBe('malformedXml')
    expect(await code(sampleGpx().replace('lat="50.2948"', 'lat="95"'))).toBe('invalidCoordinate')
    expect(await code('<gpx><trk><trkseg><trkpt lat="1" lon="1"/></trkseg></trk></gpx>')).toBe('tooFewPoints')

    const badKind = await upload(api, token, run.id, { kind: 'recordedtrack', file: file() })
    expect(badKind.status).toBe(400)
    expect(((await badKind.json()) as { errors: object }).errors).toHaveProperty('kind')
    expect(
      (
        (await (await upload(api, token, restDay.id, { kind: 'RecordedTrack', file: file() })).json()) as {
          errors: object
        }
      ).errors,
    ).toHaveProperty('runId')
    expect(
      ((await (await upload(api, token, run.id, { kind: 'RecordedTrack' })).json()) as { errors: object }).errors,
    ).toHaveProperty('file')
    expect((await upload(api, token, 'nope', { kind: 'RecordedTrack', file: file() })).status).toBe(404)
    expect(
      (await call(api, 'POST', `/api/runs/${run.id}/tracks`, { token, body: { kind: 'RecordedTrack' } })).status,
    ).toBe(415)

    const tiny = createFakeApi({ guestSeed: [easy('2026-11-14')], limits: { maxGuestFileBytes: 100 } })
    const tinyToken = tiny.issueToken('Guest')
    const tinyRun = tiny.runsOf(tinyToken)[0]!
    expect((await upload(tiny, tinyToken, tinyRun.id, { kind: 'RecordedTrack', file: file() })).status).toBe(413)
    expect(api.tracksOf(token, run.id)).toHaveLength(0)
  })

  it('reports warnings: no elevation, route-only', async () => {
    const { api, token, run } = setup()
    const bare = (await (
      await upload(api, token, run.id, { kind: 'RecordedTrack', file: file(sampleGpx({ ele: false })) })
    ).json()) as TrackDto
    expect(bare.warnings).toEqual(['noElevation'])
    expect(bare.summary).toMatchObject({ elevationGainMeters: null, elapsedSeconds: null, startLocal: null })
    const route = (await (
      await upload(api, token, run.id, { kind: 'PlannedCourse', file: file(sampleGpx({ route: true })) })
    ).json()) as TrackDto
    expect(route.warnings).toEqual(['routeConverted'])
  })

  it('409 track-exists until replace=true; replace swaps the track (new id); track limit is 409 track-limit', async () => {
    const { api, token, run } = setup()
    const first = (await (await upload(api, token, run.id, { kind: 'RecordedTrack', file: file() })).json()) as TrackDto
    const again = await upload(api, token, run.id, { kind: 'RecordedTrack', file: file() })
    expect(again.status).toBe(409)
    expect(((await again.json()) as { type: string }).type).toBe('urn:runnersdiary:problem:track-exists')

    const replaced = (await (
      await upload(api, token, run.id, { kind: 'RecordedTrack', file: file(undefined, 'b.gpx'), replace: 'true' })
    ).json()) as TrackDto
    expect(replaced.id).not.toBe(first.id)
    expect(api.tracksOf(token, run.id).map((t) => t.originalFileName)).toEqual(['b.gpx'])

    const limited = createFakeApi({ guestSeed: [easy('2026-11-14')], limits: { maxTracksPerAccount: 1 } })
    const limitedToken = limited.issueToken('Guest')
    const limitedRun = limited.runsOf(limitedToken)[0]!
    await upload(limited, limitedToken, limitedRun.id, { kind: 'RecordedTrack', file: file() })
    const over = await upload(limited, limitedToken, limitedRun.id, { kind: 'PlannedCourse', file: file() })
    expect(over.status).toBe(409)
    expect(((await over.json()) as { type: string }).type).toBe('urn:runnersdiary:problem:track-limit')
  })

  it('delete removes the track from the run; deleting the run removes its tracks', async () => {
    const { api, token, run } = setup()
    const track = api.addTrack(token, run.id, { kind: 'RecordedTrack' })
    expect((await call(api, 'DELETE', `/api/runs/${run.id}/tracks/${track.id}`, { token })).status).toBe(204)
    expect(api.runsOf(token)[0]!.tracks).toEqual([])
    expect((await call(api, 'DELETE', `/api/runs/${run.id}/tracks/${track.id}`, { token })).status).toBe(404)

    api.addTrack(token, run.id, { kind: 'PlannedCourse' })
    await call(api, 'DELETE', `/api/runs/${run.id}`, { token })
    expect(api.tracksOf(token, run.id)).toEqual([])
  })

  it('can seed a demo track on the race of each new guest', async () => {
    const api = createFakeApi({ demoTracks: true })
    const token = api.issueToken('Guest')
    const race = api.runsOf(token).find((r) => r.race)!
    expect(race.tracks).toEqual([
      expect.objectContaining({ kind: 'RecordedTrack', originalFileName: 'gliwice-10k.gpx' }),
    ])
    expect(
      createFakeApi()
        .runsOf(createFakeApi().issueToken('Guest'))
        .every((r) => r.tracks.length === 0),
    ).toBe(true)
  })

  it('place search: 503 feature-unavailable until enabled, then validated and filtered', async () => {
    const api = createFakeApi()
    const token = api.issueToken('Guest')
    const disabled = await call(api, 'GET', '/api/geocoding/search?q=gliwice', { token })
    expect(disabled.status).toBe(503)
    expect(((await disabled.json()) as { type: string }).type).toBe('urn:runnersdiary:problem:feature-unavailable')

    api.setGeocodingEnabled(true)
    expect(await (await call(api, 'GET', '/api/client-config')).json()).toMatchObject({
      maps: { geocodingEnabled: true },
    })
    const found = (await (await call(api, 'GET', '/api/geocoding/search?q=gliwice', { token })).json()) as {
      results: { name: string; latitude: number; longitude: number; type: string | null }[]
    }
    expect(found.results.map((place) => place.name)).toEqual(['Gliwice Rynek', 'Gliwice'])
    expect((await call(api, 'GET', '/api/geocoding/search?q=g', { token })).status).toBe(400)
    expect((await call(api, 'GET', '/api/geocoding/search?q=gliwice')).status).toBe(401)
  })
})
