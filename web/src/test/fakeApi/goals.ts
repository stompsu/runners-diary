import type { components } from '../../api/generated/schema.d.ts'
import { addDays, compare, diffDays, isoWeekday, isValid } from '../../lib/dates/plainDate.ts'
import {
  buildRun,
  json,
  notFound,
  problem,
  validateWrite,
  validation,
  type Account,
  type RunDto,
  type RunWriteRequest,
} from './handlers.ts'
import { RUN_TYPES, typeId } from './seed.ts'

type S = components['schemas']
export type GoalDto = S['GoalDto']
export type GoalWriteRequest = S['GoalWriteRequest']
export type TrainingSuggestionDto = S['TrainingSuggestionDto']
export type GoalEstimateResponse = S['GoalEstimateResponse']
export type TrainingPlanResponse = S['TrainingPlanResponse']

const PREFIX = 'urn:runnersdiary:problem:'
export const FEATURE_UNAVAILABLE = `${PREFIX}feature-unavailable`
export const PLAN_PROBLEMS = {
  goalLimit: `${PREFIX}goal-limit`,
  otherGoalActive: `${PREFIX}plan-other-goal-active`,
  inProgress: `${PREFIX}plan-in-progress`,
  insufficientHistory: `${PREFIX}insufficient-history`,
  failed: `${PREFIX}plan-failed`,
  aiQuota: `${PREFIX}ai-quota`,
  aiBusy: `${PREFIX}ai-busy`,
  rateLimited: `${PREFIX}rate-limited`,
  concurrency: `${PREFIX}concurrency-conflict`,
} as const

export const MAX_GOALS = 50
export const GUEST_MAX_GOALS = 10
export const SHORT_TERM_DAYS = 84
const PLAN_HORIZON_DAYS = 112

/** What the fake holds per account (the DTO without the derived plan counts and term). */
export interface StoredGoal {
  dto: GoalDto
}

export interface GoalRouteContext {
  today: string
  nowIso: string
  nextId: () => string
  nextVersion: () => number
  encodeVersion: (n: number) => string
  /** `client-config.ai.planGeneration`: false = the generate endpoint answers 503 feature-unavailable. */
  planGeneration: boolean
  /** The same checks `POST /api/runs` makes. */
  limitProblem: (account: Account, date: string) => Response | null
}

const SURFACES = ['Road', 'Trail', 'Track', 'Treadmill', 'Mixed']
const TYPES = ['Race', 'Performance', 'Distance']
const STATUSES = ['Active', 'Achieved', 'Archived']
const GOAL_KEYS = new Set([
  'type',
  'title',
  'targetDate',
  'distanceMeters',
  'elevationGainMeters',
  'targetDurationSeconds',
  'surface',
  'eventName',
  'location',
  'raceRunId',
  'notes',
  'status',
  'version',
])
const isObject = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null && !Array.isArray(v)
const isNumber = (v: unknown): v is number => typeof v === 'number' && Number.isFinite(v)

export const goalTerm = (targetDate: string, today: string): GoalDto['term'] =>
  diffDays(today, targetDate) <= SHORT_TERM_DAYS ? 'ShortTerm' : 'LongTerm'

