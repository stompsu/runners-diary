import type { TFunction } from 'i18next'
import { parseProblem, type ApiProblem } from '@/lib/api/problem'
import { writeProblemMessage } from '@/features/runs/runProblemMessage'
import '@/i18n/lazyBundles'

/** Stable `type` URNs of the track endpoints (docs/api-contract.md). */
export const TRACK_PROBLEM_TYPES = {
  invalidGpx: 'urn:runnersdiary:problem:invalid-gpx',
  trackExists: 'urn:runnersdiary:problem:track-exists',
  trackLimit: 'urn:runnersdiary:problem:track-limit',
  processingBusy: 'urn:runnersdiary:problem:processing-busy',
  storageUnavailable: 'urn:runnersdiary:problem:storage-unavailable',
  featureUnavailable: 'urn:runnersdiary:problem:feature-unavailable',
} as const

/** `code` of an `invalid-gpx` problem (ADR 0020). Unknown codes (a newer server) show the generic message. */
export const INVALID_GPX_CODES = [
  'malformedXml',
  'dtdProhibited',
  'unsupportedFormat',
  'notGpx',
  'tooLarge',
  'tooManyPoints',
  'tooFewPoints',
  'invalidCoordinate',
  'tooComplex',
  'wrongExtension',
] as const
export type InvalidGpxCode = (typeof INVALID_GPX_CODES)[number]

export type TrackProblem =
  | { kind: 'invalidGpx'; code: InvalidGpxCode | null }
  | { kind: 'exists' }
  | { kind: 'limit' }
  | { kind: 'restDay' }
  | { kind: 'busy'; retryAfterSeconds: number | null }
  | { kind: 'storage'; retryAfterSeconds: number | null }
  | { kind: 'featureUnavailable' }
  | { kind: 'unsupportedMedia' }
  | { kind: 'other'; problem: ApiProblem }

const isRecord = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null

/**
 * Classifies a failed track call. `body` is what the client throws: the problem+json object (or the synthetic
 * `{ status }` object / a `TypeError` for network failures, see `api/client.ts`).
 */
export function classifyTrackProblem(body: unknown): TrackProblem {
  const record = isRecord(body) ? body : {}
  const problem = parseProblem(body)
  const retryAfterSeconds =
    problem.kind === 'rateLimited' || problem.kind === 'unavailable' || problem.kind === 'guestCapacity'
      ? problem.retryAfterSeconds
      : null
  switch (record.type) {
    case TRACK_PROBLEM_TYPES.invalidGpx: {
      const code = INVALID_GPX_CODES.find((candidate) => candidate === record.code) ?? null
      return { kind: 'invalidGpx', code }
    }
    case TRACK_PROBLEM_TYPES.trackExists:
      return { kind: 'exists' }
    case TRACK_PROBLEM_TYPES.trackLimit:
      return { kind: 'limit' }
    case TRACK_PROBLEM_TYPES.processingBusy:
      return { kind: 'busy', retryAfterSeconds }
    case TRACK_PROBLEM_TYPES.storageUnavailable:
      return { kind: 'storage', retryAfterSeconds }
    case TRACK_PROBLEM_TYPES.featureUnavailable:
      return { kind: 'featureUnavailable' }
  }
  if (problem.kind === 'validation' && problem.fieldErrors.runId) return { kind: 'restDay' }
  if (record.status === 415) return { kind: 'unsupportedMedia' }
  return { kind: 'other', problem }
}

/** Localised, user-facing text for any track problem; never shows server text. */
export function trackProblemMessage(
  problem: TrackProblem,
  t: TFunction<'tracks'>,
  tErrors: TFunction<'errors'>,
  options: { maxMegabytes?: number } = {},
): string {
  switch (problem.kind) {
    case 'invalidGpx':
      return t(`problem.invalidGpx.${problem.code ?? 'unknown'}`)
    case 'exists':
      return t('problem.exists')
    case 'limit':
      return t('problem.limit')
    case 'restDay':
      return t('problem.restDay')
    case 'busy':
      return problem.retryAfterSeconds === null
        ? t('problem.busy')
        : t('problem.busyWait', { seconds: problem.retryAfterSeconds })
    case 'storage':
      return problem.retryAfterSeconds === null
        ? t('problem.storage')
        : t('problem.storageWait', { seconds: problem.retryAfterSeconds })
    case 'featureUnavailable':
      return t('problem.featureUnavailable')
    case 'unsupportedMedia':
      return t('problem.unsupportedMedia')
    case 'other':
      if (problem.problem.kind === 'payloadTooLarge') {
        return options.maxMegabytes === undefined
          ? t('problem.tooLargeGeneric')
          : t('problem.tooLarge', { size: options.maxMegabytes })
      }
      if (problem.problem.kind === 'notFound') return t('problem.gone')
      return writeProblemMessage(problem.problem, tErrors)
  }
}

/** A 404 on the file of a track the run still lists: the row outlived its blobs (e.g. after a partly failed delete). */
export function isFileMissing(caught: unknown): boolean {
  const problem = classifyTrackProblem(caught)
  return problem.kind === 'other' && problem.problem.kind === 'notFound'
}
