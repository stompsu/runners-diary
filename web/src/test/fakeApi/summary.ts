import type { components } from '../../api/generated/schema.d.ts'
import { parse } from '../../lib/dates/plainDate.ts'
import { json, problem, validation, type Account } from './handlers.ts'

type S = components['schemas']
export type InsightsSummaryDto = S['InsightsSummaryResponse']
type SummaryContent = S['InsightsSummaryDto']

/**
 * What the fake `GET /api/insights/summary` answers with before anything was generated (M6 plan §3.3):
 * - `none`: nothing stored, generation available.
 * - `fresh` / `stale`: a stored model summary (stale = "newer data is available").
 * - `demo`: the example summary of the default guest view (source `demo`, no createdAt).
 * - `template`: the automatic note of a period that never reaches the model (`notNeeded`).
 * - `notConfigured`: no model in this environment (a demo or no summary at all).
 * - `quotaExhausted`: the account used up its generations (guest: 3 for the account; user: 10 per rolling 24 h).
 * A successful POST stores a fresh summary for the period; any change to the account's runs makes it stale.
 */
export type SummaryScenario = 'none' | 'fresh' | 'stale' | 'demo' | 'template' | 'notConfigured' | 'quotaExhausted'

/** How `POST /api/insights/summary` ends. `slow` succeeds after `slowMs` unless the request is aborted first. */
export type SummaryPostOutcome =
  | 'ok'
  | 'inProgress'
  | 'quotaAccount'
  | 'quotaGlobal'
  | 'rateLimited'
  | 'invalidOutput'
  | 'timeout'
  | 'provider'
  | 'unavailable'
  | 'busy'
  | 'slow'

export interface SummaryOptions {
  scenario?: SummaryScenario
  post?: SummaryPostOutcome
  /** Duration of the `slow` outcome. Default 60 000 ms. */
  slowMs?: number
}

const PREFIX = 'urn:runnersdiary:problem:'
const ALLOWED_BODY = new Set(['from', 'to', 'compare', 'lang', 'regenerate'])
const COMPARE = ['none', 'previous', 'previousYear']
export const GUEST_LIMIT = 3
export const USER_LIMIT = 10

function text(value: string, keys: string[] = []) {
  return { text: value, keys }
}

/** A model summary exercising every kind of value the client formats (the keys follow the fact-sheet grammar). */
export function sampleSummaryContent(source: SummaryContent['source'], createdAt: string | null): SummaryContent {
  const model = source === 'model'
  return {
    source,
    createdAt: model ? createdAt : null,
    metricsVersion: 1,
    factSheetVersion: 1,
    promptVersion: model ? 1 : null,
    model: model ? 'gpt-5.4-mini' : null,
    headline: text('A steady block with more distance than before and a personal best at the {race.1.category}.', [
      'race.1.category',
    ]),
    wentWell: [
      text(
        'You covered {totals.distance} in {totals.runCount} runs, {comparison.delta.distance.percent} on the previous period.',
        ['totals.distance', 'totals.runCount', 'comparison.delta.distance.percent'],
      ),
      text('Your {race.1.category} on {race.1.date} was a personal best in {race.1.resultTime}.', [
        'race.1.category',
        'race.1.date',
        'race.1.resultTime',
      ]),
    ],
    watchOuts: [
      text('Your ACWR peaked at {acwr.peak.value} on {acwr.peak.date}, in the {acwr.peak.band} band.', [
        'acwr.peak.value',
        'acwr.peak.date',
        'acwr.peak.band',
      ]),
    ],
    suggestion: text('Keep easy days easy and raise weekly load gradually.'),
    dataNotes: [text('{dq.estimatedLoadShare} of the load uses estimated effort.', ['dq.estimatedLoadShare'])],
    values: {
      'race.1.category': { number: null, text: 'halfMarathon', unit: 'raceCategory' },
      'totals.distance': { number: 382_600, text: null, unit: 'meters' },
      'totals.runCount': { number: 39, text: null, unit: 'count' },
      'comparison.delta.distance.percent': { number: 14.2, text: null, unit: 'percent' },
      'race.1.date': { number: null, text: '2026-10-11', unit: 'date' },
      'race.1.resultTime': { number: 5_432, text: null, unit: 'seconds' },
      'acwr.peak.value': { number: 1.42, text: null, unit: 'ratio' },
      'acwr.peak.date': { number: null, text: '2026-10-11', unit: 'date' },
      'acwr.peak.band': { number: null, text: 'high', unit: 'band' },
      'dq.estimatedLoadShare': { number: 0.18, text: null, unit: 'share' },
    },
  }
}

