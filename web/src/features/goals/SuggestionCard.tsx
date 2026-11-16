import { Sparkles } from 'lucide-react'
import { useId, useRef } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { toast } from 'sonner'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { useRunTypeCodes } from '@/features/calendar/calendarQueries'
import { runTypeLabelKey } from '@/features/calendar/runTypePresentation'
import { diaryPath } from '@/features/diary/diaryMonth'
import { returnFocusState } from '@/features/runs/returnFocus'
import { writeProblemMessage } from '@/features/runs/runProblemMessage'
import { useInvalidateRuns } from '@/features/runs/runQueries'
import { parseProblem } from '@/lib/api/problem'
import { dayLabel, monthOf } from '@/lib/dates/plainDate'
import { formatDuration } from '@/lib/format/duration'
import { formatDistance } from '@/lib/format/units'
import { paceText } from './goalFormat'
import { useAcceptSuggestion, useDismissSuggestion, useInvalidateGoals } from './goalQueries'
import type { TrainingSuggestion } from './goalTypes'
import '@/i18n/lazyBundles'

/**
 * One suggested training with its three real buttons: Accept (the default mapping), Edit & accept (the run form
 * pre-filled, in a diary panel) and Dismiss; a dismissed card offers "Accept anyway" only (ADR 0041 §2, §4). Used by
 * the Day panel and the goal page. Model text (`description`) is rendered as plain text only.
 */
export function SuggestionCard({
  suggestion,
  goalTitle,
  panel,
  showDate,
}: {
  suggestion: TrainingSuggestion
  goalTitle?: string
  /** The `data-panel` of the dialog the card is in, or undefined on the goal page. */
  panel?: string
  showDate?: boolean
}) {
  const { t, i18n } = useTranslation('goals')
  const { t: tType } = useTranslation('runTypes')
  const { t: tErrors } = useTranslation('errors')
  const { codes } = useRunTypeCodes()
  const accept = useAcceptSuggestion()
  const dismiss = useDismissSuggestion()
  const invalidateRuns = useInvalidateRuns()
  const invalidateGoals = useInvalidateGoals()
  const headingId = useId()
  const card = useRef<HTMLLIElement>(null)
  const locale = i18n.language
  const dismissed = suggestion.status === 'Dismissed'
  const busy = accept.isPending || dismiss.isPending
  const action = 'h-11 sm:h-9'
  const base = diaryPath(monthOf(suggestion.date))
  const typeLabel = tType(runTypeLabelKey(codes.get(suggestion.runTypeId)))
  const min = suggestion.targetPaceMinSecondsPerKm
  const max = suggestion.targetPaceMaxSecondsPerKm

  /** The card left the list: put the focus on a stable heading instead of losing it to the body. */
  function restoreFocus() {
    window.setTimeout(() => {
      if (card.current && !card.current.isConnected) {
        document
          .querySelector<HTMLElement>('[data-panel] [data-slot="dialog-title"], [data-suggestions-focus]')
          ?.focus()
      }
    }, 0)
  }

  async function onAccept() {
    try {
      await accept.mutateAsync({ params: { path: { suggestionId: suggestion.id } } })
      await invalidateRuns()
      toast.success(t('suggestion.accepted'))
      restoreFocus()
    } catch (error) {
      const problem = parseProblem(error)
      if (problem.kind === 'notFound') {
        await invalidateGoals()
        toast.error(t('suggestion.gone'))
      } else toast.error(writeProblemMessage(problem, tErrors))
    }
  }

  async function onDismiss() {
    try {
      await dismiss.mutateAsync({ params: { path: { suggestionId: suggestion.id } } })
      await invalidateGoals()
      toast(t('suggestion.dismissedToast'))
      restoreFocus()
    } catch (error) {
      const problem = parseProblem(error)
      if (problem.kind === 'notFound') {
        await invalidateGoals()
        toast.error(t('suggestion.gone'))
      } else toast.error(t('suggestion.failed'))
    }
  }

  return (
    <li
      ref={card}
      data-kind="suggestion"
      data-suggestion-id={suggestion.id}
      data-status={suggestion.status}
      className="rounded-lg border border-solid border-muted-foreground/60 p-3"
      style={{
        backgroundImage:
          'repeating-linear-gradient(135deg, transparent 0 6px, color-mix(in oklab, var(--muted-foreground) 10%, transparent) 6px 7px)',
      }}
    >
      <div className="flex flex-wrap items-center gap-2">
        <h4 id={headingId} className="flex min-w-0 items-center gap-1.5 font-medium">
          <Sparkles className="size-4 shrink-0 text-muted-foreground" aria-hidden="true" />
          <span className="min-w-0 break-words">{typeLabel}</span>
        </h4>
        <Badge variant="outline">{t(dismissed ? 'suggestion.dismissed' : 'suggestion.badge')}</Badge>
        <Badge variant="secondary">{t(`intensity.${suggestion.intensity}`)}</Badge>
      </div>
      {showDate ? <p className="mt-1 text-sm text-muted-foreground">{dayLabel(suggestion.date, locale)}</p> : null}
      <p className="mt-1 text-sm">
        {[
          suggestion.distanceMeters ? formatDistance(suggestion.distanceMeters, locale, 1) : '',
          suggestion.durationSeconds ? formatDuration(suggestion.durationSeconds) : '',
          min !== null && max !== null ? t('suggestion.pace', { min: paceText(min), max: paceText(max) }) : '',
          t('suggestion.phase', { phase: t(`phase.${suggestion.phase}`) }),
        ]
          .filter(Boolean)
          .join(' · ')}
      </p>
      <p className="mt-1 text-sm text-muted-foreground">{suggestion.description}</p>
      {goalTitle ? (
        <p className="mt-1 text-xs text-muted-foreground">{t('suggestion.forGoal', { goal: goalTitle })}</p>
      ) : null}
      <div className="mt-3 flex flex-wrap gap-2">
        <Button
          type="button"
          className={action}
          aria-describedby={headingId}
          disabled={busy}
          onClick={() => void onAccept()}
        >
          {t(dismissed ? 'suggestion.acceptAnyway' : 'suggestion.accept')}
        </Button>
        {dismissed ? null : (
          <>
            <Button asChild variant="outline" className={action}>
              <Link
                to={`${base}/suggestions/${suggestion.id}/accept`}
                data-action="edit-accept"
                data-suggestion-id={suggestion.id}
                aria-describedby={headingId}
                state={
                  panel
                    ? returnFocusState(
                        `[data-panel="${panel}"] a[data-action="edit-accept"][data-suggestion-id="${suggestion.id}"]`,
                      )
                    : undefined
                }
              >
                {t('suggestion.editAndAccept')}
              </Link>
            </Button>
            <Button
              type="button"
              variant="outline"
              className={action}
              aria-describedby={headingId}
              disabled={busy}
              onClick={() => void onDismiss()}
            >
              {t('suggestion.dismiss')}
            </Button>
          </>
        )}
      </div>
    </li>
  )
}
