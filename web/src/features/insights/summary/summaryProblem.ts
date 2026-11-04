import { parseProblem } from '@/lib/api/problem'

const PREFIX = 'urn:runnersdiary:problem:'

/** What went wrong with `POST /api/insights/summary`, as far as the card words it (M6 plan §3.2 problem types). */
export type SummaryError =
  | { kind: 'inProgress' }
  | { kind: 'quota'; scope: 'account' | 'global'; resetsAt: string | null }
  | { kind: 'rateLimited'; retryAfterSeconds: number | null }
  | { kind: 'failed'; reason: 'invalidOutput' | 'timeout' | 'provider' | 'unknown' }
  | { kind: 'unavailable' }
  | { kind: 'busy' }
  | { kind: 'network' }
  | { kind: 'generic' }

/** A `fetch` that was aborted through the `AbortController` of the Cancel button. */
export function isAbortError(error: unknown): boolean {
  return error instanceof DOMException && error.name === 'AbortError'
}

function record(value: unknown): Record<string, unknown> {
  return typeof value === 'object' && value !== null ? (value as Record<string, unknown>) : {}
}

/**
 * Maps the error body the client middleware throws (problem+json, or `{ status }` for HTML bodies) to a `SummaryError`.
 * Unknown types fall back on the status, so a proxy 502/503 reads as a failure and not as a model problem.
 */
export function classifySummaryError(error: unknown): SummaryError {
  if (error instanceof TypeError) return { kind: 'network' }
  const body = record(error)
  const type = typeof body.type === 'string' ? body.type : null
  const status = typeof body.status === 'number' ? body.status : null

  switch (type) {
    case `${PREFIX}summary-in-progress`:
      return { kind: 'inProgress' }
    case `${PREFIX}ai-quota`:
      return {
        kind: 'quota',
        scope: body.scope === 'global' ? 'global' : 'account',
        resetsAt: typeof body.resetsAt === 'string' ? body.resetsAt : null,
      }
    case `${PREFIX}ai-busy`:
      return { kind: 'busy' }
    case `${PREFIX}feature-unavailable`:
      return { kind: 'unavailable' }
    case `${PREFIX}summary-failed`: {
      const reason = body.reason
      return {
        kind: 'failed',
        reason: reason === 'invalidOutput' || reason === 'timeout' || reason === 'provider' ? reason : 'unknown',
      }
    }
  }

  const problem = parseProblem(error)
  if (problem.kind === 'rateLimited') return { kind: 'rateLimited', retryAfterSeconds: problem.retryAfterSeconds }
  if (status === 409) return { kind: 'inProgress' }
  if (status === 502) return { kind: 'failed', reason: 'unknown' }
  if (status === 503 || status === 504) return { kind: 'unavailable' }
  return { kind: 'generic' }
}

/** Quota errors cannot be retried: asking again changes nothing until the limit resets. */
export const isRetryable = (error: SummaryError) => error.kind !== 'quota'
