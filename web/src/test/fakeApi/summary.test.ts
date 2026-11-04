import { describe, expect, it } from 'vitest'
import {
  createFakeApi,
  GUEST_LIMIT,
  typeId,
  type InsightsSummaryDto,
  type SummaryPostOutcome,
  type SummaryScenario,
} from '.'

const BASE = 'http://localhost'
const QUERY = 'from=2026-08-17&to=2026-11-08&compare=previous&lang=en'
const BODY = { from: '2026-08-17', to: '2026-11-08', compare: 'previous', lang: 'en', regenerate: false }

function setup(scenario: SummaryScenario = 'none', post: SummaryPostOutcome = 'ok', kind: 'Guest' | 'User' = 'Guest') {
  const api = createFakeApi({ summary: { scenario, post, slowMs: 50 } })
  const token = api.issueToken(kind)
  const headers = { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' }
  return {
    api,
    headers,
    get: async (query = QUERY) => {
      const response = await api.handle(new Request(`${BASE}/api/insights/summary?${query}`, { headers }))
      return { response, body: (await response.json()) as InsightsSummaryDto & { errors?: Record<string, string[]> } }
    },
    post: async (body: unknown = BODY) => {
      const response = await api.handle(
        new Request(`${BASE}/api/insights/summary`, { method: 'POST', headers, body: JSON.stringify(body) }),
      )
      return { response, body: (await response.json()) as InsightsSummaryDto & Record<string, unknown> }
    },
  }
}

describe('fake insights summary', () => {
  it('requires a token', async () => {
    const api = createFakeApi()
    expect((await api.handle(new Request(`${BASE}/api/insights/summary?${QUERY}`))).status).toBe(401)
  })

  it.each([
    ['none', 'none', null, 'available'],
    ['fresh', 'fresh', 'model', 'available'],
    ['stale', 'stale', 'model', 'available'],
    ['demo', 'fresh', 'demo', 'available'],
    ['template', 'fresh', 'template', 'notNeeded'],
    ['notConfigured', 'fresh', 'demo', 'notConfigured'],
    ['quotaExhausted', 'none', null, 'quotaExhausted'],
  ] as [SummaryScenario, string, string | null, string][])(
    'scenario %s',
    async (scenario, state, source, availability) => {
      const { get } = setup(scenario)
      const { response, body } = await get()

      expect(response.headers.get('Cache-Control')).toBe('private, no-store')
      expect(body.state).toBe(state)
      expect(body.summary?.source ?? null).toBe(source)
      expect(body.generation.availability).toBe(availability)
    },
  )

  it('validates the query and the body like the API (unknown members are rejected)', async () => {
    const { get, post } = setup()
    expect((await get('from=2026-13-01&to=2026-11-08')).response.status).toBe(400)
    expect((await get('from=2026-08-17&to=2026-11-08&lang=pl')).body.errors).toHaveProperty('lang')
    expect((await post({ ...BODY, extra: 1 })).response.status).toBe(400)
    expect((await post({ ...BODY, from: 'x' })).response.status).toBe(400)
  })

  it('a POST stores a fresh summary, counts toward the quota, and ends in 429 at the guest limit', async () => {
    const { get, post } = setup()
    for (let i = 0; i < GUEST_LIMIT; i++) expect((await post()).response.status).toBe(200)

    const stored = await get()
    expect(stored.body.state).toBe('fresh')
    expect(stored.body.generation).toMatchObject({
      availability: 'quotaExhausted',
      used: 3,
      limit: 3,
      scope: 'account',
    })
    const refused = await post()
    expect(refused.response.status).toBe(429)
    expect(refused.body).toMatchObject({ type: 'urn:runnersdiary:problem:ai-quota', scope: 'account' })
  })

  it('a summary goes stale when the runs change', async () => {
    const { api, post, get, headers } = setup()
    await post()
    expect((await get()).body.state).toBe('fresh')

    const created = await api.handle(
      new Request(`${BASE}/api/runs`, {
        method: 'POST',
        headers,
        body: JSON.stringify({ date: '2026-10-01', status: 'Planned', runTypeId: typeId('easy') }),
      }),
    )
    expect(created.status).toBe(201)

    expect((await get()).body.state).toBe('stale')
    expect((await post({ ...BODY, regenerate: true })).response.status).toBe(200)
    expect((await get()).body.state).toBe('fresh')
  })

  it.each([
    ['inProgress', 409, 'summary-in-progress'],
    ['quotaAccount', 429, 'ai-quota'],
    ['quotaGlobal', 429, 'ai-quota'],
    ['rateLimited', 429, 'rate-limited'],
    ['invalidOutput', 502, 'summary-failed'],
    ['timeout', 502, 'summary-failed'],
    ['provider', 502, 'summary-failed'],
    ['unavailable', 503, 'feature-unavailable'],
    ['busy', 503, 'ai-busy'],
  ] as [SummaryPostOutcome, number, string][])('outcome %s answers %i %s', async (outcome, status, type) => {
    const { post } = setup('none', outcome)
    const { response, body } = await post()

    expect(response.status).toBe(status)
    expect(body.type).toBe(`urn:runnersdiary:problem:${type}`)
  })

  it.each(['invalidOutput', 'timeout', 'provider'] as const)(
    '%s failure carries its reason and uses quota',
    async (reason) => {
      const { post, get } = setup('none', reason)
      expect((await post()).body.reason).toBe(reason)
      expect((await get()).body.generation.used).toBe(1)
    },
  )

  it('a slow POST can be aborted', async () => {
    const { api } = setup('none', 'slow')
    const token = api.issueToken('Guest')
    const controller = new AbortController()
    const pending = api.handle(
      new Request(`${BASE}/api/insights/summary`, {
        method: 'POST',
        headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
        body: JSON.stringify(BODY),
        signal: controller.signal,
      }),
    )
    controller.abort()

    await expect(pending).rejects.toMatchObject({ name: 'AbortError' })
  })

  it('reports whether generation is enabled in client-config', async () => {
    const enabled = createFakeApi({ summary: { scenario: 'none' } })
    const disabled = createFakeApi({ summary: { scenario: 'notConfigured' } })
    const read = async (api: ReturnType<typeof createFakeApi>) =>
      ((await (await api.handle(new Request(`${BASE}/api/client-config`))).json()) as { ai: unknown }).ai

    expect(await read(enabled)).toEqual({ summaryGeneration: true })
    expect(await read(disabled)).toEqual({ summaryGeneration: false })
  })
})