/** The server rules of ADR 0039 §2 (the subset the UI relies on), keyed like the real 400s. */
export function validateGoal(
  raw: unknown,
  mode: 'create' | 'update',
  ctx: { today: string; previousTargetDate?: string; account: Account },
): { errors: Record<string, string[]> } | { value: GoalWriteRequest } {
  const errors: Record<string, string[]> = {}
  const add = (key: string, message: string) => (errors[key] ??= []).push(message)
  if (!isObject(raw)) return { errors: { body: ['The request body is invalid.'] } }
  for (const key of Object.keys(raw)) if (!GOAL_KEYS.has(key)) add('body', 'The request body is invalid.')
  if (errors.body) return { errors }
  const g = raw as unknown as GoalWriteRequest

  if (!TYPES.includes(g.type)) add('type', 'Type is invalid.')
  if (typeof g.title !== 'string' || g.title.trim() === '' || g.title.length > 120)
    add('title', 'Title must be 1 to 120 characters.')
  if (typeof g.targetDate !== 'string' || !isValid(g.targetDate)) add('targetDate', 'Target date must be a valid date.')
  else if (g.targetDate !== ctx.previousTargetDate) {
    const days = diffDays(ctx.today, g.targetDate)
    if (days < 7) add('targetDate', 'Target date must be at least 7 days from today.')
    else if (days > 730) add('targetDate', 'Target date must be at most 730 days from today.')
  }
  if (!isNumber(g.distanceMeters) || g.distanceMeters < 1000 || g.distanceMeters > 250_000)
    add('distanceMeters', 'Distance must be between 1,000 and 250,000 metres.')
  if (
    g.elevationGainMeters != null &&
    (!isNumber(g.elevationGainMeters) || g.elevationGainMeters < 0 || g.elevationGainMeters > 10_000)
  )
    add('elevationGainMeters', 'Elevation gain must be between 0 and 10,000 metres.')
  if (g.surface != null && !SURFACES.includes(g.surface)) add('surface', 'Surface is invalid.')
  if (g.type === 'Performance' && g.targetDurationSeconds == null)
    add('targetDurationSeconds', 'A performance goal needs a target time.')
  if (g.type === 'Distance' && g.targetDurationSeconds != null)
    add('targetDurationSeconds', 'A distance goal has no target time.')
  if (g.targetDurationSeconds != null) {
    if (!isNumber(g.targetDurationSeconds) || g.targetDurationSeconds <= 0) {
      add('targetDurationSeconds', 'Target time must be greater than 0.')
    } else if (isNumber(g.distanceMeters) && g.distanceMeters > 0) {
      const pace = g.targetDurationSeconds / (g.distanceMeters / 1000)
      if (pace < 150 || pace > 900) add('targetDurationSeconds', 'The implied pace must be between 2:30 and 15:00 /km.')
    }
  }
  if (g.type === 'Race' && (typeof g.eventName !== 'string' || g.eventName.trim() === ''))
    add('eventName', 'A race goal needs an event name.')
  if (g.eventName != null && g.eventName.length > 150) add('eventName', 'Event name must be at most 150 characters.')
  if (g.notes != null && g.notes.length > 1000) add('notes', 'Notes must be at most 1000 characters.')
  if (g.raceRunId != null) {
    const run = ctx.account.runs.get(g.raceRunId)
    const type = RUN_TYPES.find((t) => t.id === run?.runTypeId)
    if (!run || run.status !== 'Planned' || type?.category !== 'Race' || run.date !== g.targetDate)
      add('raceRunId', 'The linked run must be your planned race run on the target date.')
  }
  if (g.status != null && !STATUSES.includes(g.status)) add('status', 'Status is invalid.')
  if (mode === 'update' && (g.status == null || !g.version)) {
    if (g.status == null) add('status', 'Status is required.')
    if (!g.version) add('version', 'Version is required.')
  }
  return Object.keys(errors).length ? { errors } : { value: g }
}

// ---------- views ----------

const runningTypes = new Set(RUN_TYPES.filter((t) => t.countsTowardRunningDistance).map((t) => t.id))

/** The DTO the API would return: plan counts and `raceRunDate` are derived from the account's current rows. */
export function goalView(account: Account, goal: GoalDto, today: string): GoalDto {
  const suggestions = [...account.suggestions.values()].filter((s) => s.goalId === goal.id)
  const accepted = [...account.runs.values()].filter((r) => r.goalId === goal.id).length
  const raceRun = goal.raceRunId ? account.runs.get(goal.raceRunId) : undefined
  return {
    ...goal,
    term: goalTerm(goal.targetDate, today),
    raceRunDate: raceRun?.date ?? null,
    plan: goal.plan
      ? {
          ...goal.plan,
          pendingCount: suggestions.filter((s) => s.status === 'Pending').length,
          dismissedCount: suggestions.filter((s) => s.status === 'Dismissed').length,
          acceptedCount: accepted,
        }
      : null,
  }
}

const byActiveThenDate = (a: GoalDto, b: GoalDto) =>
  (a.status === 'Active' ? 0 : 1) - (b.status === 'Active' ? 0 : 1) || compare(a.targetDate, b.targetDate)

// ---------- estimate ----------

const runSeconds = (run: RunDto) => run.race?.resultTimeSeconds ?? run.durationSeconds
const effort = (meters: number, elevation: number | null | undefined) => meters + 10 * (elevation ?? 0)