function templateContent(): SummaryContent {
  return {
    source: 'template',
    createdAt: null,
    metricsVersion: 1,
    factSheetVersion: 1,
    promptVersion: null,
    model: null,
    headline: text('There are only {totals.runCount} runs in this period, too few for a summary.', ['totals.runCount']),
    wentWell: [],
    watchOuts: [],
    suggestion: null,
    dataNotes: [],
    values: { 'totals.runCount': { number: 2, text: null, unit: 'count' } },
  }
}

interface Stored {
  createdAt: string
  stamp: string
}

/** The fake summary service. State is per account (stored summaries, quota used) and per fake API instance. */
export function createSummaryService(deps: {
  now: () => number
  initial?: SummaryOptions
  runsStamp: (account: Account) => string
}) {
  let scenario: SummaryScenario = deps.initial?.scenario ?? 'none'
  let post: SummaryPostOutcome = deps.initial?.post ?? 'ok'
  let slowMs = deps.initial?.slowMs ?? 60_000
  const stored = new Map<string, Stored>()
  const used = new Map<string, number>()

  const limitOf = (account: Account) => (account.kind === 'Guest' ? GUEST_LIMIT : USER_LIMIT)
  const usedOf = (account: Account) => used.get(account.id) ?? (scenario === 'quotaExhausted' ? limitOf(account) : 0)

  function generation(account: Account): S['SummaryGenerationDto'] {
    if (scenario === 'notConfigured') {
      return { availability: 'notConfigured', used: null, limit: null, resetsAt: null, scope: null }
    }
    if (scenario === 'template' && !stored.size) {
      return { availability: 'notNeeded', used: null, limit: null, resetsAt: null, scope: null }
    }
    const guest = account.kind === 'Guest'
    const count = usedOf(account)
    const limit = limitOf(account)
    const exhausted = count >= limit
    return {
      availability: exhausted ? 'quotaExhausted' : 'available',
      used: count,
      limit,
      resetsAt: guest ? null : new Date(deps.now() + 5 * 3_600_000).toISOString(),
      scope: guest ? 'account' : 'rolling24h',
    }
  }

  function query(url: URL) {
    const from = url.searchParams.get('from')
    const to = url.searchParams.get('to')
    const compare = url.searchParams.get('compare') ?? 'previous'
    const lang = url.searchParams.get('lang') ?? 'en'
    return { from, to, compare, lang }
  }

  function validate(q: { from: string | null; to: string | null; compare: string; lang: string }) {
    const errors: Record<string, string[]> = {}
    if (!q.from || !parse(q.from)) errors.from = ['From must be a valid date (YYYY-MM-DD).']
    if (!q.to || !parse(q.to)) errors.to = ['To must be a valid date (YYYY-MM-DD).']
    if (!COMPARE.includes(q.compare)) errors.compare = ['Compare must be none, previous or previousYear.']
    if (q.lang !== 'en') errors.lang = ['Language must be en.']
    if (q.from && q.to && !errors.from && !errors.to && q.to < q.from) errors.to = ['To must not be before from.']
    return Object.keys(errors).length > 0 ? errors : null
  }

  const keyOf = (account: Account, q: { from: string | null; to: string | null; compare: string }) =>
    `${account.id}|${q.from}|${q.to}|${q.compare}`

  function response(account: Account, q: ReturnType<typeof query>): InsightsSummaryDto {
    const period = { from: q.from!, to: q.to!, compare: q.compare, lang: q.lang }
    const entry = stored.get(keyOf(account, q))
    if (entry) {
      const fresh = entry.stamp === deps.runsStamp(account)
      return {
        period,
        state: fresh ? 'fresh' : 'stale',
        summary: sampleSummaryContent('model', entry.createdAt),
        generation: generation(account),
      }
    }
    const created = new Date(deps.now() - 3_600_000).toISOString()
    const base: Pick<InsightsSummaryDto, 'state' | 'summary'> =
      scenario === 'fresh' || scenario === 'stale'
        ? { state: scenario, summary: sampleSummaryContent('model', created) }
        : scenario === 'demo' || scenario === 'notConfigured'
          ? { state: 'fresh', summary: sampleSummaryContent('demo', null) }
          : scenario === 'template'
            ? { state: 'fresh', summary: templateContent() }
            : { state: 'none', summary: null }
    return { period, ...base, generation: generation(account) }
  }

  function get(account: Account, url: URL): Response {
    const q = query(url)
    const errors = validate(q)
    if (errors) return validation(errors)
    return json(response(account, q), 200, { 'Cache-Control': 'private, no-store' })
  }

  function sleep(ms: number, signal: AbortSignal): Promise<void> {
    return new Promise((resolve, reject) => {
      if (signal.aborted) return reject(new DOMException('Aborted', 'AbortError'))
      const timer = setTimeout(resolve, ms)
      signal.addEventListener(
        'abort',
        () => {
          clearTimeout(timer)
          reject(new DOMException('Aborted', 'AbortError'))
        },
        { once: true },
      )
    })
  }

  async function generate(account: Account, request: Request, body: unknown): Promise<Response> {
    if (typeof body !== 'object' || body === null || Array.isArray(body)) {
      return validation({ body: ['The request body is invalid.'] })
    }
    const record = body as Record<string, unknown>
    const unknown = Object.keys(record).filter((key) => !ALLOWED_BODY.has(key))
    if (unknown.length > 0) return validation({ [unknown[0]!]: ['Unknown member.'] })
    const q = {
      from: typeof record.from === 'string' ? record.from : null,
      to: typeof record.to === 'string' ? record.to : null,
      compare: typeof record.compare === 'string' ? record.compare : 'previous',
      lang: typeof record.lang === 'string' ? record.lang : 'en',
    }
    const errors = validate(q)
    if (errors) return validation(errors)

    if (scenario === 'notConfigured') {
      return problem(503, 'AI summaries are not available.', { type: `${PREFIX}feature-unavailable` })
    }
    switch (post) {
      case 'inProgress':
        return problem(409, 'A summary is already being generated.', { type: `${PREFIX}summary-in-progress` })
      case 'quotaAccount':
        return problem(429, 'Generation limit reached.', {
          type: `${PREFIX}ai-quota`,
          members: {
            scope: 'account',
            limit: limitOf(account),
            resetsAt: account.kind === 'Guest' ? null : new Date(deps.now() + 5 * 3_600_000).toISOString(),
          },
        })
      case 'quotaGlobal':
        return problem(429, 'Daily capacity reached.', {
          type: `${PREFIX}ai-quota`,
          members: { scope: 'global', limit: 200, resetsAt: new Date(deps.now() + 3 * 3_600_000).toISOString() },
        })
      case 'rateLimited':
        return problem(429, 'Too many requests.', { type: `${PREFIX}rate-limited`, headers: { 'Retry-After': '30' } })
      case 'unavailable':
        return problem(503, 'AI summaries are not available.', { type: `${PREFIX}feature-unavailable` })
      case 'busy':
        return problem(503, 'The AI service is busy.', { type: `${PREFIX}ai-busy`, headers: { 'Retry-After': '10' } })
      case 'invalidOutput':
      case 'timeout':
      case 'provider':
        used.set(account.id, usedOf(account) + 1)
        return problem(502, 'The summary could not be generated.', {
          type: `${PREFIX}summary-failed`,
          members: { reason: post },
        })
      case 'slow':
        await sleep(slowMs, request.signal)
        break
      case 'ok':
        break
    }

    if (usedOf(account) >= limitOf(account)) {
      return problem(429, 'Generation limit reached.', {
        type: `${PREFIX}ai-quota`,
        members: {
          scope: account.kind === 'Guest' ? 'account' : 'rolling24h',
          limit: limitOf(account),
          resetsAt: null,
        },
      })
    }
    used.set(account.id, usedOf(account) + 1)
    stored.set(keyOf(account, q), { createdAt: new Date(deps.now()).toISOString(), stamp: deps.runsStamp(account) })
    return json(response(account, { ...q, from: q.from, to: q.to }), 200, { 'Cache-Control': 'private, no-store' })
  }

  return {
    get,
    generate,
    generationEnabled: () => scenario !== 'notConfigured',
    set(next: SummaryScenario, outcome?: SummaryPostOutcome, options: { slowMs?: number } = {}) {
      scenario = next
      if (outcome) post = outcome
      if (options.slowMs !== undefined) slowMs = options.slowMs
    },
    setPost(outcome: SummaryPostOutcome, options: { slowMs?: number } = {}) {
      post = outcome
      if (options.slowMs !== undefined) slowMs = options.slowMs
    },
  }
}
