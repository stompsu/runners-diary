import { describe, expect, it } from 'vitest'
import { isRetryable, mapFieldErrors, parseProblem, parseRetryAfter, PROBLEM_TYPES } from './problem'

const response = (status: number, headers: Record<string, string> = {}) => new Response(null, { status, headers })

describe('parseProblem', () => {
  it('recognises problem types', () => {
    expect(parseProblem({ type: PROBLEM_TYPES.concurrency, status: 409 })).toEqual({ kind: 'concurrency', status: 409 })
    expect(parseProblem({ type: PROBLEM_TYPES.runLimit, status: 409 })).toEqual({ kind: 'runLimit', status: 409 })
    expect(
      parseProblem({ type: PROBLEM_TYPES.rateLimited, status: 429 }, response(429, { 'Retry-After': '42' })),
    ).toEqual({
      kind: 'rateLimited',
      status: 429,
      retryAfterSeconds: 42,
    })
    expect(
      parseProblem({ type: PROBLEM_TYPES.guestCapacity, status: 503 }, response(503, { 'Retry-After': '30' })),
    ).toEqual({
      kind: 'guestCapacity',
      status: 503,
      retryAfterSeconds: 30,
    })
  })

  it('maps 400 to validation with camelCase field errors', () => {
    const problem = parseProblem({
      status: 400,
      title: 'One or more validation errors occurred.',
      errors: {
        distanceMeters: ['Distance must be greater than 0.'],
        'race.resultTimeSeconds': ['Bad'],
        body: 'single',
      },
    })
    expect(problem).toEqual({
      kind: 'validation',
      status: 400,
      fieldErrors: {
        distanceMeters: ['Distance must be greater than 0.'],
        'race.resultTimeSeconds': ['Bad'],
        body: ['single'],
      },
    })
  })

  it('gives validation an empty map when errors are missing or malformed', () => {
    expect(parseProblem({ status: 400 })).toEqual({ kind: 'validation', status: 400, fieldErrors: {} })
    expect(parseProblem({ status: 400, errors: 'oops' })).toEqual({ kind: 'validation', status: 400, fieldErrors: {} })
  })

  it.each([
    [401, 'unauthorized'],
    [404, 'notFound'],
    [413, 'payloadTooLarge'],
    [429, 'rateLimited'],
    [502, 'unavailable'],
    [503, 'unavailable'],
    [504, 'unavailable'],
    [500, 'unknown'],
    [409, 'unknown'],
    [418, 'unknown'],
  ])('maps status %s without a type to %s', (status, kind) => {
    expect(parseProblem({ status }).kind).toBe(kind)
  })

  it('prefers the response status over the body status', () => {
    expect(parseProblem({}, response(404))).toEqual({ kind: 'notFound', status: 404 })
  })

  it('handles non-JSON error bodies (proxy HTML) via the response status', () => {
    expect(parseProblem('<html>Bad gateway</html>', response(502))).toEqual({
      kind: 'unavailable',
      status: 502,
      retryAfterSeconds: null,
    })
    expect(parseProblem('plain text', response(500))).toEqual({ kind: 'unknown', status: 500 })
  })

  it('handles the synthetic { status } object from the client middleware', () => {
    expect(parseProblem({ status: 504 })).toEqual({ kind: 'unavailable', status: 504, retryAfterSeconds: null })
  })

  it('treats a network TypeError as unavailable', () => {
    expect(parseProblem(new TypeError('Failed to fetch'))).toEqual({
      kind: 'unavailable',
      status: null,
      retryAfterSeconds: null,
    })
  })

  it('returns unknown for undefined/garbage', () => {
    expect(parseProblem(undefined)).toEqual({ kind: 'unknown', status: null })
    expect(parseProblem(42)).toEqual({ kind: 'unknown', status: null })
  })
})

describe('parseRetryAfter', () => {
  it('reads delay-seconds', () => {
    expect(parseRetryAfter('120')).toBe(120)
    expect(parseRetryAfter(' 5 ')).toBe(5)
    expect(parseRetryAfter('0')).toBe(0)
  })

  it('reads an HTTP-date relative to now', () => {
    const now = Date.parse('2026-11-03T12:00:00Z')
    expect(parseRetryAfter('Tue, 03 Nov 2026 12:01:30 GMT', now)).toBe(90)
    expect(parseRetryAfter('Tue, 03 Nov 2026 11:00:00 GMT', now)).toBe(0)
  })

  it('is null for missing or invalid values', () => {
    expect(parseRetryAfter(null)).toBeNull()
    expect(parseRetryAfter(undefined)).toBeNull()
    expect(parseRetryAfter('')).toBeNull()
    expect(parseRetryAfter('soon')).toBeNull()
    expect(parseRetryAfter('-5')).toBeNull()
  })
})

describe('mapFieldErrors', () => {
  const isField = (name: string) => ['distanceKm', 'title'].includes(name)

  it('renames keys, keeps identity otherwise and routes unknown keys to the summary', () => {
    const result = mapFieldErrors(
      { distanceMeters: ['Too small.'], title: ['Too long.'], body: ['Invalid body.'], nonsense: ['Hm.'] },
      { keyMap: { distanceMeters: 'distanceKm' }, isField },
    )
    expect(result.fields).toEqual({ distanceKm: 'Too small.', title: 'Too long.' })
    expect(result.summary).toEqual(['Invalid body.', 'Hm.'])
  })

  it('joins several messages for one field', () => {
    expect(mapFieldErrors({ title: ['A.', 'B.'] }, { isField }).fields).toEqual({ title: 'A. B.' })
  })
})

describe('isRetryable', () => {
  it('retries network errors and 502/503/504 only', () => {
    expect(isRetryable(new TypeError('Failed to fetch'))).toBe(true)
    for (const status of [502, 503, 504]) expect(isRetryable({ status })).toBe(true)
    for (const status of [400, 401, 404, 409, 413, 429, 500]) expect(isRetryable({ status })).toBe(false)
    expect(isRetryable(new Error('boom'))).toBe(false)
    expect(isRetryable('text')).toBe(false)
    expect(isRetryable(undefined)).toBe(false)
  })
})