/** A simplified `GoalEstimator`: Riegel over km-effort from the closest completed race, else no prediction. */
export function estimateGoal(account: Account, today: string, body: S['EstimateGoalRequest']): GoalEstimateResponse {
  const daysToGoal = diffDays(today, body.targetDate)
  const weeksToGoal = Math.floor(daysToGoal / 7)
  const goalEffort = effort(body.distanceMeters, body.elevationGainMeters)
  const candidates = [...account.runs.values()].filter(
    (run) =>
      run.status === 'Completed' &&
      RUN_TYPES.find((t) => t.id === run.runTypeId)?.category === 'Race' &&
      (run.distanceMeters ?? 0) >= 3000 &&
      runSeconds(run) != null &&
      diffDays(run.date, today) <= 364,
  )
  const basis = [...candidates].sort(
    (a, b) =>
      Math.abs(Math.log(goalEffort / effort(a.distanceMeters!, a.elevationGainMeters))) -
        Math.abs(Math.log(goalEffort / effort(b.distanceMeters!, b.elevationGainMeters))) || compare(b.date, a.date),
  )[0]
  const ratio = basis ? goalEffort / effort(basis.distanceMeters!, basis.elevationGainMeters) : 0
  const durationSeconds = basis ? Math.round(runSeconds(basis)! * Math.pow(ratio, 1.06)) : 0
  const minimumWeeks =
    body.distanceMeters < 15_000 ? 4 : body.distanceMeters < 30_000 ? 8 : body.distanceMeters <= 42_200 ? 12 : 16

  const recent = [...account.runs.values()].filter(
    (run) =>
      run.status === 'Completed' &&
      runningTypes.has(run.runTypeId) &&
      diffDays(run.date, today) >= 0 &&
      diffDays(run.date, today) < 28,
  )
  const recentWeeklyDistanceMeters = recent.reduce((sum, run) => sum + (run.distanceMeters ?? 0), 0) / 4

  const warnings: GoalEstimateResponse['warnings'] = []
  if (!basis) warnings.push({ code: 'noRecentEffort', value: null })
  else if (body.targetDurationSeconds != null && body.targetDurationSeconds < 0.95 * durationSeconds) {
    warnings.push({
      code: 'targetMuchFasterThanPredicted',
      value: Math.round((1 - body.targetDurationSeconds / durationSeconds) * 1000) / 10,
    })
  }
  if (weeksToGoal < minimumWeeks) warnings.push({ code: 'tooLittleTime', value: minimumWeeks - weeksToGoal })
  if (body.distanceMeters >= 21_000 && recentWeeklyDistanceMeters < 0.5 * body.distanceMeters)
    warnings.push({ code: 'lowWeeklyVolume', value: Math.round(recentWeeklyDistanceMeters) })
  if (daysToGoal > PLAN_HORIZON_DAYS) warnings.push({ code: 'beyondPlanHorizon', value: null })

  return {
    today,
    daysToGoal,
    weeksToGoal,
    term: goalTerm(body.targetDate, today),
    minimumWeeks,
    recentWeeklyDistanceMeters,
    prediction: basis
      ? {
          durationSeconds,
          paceSecondsPerKm: Math.round(durationSeconds / (body.distanceMeters / 1000)),
          confidence: basis.race && ratio >= 0.5 && ratio <= 2 ? 'High' : 'Low',
          basis: {
            runId: basis.id,
            date: basis.date,
            kind: 'Race',
            distanceMeters: basis.distanceMeters!,
            durationSeconds: runSeconds(basis)!,
            elevationGainMeters: basis.elevationGainMeters,
          },
        }
      : null,
    warnings,
  }
}

// ---------- plan generation ----------

const WEEK_TEMPLATE = [
  {
    weekday: 2,
    code: 'easy',
    intensity: 'Easy',
    share: 0.18,
    description: 'Relaxed aerobic run, conversational pace.',
  },
  { weekday: 4, code: 'tempo', intensity: 'Tempo', share: 0.2, description: 'Steady tempo block in the middle.' },
  { weekday: 6, code: 'easy', intensity: 'Easy', share: 0.15, description: 'Short easy run, loosen the legs.' },
  { weekday: 7, code: 'long', intensity: 'Steady', share: 0.37, description: 'Long run at an even effort.' },
] as const

