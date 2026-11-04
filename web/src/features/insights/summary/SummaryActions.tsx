import { forwardRef, useId } from 'react'
import { Button } from '@/components/ui/button'
import type { SummaryResponse } from '../insightsTypes'
import { useInsightsT } from '../useInsightsT'
import { formatResetTime } from './summaryFormat'
import { hasSummaryAction } from './summaryState'

interface Props {
  response: SummaryResponse
  zone: string | undefined
  onGenerate: (regenerate: boolean) => void
}

/**
 * Generate / Regenerate with what is left of the quota. When the quota is used up the button stays focusable
 * (`aria-disabled`) and `aria-describedby` points to the reason, so a keyboard or screen-reader user learns why.
 */
export const SummaryActions = forwardRef<HTMLButtonElement, Props>(function SummaryActions(
  { response, zone, onGenerate },
  ref,
) {
  const { t, i18n } = useInsightsT()
  const reasonId = useId()
  const { generation, summary } = response
  if (!hasSummaryAction(response)) return null

  const exhausted = generation.availability === 'quotaExhausted'
  const label = !summary
    ? t('summary.actions.generate')
    : summary.source === 'demo'
      ? t('summary.actions.generateOwn')
      : t('summary.actions.regenerate')
  // A first summary may be a stored one (a second tab); everything else asks for a new one explicitly.
  const regenerate = summary !== null
  const guest = generation.scope === 'account'
  const left =
    generation.limit !== null && generation.used !== null ? Math.max(0, generation.limit - generation.used) : null
  const remaining =
    left === null || generation.limit === null
      ? null
      : t(guest ? 'summary.remaining.guest' : 'summary.remaining.user', { left, limit: generation.limit })
  const resets = generation.resetsAt ? formatResetTime(generation.resetsAt, i18n.language, zone) : null
  const reason = exhausted
    ? guest
      ? t('summary.quota.guest', { limit: generation.limit ?? 0 })
      : resets
        ? t('summary.quota.user', { time: resets })
        : t('summary.quota.userNoTime')
    : null

  return (
    <div className="flex flex-col gap-2" data-testid="summary-actions">
      <div className="flex flex-wrap items-center gap-x-3 gap-y-2">
        <Button
          ref={ref}
          type="button"
          variant={summary ? 'outline' : 'default'}
          className="min-h-11 aria-disabled:cursor-not-allowed aria-disabled:opacity-50 sm:min-h-9"
          aria-disabled={exhausted ? true : undefined}
          aria-describedby={exhausted ? reasonId : undefined}
          onClick={() => {
            if (!exhausted) onGenerate(regenerate)
          }}
        >
          {label}
        </Button>
        {remaining && !exhausted ? (
          <span className="text-sm text-muted-foreground" data-testid="summary-remaining">
            {remaining}
          </span>
        ) : null}
      </div>
      {reason ? (
        <p id={reasonId} className="text-sm text-muted-foreground" data-testid="summary-quota">
          {reason}
        </p>
      ) : null}
    </div>
  )
})
