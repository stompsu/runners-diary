import { describe, expect, it } from 'vitest'
import { createFakeApi, PLAN_PROBLEMS, typeId, type RunWriteRequest } from '.'

const BASE = 'http://localhost'
const HISTORY: RunWriteRequest[] = ['2026-10-15', '2026-10-20', '2026-10-25'].map((date) => ({
  date,
  status: 'Completed',
  runTypeId: typeId('easy'),
  distanceMeters: 8000,
  durationSeconds: 2700,
}))

function setup(options: Parameters<typeof createFakeApi>[0] = {}) {
  const api = createFakeApi({ goalsEnabled: true, ...options })
  const token = api.issueToken('Guest', { seed: HISTORY })
  const call = (method: string, path: string, body?: unknown) =>
    api.handle(
      new Request(BASE + path, {
        method,
        headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
        body: body === undefined ? undefined : JSON.stringify(body),
      }),
    )
  return { api, token, call }
}

const goalBody = (over: Record<string, unknown> = {}) => ({
  type: 'Distance',
  title: 'Autumn 10K',
  targetDate: '2026-12-20',
  distanceMeters: 10_000,
  ...over,
})

describe('fake API: goals (M9)', () => {
  it('answers 503 feature-unavailable for both groups while goals are off, and says so in client-config', async () => {
    const { call, api } = setup({ goalsEnabled: false })
    for (const path of ['/api/goals', '/api/training-suggestions?from=2026-11-01&to=2026-11-30']) {
      const response = await call('GET', path)
      expect(response.status).toBe(503)
      expect((await response.json()).type).toContain('feature-unavailable')
    }
    const config = await (await api.handle(new Request(`${BASE}/api/client-config`))).json()
    expect(config).toMatchObject({ goals: { enabled: false }, ai: { planGeneration: false } })
  })

  it('creates, reads, updates with a version, and deletes a goal', async () => {
    const { call } = setup()
    const created = await call('POST', '/api/goals', goalBody())
    expect(created.status).toBe(201)
    const goal = await created.json()
    expect(goal).toMatchObject({ status: 'Active', term: 'ShortTerm', plan: null })

    const stale = await call('PUT', `/api/goals/${goal.id}`, { ...goalBody(), status: 'Active', version: 'old' })
    expect(stale.status).toBe(409)
    const updated = await call('PUT', `/api/goals/${goal.id}`, {
      ...goalBody({ title: 'Renamed' }),
      status: 'Active',
      version: goal.version,
    })
    expect(updated.status).toBe(200)
    expect((await call('DELETE', `/api/goals/${goal.id}`)).status).toBe(204)
    expect((await call('GET', `/api/goals/${goal.id}`)).status).toBe(404)
  })

  it('validates like the server (camelCase keys) and caps guests at 10 goals', async () => {
    const { call } = setup()
    const bad = await call(
      'POST',
      '/api/goals',
      goalBody({ type: 'Performance', targetDate: '2026-11-05', distanceMeters: 500 }),
    )
    expect(bad.status).toBe(400)
    expect(Object.keys((await bad.json()).errors).sort()).toEqual([
      'distanceMeters',
      'targetDate',
      'targetDurationSeconds',
    ])
    for (let i = 0; i < 10; i += 1) expect((await call('POST', '/api/goals', goalBody())).status).toBe(201)
    const over = await call('POST', '/api/goals', goalBody())
    expect(over.status).toBe(409)
    expect((await over.json()).type).toBe(PLAN_PROBLEMS.goalLimit)
  })

  it('generates a plan, regenerates, accepts, dismisses and keeps suggestions out of runs', async () => {
    const { call, api, token } = setup()
    const goal = await (await call('POST', '/api/goals', goalBody())).json()
    const generated = await call('POST', `/api/goals/${goal.id}/plan`)
    expect(generated.status).toBe(200)
    const plan = await generated.json()
    expect(plan.suggestions.length).toBeGreaterThan(5)
    expect(plan.goal.plan).toMatchObject({ source: 'Model', pendingCount: plan.suggestions.length, outdated: false })
    expect(api.runsOf(token)).toHaveLength(HISTORY.length)

    const [first, second] = plan.suggestions
    const accepted = await call('POST', `/api/training-suggestions/${first.id}/accept`)
    expect(accepted.status).toBe(201)
    expect(await accepted.json()).toMatchObject({ goalId: goal.id, status: 'Planned', date: first.date })
    const dismissed = await call('POST', `/api/training-suggestions/${second.id}/dismiss`)
    expect((await dismissed.json()).status).toBe('Dismissed')

    const list = await (
      await call('GET', `/api/training-suggestions?from=2026-11-01&to=2026-12-31&includeDismissed=true`)
    ).json()
    expect(list.items.find((s: { id: string }) => s.id === first.id)).toBeUndefined()
    expect(list.items.find((s: { id: string }) => s.id === second.id).status).toBe('Dismissed')
    const pending = await (await call('GET', `/api/training-suggestions?from=2026-11-01&to=2026-12-31`)).json()
    expect(pending.items.some((s: { status: string }) => s.status === 'Dismissed')).toBe(false)

    // Regenerating replaces pending and dismissed ones, never runs.
    const again = await (await call('POST', `/api/goals/${goal.id}/plan`)).json()
    expect(again.goal.plan.acceptedCount).toBe(1)
    expect(api.runsOf(token).filter((r) => r.goalId === goal.id)).toHaveLength(1)
  })

  it('applies the generate checks: 422 without history, 409 for another goal, 400 for a goal too close, 503 when off', async () => {
    const empty = createFakeApi({ goalsEnabled: true })
    const emptyToken = empty.issueToken('Guest', { seed: [] })
    const goal = empty.addGoal(emptyToken)
    const response = await empty.handle(
      new Request(`${BASE}/api/goals/${goal.id}/plan`, {
        method: 'POST',
        headers: { Authorization: `Bearer ${emptyToken}` },
      }),
    )
    expect(response.status).toBe(422)

    const { call, api, token } = setup()
    const a = await (await call('POST', '/api/goals', goalBody({ title: 'A' }))).json()
    const b = await (await call('POST', '/api/goals', goalBody({ title: 'B', targetDate: '2027-02-20' }))).json()
    expect((await call('POST', `/api/goals/${a.id}/plan`)).status).toBe(200)
    const blocked = await call('POST', `/api/goals/${b.id}/plan`)
    expect(blocked.status).toBe(409)
    expect(await blocked.json()).toMatchObject({ type: PLAN_PROBLEMS.otherGoalActive, goalId: a.id })
    const replaced = await (await call('POST', `/api/goals/${b.id}/plan`, { replaceOtherGoals: true })).json()
    expect(replaced.replacedGoalIds).toEqual([a.id])

    const soon = api.addGoal(token, { targetDate: '2026-11-04' })
    expect((await call('POST', `/api/goals/${soon.id}/plan`)).status).toBe(400)
    api.setPlanGeneration(false)
    expect((await call('POST', `/api/goals/${a.id}/plan`)).status).toBe(503)
  })

  it('estimates from a completed race and warns', async () => {
    const api = createFakeApi({ goalsEnabled: true })
    const token = api.issueToken('Guest', {
      seed: [
        {
          date: '2026-10-10',
          status: 'Completed',
          runTypeId: typeId('roadRace'),
          distanceMeters: 10_000,
          durationSeconds: 3000,
          race: { name: 'Race', distanceCategory: 'TenK', resultTimeSeconds: 3000 },
        },
      ],
    })
    const estimate = await (
      await api.handle(
        new Request(`${BASE}/api/goals/estimate`, {
          method: 'POST',
          headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
          body: JSON.stringify({
            type: 'Performance',
            distanceMeters: 10_000,
            targetDate: '2026-11-10',
            targetDurationSeconds: 2400,
          }),
        }),
      )
    ).json()
    expect(estimate.prediction).toMatchObject({ durationSeconds: 3000, paceSecondsPerKm: 300, confidence: 'High' })
    expect(estimate.warnings.map((w: { code: string }) => w.code)).toEqual([
      'targetMuchFasterThanPredicted',
      'tooLittleTime',
    ])
  })
})