function phaseOf(weeksLeft: number): S['TrainingPhase'] {
  if (weeksLeft <= 1) return 'Taper'
  if (weeksLeft <= 3) return 'Peak'
  if (weeksLeft <= 8) return 'Build'
  return 'Base'
}

const BLOCKING_CODES = new Set(RUN_TYPES.filter((t) => t.category !== 'Other' || t.isRestDay).map((t) => t.id))

/** True when the account has at least 3 completed running sessions in the last 84 days (the 422 check). */
function hasHistory(account: Account, today: string): boolean {
  return (
    [...account.runs.values()].filter(
      (run) =>
        run.status === 'Completed' &&
        runningTypes.has(run.runTypeId) &&
        diffDays(run.date, today) >= 0 &&
        diffDays(run.date, today) <= 83,
    ).length >= 3
  )
}

/** A believable plan: four sessions per week up to the goal (at most 16 weeks), colliding days skipped and counted. */
export function buildPlan(
  account: Account,
  goal: GoalDto,
  today: string,
  ctx: GoalRouteContext,
  generationId: string,
): { suggestions: TrainingSuggestionDto[]; dropped: number; horizonEnd: string; capped: boolean } {
  const start = addDays(today, 1)
  const horizon = addDays(today, PLAN_HORIZON_DAYS)
  const capped = compare(goal.targetDate, horizon) > 0
  const end = capped ? horizon : goal.targetDate
  const blocked = new Set(
    [...account.runs.values()].filter((run) => BLOCKING_CODES.has(run.runTypeId)).map((run) => run.date),
  )
  for (const other of account.goals.values()) {
    if (other.dto.id !== goal.id && other.dto.status === 'Active') blocked.add(other.dto.targetDate)
  }
  const peak = Math.max(goal.distanceMeters * 1.1, 8000)
  const suggestions: TrainingSuggestionDto[] = []
  let dropped = 0
  for (let date = start; compare(date, end) <= 0; date = addDays(date, 1)) {
    const slot = WEEK_TEMPLATE.find((entry) => entry.weekday === isoWeekday(date))
    if (!slot) continue
    if (blocked.has(date)) {
      dropped += 1
      continue
    }
    const weeksLeft = Math.floor(diffDays(date, goal.targetDate) / 7)
    const phase = phaseOf(weeksLeft)
    const factor = phase === 'Taper' ? 0.6 : phase === 'Peak' ? 1 : phase === 'Build' ? 0.85 : 0.7
    const distanceMeters = Math.max(3000, Math.round((peak * slot.share * 2.2 * factor) / 100) * 100)
    suggestions.push({
      id: ctx.nextId(),
      goalId: goal.id,
      generationId,
      date,
      runTypeId: typeId(slot.code),
      distanceMeters,
      durationSeconds: Math.round((distanceMeters / 1000) * 330),
      intensity: slot.intensity,
      targetPaceMinSecondsPerKm: slot.intensity === 'Easy' ? 330 : 300,
      targetPaceMaxSecondsPerKm: slot.intensity === 'Easy' ? 360 : 320,
      description: slot.description,
      phase,
      status: 'Pending',
    })
  }
  return { suggestions, dropped, horizonEnd: end, capped }
}

function otherGoalWithPending(account: Account, goalId: string): GoalDto | null {
  for (const other of account.goals.values()) {
    if (other.dto.id === goalId) continue
    for (const s of account.suggestions.values()) {
      if (s.goalId === other.dto.id && s.status === 'Pending') return other.dto
    }
  }
  return null
}

