import type { TFunction } from 'i18next'
import type { ApiProblem } from '@/lib/api/problem'

/** Localized message for a failed read (calendar, me, run types). */
export function loadProblemMessage(problem: ApiProblem, t: TFunction<'errors'>): string {
  switch (problem.kind) {
    case 'rateLimited':
      return problem.retryAfterSeconds === null
        ? t('problem.rateLimited')
        : t('problem.rateLimitedWait', { seconds: problem.retryAfterSeconds })
    case 'unavailable':
      return t('problem.unavailable')
    case 'notFound':
      return t('problem.notFound')
    case 'unauthorized':
      return t('problem.unauthorized')
    default:
      return t('problem.unknown')
  }
}
