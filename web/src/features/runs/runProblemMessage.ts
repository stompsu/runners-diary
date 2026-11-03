import type { TFunction } from 'i18next'
import type { ApiProblem } from '@/lib/api/problem'

/**
 * Localized text for a failed write (save, rate, delete). Validation problems are mapped onto fields by the form
 * itself; this covers the form-level / toast cases (ADR 0018 §3-4). 409 concurrency has its own UI in the form.
 */
export function writeProblemMessage(problem: ApiProblem, t: TFunction<'errors'>): string {
  switch (problem.kind) {
    case 'validation':
      return t('problem.validation')
    case 'concurrency':
      return t('problem.concurrency')
    case 'runLimit':
      return t('problem.runLimit')
    case 'notFound':
      return t('problem.notFound')
    case 'payloadTooLarge':
      return t('problem.payloadTooLarge')
    case 'unauthorized':
      return t('problem.unauthorized')
    case 'rateLimited':
      return problem.retryAfterSeconds === null
        ? t('problem.rateLimited')
        : t('problem.rateLimitedWait', { seconds: problem.retryAfterSeconds })
    case 'guestCapacity':
      return problem.retryAfterSeconds === null
        ? t('problem.guestCapacity')
        : t('problem.guestCapacityWait', { seconds: problem.retryAfterSeconds })
    case 'unavailable':
      return t('problem.unavailable')
    default:
      return t('problem.unknown')
  }
}