async function generatePlan(
  ctx: GoalRouteContext,
  account: Account,
  goalId: string,
  readBody: () => Promise<Response | unknown>,
): Promise<Response> {
  const stored = account.goals.get(goalId)
  if (!stored) return notFound()
  const goal = stored.dto
  const body = await readBody()
  if (body instanceof Response) return body
  let replaceOtherGoals = false
  if (body !== undefined) {
    if (!isObject(body) || Object.keys(body).some((key) => key !== 'replaceOtherGoals'))
      return validation({ body: ['The request body is invalid.'] })
    replaceOtherGoals = body.replaceOtherGoals === true
  }
  if (goal.status !== 'Active' || diffDays(ctx.today, goal.targetDate) < 2)
    return validation({ goalId: ['The goal must be active and at least 2 days away.'] })
  if (!ctx.planGeneration) return problem(503, 'AI suggestions are not available.', { type: FEATURE_UNAVAILABLE })
  if (!hasHistory(account, ctx.today))
    return problem(422, 'Not enough history.', { type: PLAN_PROBLEMS.insufficientHistory })
  const holder = otherGoalWithPending(account, goalId)
  if (holder && !replaceOtherGoals)
    return problem(409, 'Another goal has suggestions.', {
      type: PLAN_PROBLEMS.otherGoalActive,
      members: { goalId: holder.id },
    })

  const replacedGoalIds: string[] = []
  if (replaceOtherGoals) {
    for (const [id, s] of account.suggestions) {
      if (s.goalId !== goalId) {
        account.suggestions.delete(id)
        if (!replacedGoalIds.includes(s.goalId)) replacedGoalIds.push(s.goalId)
      }
    }
  }
  for (const [id, s] of account.suggestions) if (s.goalId === goalId) account.suggestions.delete(id)

  const generationId = ctx.nextId()
  const plan = buildPlan(account, goal, ctx.today, ctx, generationId)
  for (const s of plan.suggestions) account.suggestions.set(s.id, s)
  const updated: GoalDto = {
    ...goal,
    plan: {
      source: 'Model',
      generatedAt: ctx.nowIso,
      generationId,
      notes: 'Build gradually and keep the easy days easy.',
      promptVersion: 2,
      model: 'fake-model',
      horizonEnd: plan.horizonEnd,
      horizonCapped: plan.capped,
      outdated: false,
      pendingCount: 0,
      dismissedCount: 0,
      acceptedCount: 0,
    },
    version: ctx.encodeVersion(ctx.nextVersion()),
    updatedAt: ctx.nowIso,
  }
  stored.dto = updated
  const response: TrainingPlanResponse = {
    goal: goalView(account, updated, ctx.today),
    suggestions: plan.suggestions,
    droppedCount: plan.dropped,
    replacedGoalIds,
  }
  return json(response, 200, { 'Cache-Control': 'private, no-store' })
}

// ---------- routes ----------

type ReadJson = () => Promise<{ value: unknown } | { response: Response }>

