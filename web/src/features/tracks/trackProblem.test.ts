import { describe, expect, it } from 'vitest'
import i18n from '@/i18n'
import { INVALID_GPX_CODES, classifyTrackProblem, trackProblemMessage } from './trackProblem'

const t = i18n.getFixedT('en', 'tracks')
const tErrors = i18n.getFixedT('en', 'errors')
const message = (body: unknown, options?: { maxMegabytes?: number }) =>
  trackProblemMessage(classifyTrackProblem(body), t, tErrors, options)
const gpxProblem = (code: string) => ({
  type: 'urn:runnersdiary:problem:invalid-gpx',
  status: 400,
  code,
  errors: { file: ['server text that must not be shown'] },
})

describe('classifyTrackProblem', () => {
  it.each(INVALID_GPX_CODES)('maps the invalid-gpx code %s to its own localised message', (code) => {
    expect(classifyTrackProblem(gpxProblem(code))).toEqual({ kind: 'invalidGpx', code })
    const text = message(gpxProblem(code))
    expect(text).not.toContain('server text')
    expect(text).not.toMatch(/problem\./)
  })

  it('has a distinct message per code', () => {
    const texts = INVALID_GPX_CODES.map((code) => message(gpxProblem(code)))
    expect(new Set(texts).size).toBe(texts.length)
  })

  it('an unknown code (newer server) falls back to a generic message', () => {
    expect(classifyTrackProblem(gpxProblem('somethingNew'))).toEqual({ kind: 'invalidGpx', code: null })
    expect(message(gpxProblem('somethingNew'))).toBe('The file could not be used as a GPX track.')
  })

  it('recognises the conflict, limit and availability problems by type', () => {
    const typed = (type: string, status: number, extra = {}) => ({
      type: `urn:runnersdiary:problem:${type}`,
      status,
      ...extra,
    })
    expect(classifyTrackProblem(typed('track-exists', 409))).toEqual({ kind: 'exists' })
    expect(classifyTrackProblem(typed('track-limit', 409))).toEqual({ kind: 'limit' })
    expect(classifyTrackProblem(typed('processing-busy', 503, { retryAfterSeconds: 10 }))).toEqual({
      kind: 'busy',
      retryAfterSeconds: 10,
    })
    expect(classifyTrackProblem(typed('storage-unavailable', 503, { retryAfterSeconds: 5 }))).toEqual({
      kind: 'storage',
      retryAfterSeconds: 5,
    })
    expect(classifyTrackProblem(typed('feature-unavailable', 503))).toEqual({ kind: 'featureUnavailable' })
    expect(message(typed('processing-busy', 503, { retryAfterSeconds: 10 }))).toContain('10 s')
    expect(message(typed('storage-unavailable', 503))).toContain('storage')
    expect(message(typed('track-limit', 409))).toContain('maximum number')
  })

  it('a rest-day run is a 400 on runId', () => {
    expect(classifyTrackProblem({ status: 400, errors: { runId: ['Rest days have no tracks.'] } })).toEqual({
      kind: 'restDay',
    })
    expect(message({ status: 400, errors: { runId: ['x'] } })).toBe('Rest days have no GPX files.')
  })

  it('413 names the limit when it is known; 415 and 404 have their own text', () => {
    expect(message({ status: 413 }, { maxMegabytes: 5 })).toBe('The file is too large. The limit is 5 MB.')
    expect(message({ status: 413 })).toBe('The file is too large.')
    expect(classifyTrackProblem({ status: 415 })).toEqual({ kind: 'unsupportedMedia' })
    expect(message({ status: 404 })).toBe('This run or file no longer exists.')
  })

  it('everything else falls back to the shared write messages (429, network, unknown)', () => {
    expect(message({ status: 429, retryAfterSeconds: 30 })).toContain('30 s')
    expect(message(new TypeError('Failed to fetch'))).toContain('not reachable')
    expect(message({ status: 500 })).toBe('Something went wrong. Please try again.')
  })
})
