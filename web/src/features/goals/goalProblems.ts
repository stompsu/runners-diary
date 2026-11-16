import type { TFunction } from 'i18next'
import { parseProblem, type ApiProblem } from '@/lib/api/problem'

const PREFIX = 'urn:runnersdiary:problem:'

export type PlanFailureReason = 'invalidOutput' | 'timeout' | 'provider'

/** What a failed goal write or plan generation means for the UI (plan §4.3, the AI agent's problem table). */
export type GoalProblem =
  | { kind: 'featureUnavailable' }
  | { kind: 'busy'; retryAfterSeconds: number | null }
  | { kind: 'quota'; scope: 'account' | 'global'; limit: number | null; resetsAt: string | null }
  | { kind: 'inProgress' }
  | { kind: 'failed'; reason: PlanFailureReason }
  | { kind: 'insufficientHistory' }
  | { kind: 'otherGoalActive'; goalId: string | null }
  | { kind: 'goalNotEligible' }
  | { kind: 'goalLimit' }
  | { kind: 'concurrency' }
  | { kind: 'rateLimited'; retryAfterSeconds: number | null }
  | { kind: 'notFound' }
  | { kind: 'other'; problem: ApiProblem }

function record(error: unknown): Record<string, unknown> {
  return typeof error === 'object' && error !== null ? (error as Record<string, unknown>) : {}
}

/** `true` for the rejection of an aborted request (the user pressed Cancel). */
export function isAbort(error: unknown): boolean {
  return error instanceof DOMException && error.name === 'AbortError'
}

/**
 * Classifies an error body. The shared `parseProblem` knows only the run conflicts, so the M9 types are read here by
 * their `type` URN and extensions (`goalId`, `scope`, `limit`, `resetsAt`, `reason`).
 */
export function goalProblem(error: unknown): GoalProblem {
  const body = record(error)
  const type = typeof body.type === 'string' ? body.type : ''
  const retry = typeof body.retryAfterSeconds === 'number' ? body.retryAfterSeconds : null
  switch (type) {
    case `${PREFIX}feature-unavailable`:
      return { kind: 'featureUnavailable' }
    case `${PREFIX}ai-busy`:
      return { kind: 'busy', retryAfterSeconds: retry }
    case `${PREFIX}ai-quota`:
      return {
        kind: 'quota',
        scope: body.scope === 'global' ? 'global' : 'account',
        limit: typeof body.limit === 'number' ? body.limit : null,
        resetsAt: typeof body.resetsAt === 'string' ? body.resetsAt : null,
      }
    case `${PREFIX}plan-in-progress`:
      return { kind: 'inProgress' }
    case `${PREFIX}plan-failed`: {
      const reason = body.reason
      return { kind: 'failed', reason: reason === 'timeout' || reason === 'provider' ? reason : 'invalidOutput' }
    }
    case `${PREFIX}insufficient-history`:
      return { kind: 'insufficientHistory' }
    case `${PREFIX}plan-other-goal-active`:
      return { kind: 'otherGoalActive', goalId: typeof body.goalId === 'string' ? body.goalId : null }
    case `${PREFIX}goal-limit`:
      return { kind: 'goalLimit' }
    case `${PREFIX}concurrency-conflict`:
      return { kind: 'concurrency' }
    case `${PREFIX}rate-limited`:
      return { kind: 'rateLimited', retryAfterSeconds: retry }
    default:
  }
  const problem = parseProblem(error)
  if (problem.kind === 'notFound') return { kind: 'notFound' }
  if (problem.kind === 'concurrency') return { kind: 'concurrency' }
  if (problem.kind === 'rateLimited') return { kind: 'rateLimited', retryAfterSeconds: problem.retryAfterSeconds }
  if (problem.kind === 'validation' && problem.fieldErrors.goalId) return { kind: 'goalNotEligible' }
  return { kind: 'other', problem }
}

/** "3:40 PM" or "Nov 17, 3:40 PM" when it is not today, in the user's profile zone. */
export function formatResetTime(resetsAt: string, locale: string, timeZone: string): string {
  const date = new Date(resetsAt)
  if (Number.isNaN(date.getTime())) return resetsAt
  return new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeStyle: 'short', timeZone }).format(date)
}

/** The friendly message of a plan-generation problem (`goals:errors.*`); never shows server text. */
export function planProblemMessage(
  problem: GoalProblem,
  t: TFunction<'goals'>,
  context: { isGuest: boolean; locale: string; timeZone: string },
): string {
  switch (problem.kind) {
    case 'featureUnavailable':
      return t('errors.featureUnavailable')
    case 'busy':
      return problem.retryAfterSeconds === null
        ? t('errors.busy')
        : t('errors.busyWait', { seconds: problem.retryAfterSeconds })
    case 'quota':
      if (problem.scope === 'global') return t('errors.quotaGlobal')
      if (context.isGuest) return t('errors.quotaGuest')
      return problem.resetsAt
        ? t('errors.quotaAccount', { time: formatResetTime(problem.resetsAt, context.locale, context.timeZone) })
        : t('errors.quotaAccountNoTime')
    case 'inProgress':
      return t('errors.inProgress')
    case 'failed':
      return t(
        problem.reason === 'timeout'
          ? 'errors.failedTimeout'
          : problem.reason === 'provider'
            ? 'errors.failedProvider'
            : 'errors.failedInvalidOutput',
      )
    case 'insufficientHistory':
      return t('errors.insufficientHistory')
    case 'otherGoalActive':
      return t('errors.otherGoalActive')
    case 'goalNotEligible':
      return t('errors.goalNotEligible')
    case 'goalLimit':
      return t('form.problem.goalLimit')
    case 'concurrency':
      return t('errors.concurrency')
    case 'rateLimited':
      return problem.retryAfterSeconds === null
        ? t('errors.rateLimited')
        : t('errors.rateLimitedWait', { seconds: problem.retryAfterSeconds })
    case 'notFound':
      return t('errors.notFound')
    default:
      return t('errors.unknown')
  }
}