/** `/api/goals...`; returns null for an unknown route. */
export async function routeGoals(
  ctx: GoalRouteContext,
  request: Request,
  account: Account,
  path: string,
  readJson: ReadJson,
): Promise<Response | null> {
  const method = request.method.toUpperCase()
  const noStore = { 'Cache-Control': 'private, no-store' }

  if (path === '/api/goals') {
    if (method === 'GET') {
      const items = [...account.goals.values()].map((g) => goalView(account, g.dto, ctx.today)).sort(byActiveThenDate)
      return json({ items }, 200, noStore)
    }
    if (method === 'POST') {
      const body = await readJson()
      if ('response' in body) return body.response
      const result = validateGoal(body.value, 'create', { today: ctx.today, account })
      if ('errors' in result) return validation(result.errors)
      const max = account.kind === 'Guest' ? GUEST_MAX_GOALS : MAX_GOALS
      if (account.goals.size >= max) return problem(409, 'Goal limit reached.', { type: PLAN_PROBLEMS.goalLimit })
      const g = result.value
      const id = ctx.nextId()
      const dto: GoalDto = {
        id,
        type: g.type,
        title: g.title.trim(),
        targetDate: g.targetDate,
        distanceMeters: g.distanceMeters,
        elevationGainMeters: g.elevationGainMeters ?? null,
        targetDurationSeconds: g.targetDurationSeconds ?? null,
        surface: g.surface ?? null,
        eventName: g.eventName ?? null,
        location: g.location ?? null,
        raceRunId: g.raceRunId ?? null,
        notes: g.notes ?? null,
        status: g.status ?? 'Active',
        term: goalTerm(g.targetDate, ctx.today),
        raceRunDate: null,
        plan: null,
        version: ctx.encodeVersion(ctx.nextVersion()),
        createdAt: ctx.nowIso,
        updatedAt: ctx.nowIso,
      }
      account.goals.set(id, { dto })
      return json(goalView(account, dto, ctx.today), 201, { Location: `/api/goals/${id}` })
    }
    return null
  }

  if (path === '/api/goals/estimate' && method === 'POST') {
    const body = await readJson()
    if ('response' in body) return body.response
    const v = body.value
    const errors: Record<string, string[]> = {}
    if (!isObject(v)) return validation({ body: ['The request body is invalid.'] })
    if (!TYPES.includes(v.type as string)) errors.type = ['Type is invalid.']
    if (!isNumber(v.distanceMeters) || v.distanceMeters < 1000 || v.distanceMeters > 250_000)
      errors.distanceMeters = ['Distance must be between 1,000 and 250,000 metres.']
    if (typeof v.targetDate !== 'string' || !isValid(v.targetDate)) errors.targetDate = ['Target date must be valid.']
    else if (diffDays(ctx.today, v.targetDate) < 0 || diffDays(ctx.today, v.targetDate) > 730)
      errors.targetDate = ['Target date must be within the next 730 days.']
    if (Object.keys(errors).length) return validation(errors)
    return json(estimateGoal(account, ctx.today, v as unknown as S['EstimateGoalRequest']), 200, noStore)
  }

  const plan = /^\/api\/goals\/([^/]+)\/plan$/.exec(path)
  if (plan && method === 'POST') {
    return generatePlan(ctx, account, plan[1]!, async () => {
      const text = await request.text()
      if (text.trim() === '') return undefined
      try {
        return JSON.parse(text) as unknown
      } catch {
        return validation({ body: ['The request body is invalid.'] })
      }
    })
  }

  const one = /^\/api\/goals\/([^/]+)$/.exec(path)
  if (!one) return null
  const id = one[1]!
  const stored = account.goals.get(id)
  if (method === 'GET') return stored ? json(goalView(account, stored.dto, ctx.today), 200, noStore) : notFound()
  if (method === 'DELETE') {
    if (!stored) return notFound()
    account.goals.delete(id)
    for (const [sid, s] of account.suggestions) if (s.goalId === id) account.suggestions.delete(sid)
    for (const [rid, run] of account.runs) if (run.goalId === id) account.runs.set(rid, { ...run, goalId: null })
    return new Response(null, { status: 204 })
  }
  if (method === 'PUT') {
    if (!stored) return notFound()
    const body = await readJson()
    if ('response' in body) return body.response
    const result = validateGoal(body.value, 'update', {
      today: ctx.today,
      previousTargetDate: stored.dto.targetDate,
      account,
    })
    if ('errors' in result) return validation(result.errors)
    const g = result.value
    if (g.version !== stored.dto.version)
      return problem(409, 'Concurrency conflict.', { type: PLAN_PROBLEMS.concurrency })
    const status = g.status ?? stored.dto.status
    const planChanged =
      g.type !== stored.dto.type ||
      g.targetDate !== stored.dto.targetDate ||
      g.distanceMeters !== stored.dto.distanceMeters ||
      (g.elevationGainMeters ?? null) !== stored.dto.elevationGainMeters ||
      (g.targetDurationSeconds ?? null) !== stored.dto.targetDurationSeconds ||
      (g.surface ?? null) !== stored.dto.surface
    if (status !== 'Active')
      for (const [sid, s] of account.suggestions) if (s.goalId === id) account.suggestions.delete(sid)
    const dto: GoalDto = {
      ...stored.dto,
      type: g.type,
      title: g.title.trim(),
      targetDate: g.targetDate,
      distanceMeters: g.distanceMeters,
      elevationGainMeters: g.elevationGainMeters ?? null,
      targetDurationSeconds: g.targetDurationSeconds ?? null,
      surface: g.surface ?? null,
      eventName: g.eventName ?? null,
      location: g.location ?? null,
      raceRunId: g.raceRunId ?? null,
      notes: g.notes ?? null,
      status,
      plan: stored.dto.plan && planChanged ? { ...stored.dto.plan, outdated: true } : stored.dto.plan,
      version: ctx.encodeVersion(ctx.nextVersion()),
      updatedAt: ctx.nowIso,
    }
    stored.dto = dto
    return json(goalView(account, dto, ctx.today))
  }
  return null
}

