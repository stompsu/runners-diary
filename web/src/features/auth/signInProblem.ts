import type { TFunction } from 'i18next'
import type { ApiProblem } from '@/lib/api/problem'

/** Localized message for a failed guest-session request (ADR 0011: 429 per-IP quota, 503 capacity with Retry-After). */
export function guestCreationMessage(problem: ApiProblem, t: TFunction<'errors'>): string {
  switch (problem.kind) {
    case 'rateLimited':
      return problem.retryAfterSeconds === null
        ? t('problem.rateLimited')
        : t('problem.rateLimitedWait', { seconds: problem.retryAfterSeconds })
    case 'guestCapacity':
      return problem.retryAfterSeconds === null
        ? t('problem.guestCapacity')
        : t('problem.guestCapacityWait', { seconds: problem.retryAfterSeconds })
    case 'unavailable':
      return problem.retryAfterSeconds === null
        ? t('problem.unavailable')
        : t('problem.guestCapacityWait', { seconds: problem.retryAfterSeconds })
    default:
      return t('problem.unknown')
  }
}
