import { describe, expect, it } from 'vitest'
import i18n from '@/i18n'
import '@/i18n/lazyBundles'
import { goalProblem, isAbort, planProblemMessage, type GoalProblem } from './goalProblems'

const P = 'urn:runnersdiary:problem:'
const t = i18n.getFixedT('en', 'goals')
const user = { isGuest: false, locale: 'en', timeZone: 'Europe/Warsaw' }
const guest = { ...user, isGuest: true }
const message = (body: unknown, context = user) => planProblemMessage(goalProblem(body), t, context)

describe('goalProblem', () => {
  it.each([
    [{ status: 503, type: `${P}feature-unavailable` }, 'featureUnavailable'],
    [{ status: 503, type: `${P}ai-busy`, retryAfterSeconds: 7 }, 'busy'],
    [{ status: 429, type: `${P}ai-quota`, scope: 'global' }, 'quota'],
    [{ status: 409, type: `${P}plan-in-progress` }, 'inProgress'],
    [{ status: 502, type: `${P}plan-failed`, reason: 'timeout' }, 'failed'],
    [{ status: 422, type: `${P}insufficient-history` }, 'insufficientHistory'],
    [{ status: 409, type: `${P}plan-other-goal-active`, goalId: 'g1' }, 'otherGoalActive'],
    [{ status: 409, type: `${P}goal-limit` }, 'goalLimit'],
    [{ status: 409, type: `${P}concurrency-conflict` }, 'concurrency'],
    [{ status: 429, type: `${P}rate-limited` }, 'rateLimited'],
    [{ status: 404 }, 'notFound'],
    [{ status: 400, errors: { goalId: ['The goal must be active.'] } }, 'goalNotEligible'],
    [{ status: 500 }, 'other'],
  ])('classifies %j as %s', (body, kind) => {
    expect(goalProblem(body).kind).toBe(kind)
  })

  it('reads the extensions', () => {
    expect(goalProblem({ type: `${P}plan-other-goal-active`, goalId: 'g1' })).toEqual({
      kind: 'otherGoalActive',
      goalId: 'g1',
    })
    expect(goalProblem({ type: `${P}plan-failed`, reason: 'provider' })).toEqual({ kind: 'failed', reason: 'provider' })
    expect(goalProblem({ type: `${P}plan-failed` })).toEqual({ kind: 'failed', reason: 'invalidOutput' })
    expect(goalProblem({ type: `${P}ai-quota`, scope: 'account', limit: 5, resetsAt: null })).toEqual({
      kind: 'quota',
      scope: 'account',
      limit: 5,
      resetsAt: null,
    })
  })

  it('recognises a network failure as "other" and an abort', () => {
    expect(goalProblem(new TypeError('Failed to fetch')).kind).toBe('other')
    expect(isAbort(new DOMException('x', 'AbortError'))).toBe(true)
    expect(isAbort(new Error('x'))).toBe(false)
  })
})

describe('planProblemMessage', () => {
  it('has a friendly, distinct message per problem and never shows server text', () => {
    const bodies: unknown[] = [
      { type: `${P}feature-unavailable`, title: 'SERVER TEXT' },
      { type: `${P}ai-busy` },
      { type: `${P}ai-busy`, retryAfterSeconds: 12 },
      { type: `${P}ai-quota`, scope: 'global' },
      { type: `${P}ai-quota`, scope: 'account', resetsAt: '2026-11-04T15:40:00Z' },
      { type: `${P}ai-quota`, scope: 'account', resetsAt: null },
      { type: `${P}plan-in-progress` },
      { type: `${P}plan-failed`, reason: 'invalidOutput' },
      { type: `${P}plan-failed`, reason: 'timeout' },
      { type: `${P}plan-failed`, reason: 'provider' },
      { type: `${P}insufficient-history` },
      { type: `${P}plan-other-goal-active` },
      { type: `${P}rate-limited` },
      { type: `${P}rate-limited`, retryAfterSeconds: 30 },
      { type: `${P}concurrency-conflict` },
      { status: 404 },
      { status: 400, errors: { goalId: ['x'] } },
      { status: 500 },
    ]
    const texts = bodies.map((body) => message(body))
    expect(new Set(texts).size).toBe(texts.length)
    for (const text of texts) {
      expect(text).not.toMatch(/SERVER TEXT|\{\{/)
      expect(text.length).toBeGreaterThan(10)
    }
  })

  it('words the quota by scope: account time, demo session, global', () => {
    const account: GoalProblem = { kind: 'quota', scope: 'account', limit: 5, resetsAt: '2026-11-04T15:40:00Z' }
    expect(planProblemMessage(account, t, user)).toMatch(/generate again at .*Nov 4, 2026.*4:40/)
    expect(planProblemMessage(account, t, guest)).toBe('The AI suggestions of this demo session are used up.')
    expect(planProblemMessage({ ...account, scope: 'global' }, t, user)).toMatch(/used up for today/)
  })

  it('shows the retry hint of a busy model', () => {
    expect(message({ type: `${P}ai-busy`, retryAfterSeconds: 12 })).toContain('12 s')
  })
})