/** `/api/training-suggestions...`; returns null for an unknown route. */
export async function routeSuggestions(
  ctx: GoalRouteContext,
  request: Request,
  account: Account,
  path: string,
): Promise<Response | null> {
  const method = request.method.toUpperCase()

  if (path === '/api/training-suggestions' && method === 'GET') {
    const url = new URL(request.url)
    const from = url.searchParams.get('from') ?? ''
    const to = url.searchParams.get('to') ?? ''
    const errors: Record<string, string[]> = {}
    if (!isValid(from)) errors.from = ['From must be a valid date.']
    if (!isValid(to)) errors.to = ['To must be a valid date.']
    if (Object.keys(errors).length) return validation(errors)
    if (compare(to, from) < 0) return validation({ to: ['To must not be before from.'] })
    if (diffDays(from, to) + 1 > 92) return validation({ to: ['The range is at most 92 days.'] })
    const includeDismissed = url.searchParams.get('includeDismissed') === 'true'
    const items = [...account.suggestions.values()]
      .filter(
        (s) =>
          compare(s.date, from) >= 0 &&
          compare(s.date, to) <= 0 &&
          (includeDismissed || s.status === 'Pending') &&
          account.goals.get(s.goalId)?.dto.status === 'Active',
      )
      .sort((a, b) => compare(a.date, b.date) || a.goalId.localeCompare(b.goalId))
    return json({ from, to, items }, 200, { 'Cache-Control': 'private, no-store' })
  }

  const action = /^\/api\/training-suggestions\/([^/]+)\/(accept|dismiss)$/.exec(path)
  if (!action || method !== 'POST') return null
  const suggestion = account.suggestions.get(action[1]!)
  if (!suggestion) return notFound()

  if (action[2] === 'dismiss') {
    const dismissed: TrainingSuggestionDto = { ...suggestion, status: 'Dismissed' }
    account.suggestions.set(dismissed.id, dismissed)
    return json(dismissed)
  }

  const text = await request.text()
  let parsed: unknown = {}
  if (text.trim() !== '' && text.trim() !== 'null') {
    try {
      parsed = JSON.parse(text)
    } catch {
      return validation({ body: ['The request body is invalid.'] })
    }
  }
  if (!isObject(parsed) || Object.keys(parsed).some((key) => key !== 'run'))
    return validation({ body: ['The request body is invalid.'] })

  let write: RunWriteRequest
  if (parsed.run != null) {
    const result = validateWrite(parsed.run, 'create')
    if ('errors' in result) {
      const prefixed: Record<string, string[]> = {}
      for (const [key, messages] of Object.entries(result.errors)) prefixed[`run.${key}`] = messages
      return validation(prefixed)
    }
    write = result.value
  } else {
    write = {
      date: suggestion.date,
      startTime: null,
      timeZoneId: account.timeZoneId,
      status: 'Planned',
      runTypeId: suggestion.runTypeId,
      title: null,
      notes: suggestion.description,
      distanceMeters: suggestion.distanceMeters,
      durationSeconds: suggestion.durationSeconds,
    }
  }
  const limit = ctx.limitProblem(account, write.date)
  if (limit) return limit
  const runId = ctx.nextId()
  const run: RunDto = {
    ...buildRun(runId, write, account, ctx.nowIso, ctx.nextVersion()),
    goalId: suggestion.goalId,
  }
  account.runs.set(runId, run)
  account.suggestions.delete(suggestion.id)
  return json(run, 201, { Location: `/api/runs/${runId}` })
}

/** A goal for tests: an active Distance 10K, 10 weeks out (a short-term goal) unless overridden. */
export function seedGoal(
  today: string,
  patch: Partial<GoalDto> = {},
): Omit<GoalDto, 'id' | 'version' | 'createdAt' | 'updatedAt'> {
  const targetDate = patch.targetDate ?? addDays(today, 70)
  return {
    type: 'Distance',
    title: 'Autumn 10K',
    targetDate,
    distanceMeters: 10_000,
    elevationGainMeters: null,
    targetDurationSeconds: null,
    surface: null,
    eventName: null,
    location: null,
    raceRunId: null,
    notes: null,
    status: 'Active',
    term: goalTerm(targetDate, today),
    raceRunDate: null,
    plan: null,
    ...patch,
  }
}
