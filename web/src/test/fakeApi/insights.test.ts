import { describe, expect, it } from 'vitest'
import { createFakeApi, DEMO_REPORT_TODAY, demoReport, type InsightsReportDto, type InsightsScenario } from '.'

const BASE = 'http://localhost'

async function report(
  query: string,
  options: { scenario?: InsightsScenario; today?: string; token?: string | null } = {},
) {
  const api = createFakeApi({ today: options.today ?? DEMO_REPORT_TODAY, insights: options.scenario })
  const token = options.token === undefined ? api.issueToken('Guest') : options.token
  const response = await api.handle(
    new Request(`${BASE}/api/insights/report?${query}`, { headers: token ? { Authorization: `Bearer ${token}` } : {} }),
  )
  return { response, body: (await response.json()) as InsightsReportDto & { errors?: Record<string, string[]> } }
}

const DEMO_QUERY = 'from=2026-08-17&to=2026-11-08'

describe('fake insights report', () => {
  it('serves the Domain golden report for the default window, never cached', async () => {
    const { response, body } = await report(DEMO_QUERY)

    expect(response.status).toBe(200)
    expect(response.headers.get('Cache-Control')).toBe('private, no-store')
    expect(body).toEqual(demoReport)
  })

  it('requires a token', async () => {
    const { response } = await report(DEMO_QUERY, { token: null })
    expect(response.status).toBe(401)
  })

  it.each([
    ['', ['from', 'to']],
    ['from=2026-01-01', ['to']],
    ['from=2026-13-01&to=2026-12-01', ['from']],
    ['from=2026-02-01&to=2026-01-01', ['to']],
    ['from=2026-01-01&to=2027-01-02', ['to']],
    ['from=2026-01-01&to=2026-02-01&compare=sometimes', ['compare']],
    ['from=1949-12-31&to=1950-01-02', ['from']],
  ])('answers 400 with errors keyed by parameter for "%s"', async (query, keys) => {
    const { response, body } = await report(query)

    expect(response.status).toBe(400)
    expect(Object.keys(body.errors ?? {}).sort()).toEqual([...keys].sort())
  })

  it('has no comparison for "none" or for a wholly future period', async () => {
    expect((await report(`${DEMO_QUERY}&compare=none`)).body.comparison).toBeNull()
    expect((await report('from=2026-12-07&to=2026-12-13')).body.comparison).toBeNull()
  })

  it('compares a partial month with the same number of elapsed days one month back', async () => {
    const { body } = await report('from=2026-11-01&to=2026-11-30')

    expect(body.period).toMatchObject({ asOf: '2026-11-04', isPartial: true, days: 30, elapsedDays: 4 })
    expect(body.comparison).toMatchObject({ kind: 'previous', from: '2026-10-01', to: '2026-10-04' })
  })

  it('reports zeros for a period without data, and clamps nothing for a past month', async () => {
    const { body } = await report('from=2025-03-01&to=2025-03-31')

    expect(body.totals).toMatchObject({ runCount: 0, distanceMeters: 0, activeDays: 0, averagePaceSecondsPerKm: null })
    expect(body.period.isPartial).toBe(false)
    expect(body.races).toEqual([])
    expect(body.typeDistribution).toEqual([])
    expect(body.load.atAsOf.unavailableReason).toBe('insufficientHistory')
  })

  it('weeks and months are whole ISO weeks / calendar months, the current week incomplete', async () => {
    const { body } = await report('from=2026-10-14&to=2026-11-10')

    expect(body.weeks.map((week) => week.start)).toEqual([
      '2026-10-12',
      '2026-10-19',
      '2026-10-26',
      '2026-11-02',
      '2026-11-09',
    ])
    expect(body.weeks.map((week) => week.complete)).toEqual([true, true, true, false, false])
    expect(body.months.map((month) => month.start)).toEqual(['2026-10-01', '2026-11-01'])
  })

  it('serves the other scenarios', async () => {
    expect((await report(DEMO_QUERY, { scenario: 'empty' })).body.totals.runCount).toBe(0)
    expect((await report(DEMO_QUERY, { scenario: 'insufficientHistory' })).body.load.atAsOf).toMatchObject({
      value: null,
      unavailableReason: 'insufficientHistory',
    })
    expect((await report(DEMO_QUERY, { scenario: 'noChronicLoad' })).body.load.atAsOf).toMatchObject({
      value: null,
      chronic: 0,
      unavailableReason: 'noChronicLoad',
    })
    expect((await report(DEMO_QUERY, { scenario: 'estimated' })).body.dataQuality.estimatedLoadShare).toBeGreaterThan(0)
  })
})
