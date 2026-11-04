import { describe, expect, it } from 'vitest'
import { classifySummaryError, isAbortError, isRetryable } from './summaryProblem'

const P = 'urn:runnersdiary:problem:'

describe('classifySummaryError', () => {
  it.each([
    [{ type: `${P}summary-in-progress`, status: 409 }, { kind: 'inProgress' }],
    [{ status: 409 }, { kind: 'inProgress' }],
    [
      { type: `${P}ai-quota`, status: 429, scope: 'account', limit: 3, resetsAt: null },
      { kind: 'quota', scope: 'account', resetsAt: null },
    ],
    [
      { type: `${P}ai-quota`, status: 429, scope: 'global', resetsAt: '2026-11-05T10:00:00Z' },
      { kind: 'quota', scope: 'global', resetsAt: '2026-11-05T10:00:00Z' },
    ],
    [
      { type: `${P}rate-limited`, status: 429, retryAfterSeconds: 30 },
      { kind: 'rateLimited', retryAfterSeconds: 30 },
    ],
    [{ status: 429 }, { kind: 'rateLimited', retryAfterSeconds: null }],
    [
      { type: `${P}summary-failed`, status: 502, reason: 'timeout' },
      { kind: 'failed', reason: 'timeout' },
    ],
    [
      { type: `${P}summary-failed`, status: 502, reason: 'invalidOutput' },
      { kind: 'failed', reason: 'invalidOutput' },
    ],
    [
      { type: `${P}summary-failed`, status: 502, reason: 'provider' },
      { kind: 'failed', reason: 'provider' },
    ],
    [
      { type: `${P}summary-failed`, status: 502, reason: 'other' },
      { kind: 'failed', reason: 'unknown' },
    ],
    [{ status: 502 }, { kind: 'failed', reason: 'unknown' }],
    [{ type: `${P}feature-unavailable`, status: 503 }, { kind: 'unavailable' }],
    [{ type: `${P}ai-busy`, status: 503, retryAfterSeconds: 10 }, { kind: 'busy' }],
    [{ status: 503 }, { kind: 'unavailable' }],
    [{ status: 400, errors: { from: ['bad'] } }, { kind: 'generic' }],
    [{ status: 500 }, { kind: 'generic' }],
    [undefined, { kind: 'generic' }],
    ['<html>', { kind: 'generic' }],
  ])('%j', (body, expected) => {
    expect(classifySummaryError(body)).toEqual(expected)
  })

  it('reads a failed fetch as a network problem', () => {
    expect(classifySummaryError(new TypeError('Failed to fetch'))).toEqual({ kind: 'network' })
  })

  it('only quota problems are not retryable', () => {
    expect(isRetryable({ kind: 'quota', scope: 'account', resetsAt: null })).toBe(false)
    expect(isRetryable({ kind: 'busy' })).toBe(true)
    expect(isRetryable({ kind: 'failed', reason: 'timeout' })).toBe(true)
  })

  it('recognises an aborted request', () => {
    expect(isAbortError(new DOMException('Aborted', 'AbortError'))).toBe(true)
    expect(isAbortError(new TypeError('x'))).toBe(false)
    expect(isAbortError({ name: 'AbortError' })).toBe(false)
  })
})
