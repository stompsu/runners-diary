import { Loader2, Sparkles } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { useClientConfig } from '@/auth/useClientConfig'
import { useMe } from '@/auth/useMe'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Switch } from '@/components/ui/switch'
import { Label } from '@/components/ui/label'
import { Skeleton } from '@/components/ui/skeleton'
import { QueryError } from '@/features/errors/QueryError'
import { aiLanguage, toLanguage } from '@/i18n/language'
import { dayLabel } from '@/lib/dates/plainDate'
import { otherGoalWithPending } from './goalFormat'
import { goalProblem, isAbort, planProblemMessage, type GoalProblem } from './goalProblems'
import { useGenerateTrainingPlan, useGoalSuggestions, useInvalidateGoals } from './goalQueries'
import type { GoalDto, TrainingPlanResponse } from './goalTypes'
import { AmbitiousTargetNotice } from './AmbitiousTargetNotice'
import { ReplaceOtherGoalDialog } from './ReplaceOtherGoalDialog'
import { SuggestionWeekList } from './SuggestionWeekList'
import '@/i18n/lazyBundles'

/**
 * After a cancel, the second refresh waits this long. The abort reaches the server as a cancelled request, so a model
 * call still running is stopped; what can still land is a plan whose model answer had just arrived, i.e. a short
 * validate-and-commit (milliseconds to about a second). 5 s covers that with margin and still feels immediate; it is
 * deliberately not tied to the 90 s attempt / 190 s total timeouts, which only bound a request that is not cancelled.
 */
export const CANCEL_REFRESH_DELAY_MS = 5000

/**
 * The plan section of a goal (plan §5): Generate / Regenerate with a progress state for a request of up to ~3 minutes
 * (cancellable), the replace-other-goal confirmation, every problem type as a friendly message, the outdated and
 * capped notes and the suggestions by ISO week.
 */
export function PlanSection({ goal, goals, today }: { goal: GoalDto; goals: readonly GoalDto[]; today: string }) {
  const { t, i18n } = useTranslation('goals')
  const me = useMe()
  const config = useClientConfig()
  const generate = useGenerateTrainingPlan()
  const invalidate = useInvalidateGoals()
  const [showDismissed, setShowDismissed] = useState(false)
  const suggestions = useGoalSuggestions(goal.id, today, showDismissed)
  const [running, setRunning] = useState(false)
  const [error, setError] = useState<GoalProblem | null>(null)
  const [result, setResult] = useState<TrainingPlanResponse | null>(null)
  const [cancelled, setCancelled] = useState(false)
  const [replaceFor, setReplaceFor] = useState<string | null | undefined>(undefined)
  const controller = useRef<AbortController | null>(null)
  const attempt = useRef(0)

  // Leaving the page cancels a request that is still in flight (the server may still finish and store the plan, and a cancelled attempt counts toward the generation limit).
  const lateRefresh = useRef<ReturnType<typeof setTimeout> | null>(null)
  const clearLateRefresh = () => {
    if (lateRefresh.current !== null) clearTimeout(lateRefresh.current)
    lateRefresh.current = null
  }
  useEffect(
    () => () => {
      controller.current?.abort()
      if (lateRefresh.current !== null) clearTimeout(lateRefresh.current)
    },
    [],
  )

  const aiAvailable = config.data?.ai?.planGeneration === true
  const eligible = goal.status === 'Active'
  const plan = goal.plan
  const other = otherGoalWithPending(goals, goal.id)
  const isGuest = me.data?.kind === 'Guest'
  const lang = aiLanguage(i18n.language)
  const planLanguage = plan ? toLanguage(plan.language) : null
  const languageHint = planLanguage !== null && planLanguage !== lang ? planLanguage : null

  async function start(replaceOtherGoals: boolean) {
    const mine = ++attempt.current
    clearLateRefresh() // a new attempt makes its own refresh the one that counts
    const abort = new AbortController()
    controller.current = abort
    setRunning(true)
    setError(null)
    setResult(null)
    setCancelled(false)
    try {
      const response = await generate.mutateAsync({
        params: { path: { goalId: goal.id } },
        // `lang` is always sent: without it the server generates in English, not in the profile language.
        body: replaceOtherGoals ? { replaceOtherGoals: true, lang } : { lang },
        signal: abort.signal,
      })
      if (attempt.current !== mine) return
      await invalidate()
      setResult(response)
      toast.success(t('plan.success'))
    } catch (caught) {
      if (attempt.current !== mine || isAbort(caught)) return
      const problem = goalProblem(caught)
      if (problem.kind === 'otherGoalActive') setReplaceFor(problem.goalId)
      else {
        setError(problem)
        if (problem.kind === 'concurrency' || problem.kind === 'notFound') await invalidate()
      }
    } finally {
      if (attempt.current === mine) setRunning(false)
    }
  }

  function cancel() {
    attempt.current += 1
    controller.current?.abort()
    setRunning(false)
    setCancelled(true)
    // The abort can land after the model succeeded: the server then still commits the plan, so refresh what shows it.
    void invalidate()
    // ...and once more shortly after, for a commit that lands after this first refresh.
    clearLateRefresh()
    lateRefresh.current = setTimeout(() => {
      lateRefresh.current = null
      void invalidate()
    }, CANCEL_REFRESH_DELAY_MS)
  }

  function onGenerate() {
    if (other) setReplaceFor(other.id)
    else void start(false)
  }

  const replaceTitle =
    replaceFor === undefined ? null : (goals.find((g) => g.id === replaceFor)?.title ?? other?.title ?? null)
  const open = suggestions.items
  const visible = open.filter((item) => showDismissed || item.status === 'Pending')
  const disabledReason = !eligible ? t('plan.notActive') : !aiAvailable ? t('plan.unavailable') : null

  return (
    <section aria-labelledby="plan-title" className="flex flex-col gap-4 rounded-lg border p-4">
      <div>
        <h2
          id="plan-title"
          tabIndex={-1}
          data-suggestions-focus
          className="flex items-center gap-2 text-lg font-semibold outline-none"
        >
          <Sparkles className="size-5" aria-hidden="true" />
          {t('plan.title')}
        </h2>
        <p className="mt-1 text-sm text-muted-foreground">{t('plan.intro')}</p>
        <p className="mt-1 text-xs text-muted-foreground">{t('notMedicalAdvice')}</p>
      </div>

      <div className="flex flex-wrap items-center gap-3">
        <Button
          type="button"
          className="h-11 sm:h-9"
          disabled={running || disabledReason !== null}
          aria-describedby={disabledReason ? 'plan-disabled-reason' : undefined}
          onClick={onGenerate}
        >
          {running ? <Loader2 className="animate-spin" aria-hidden="true" /> : <Sparkles aria-hidden="true" />}
          {t(plan ? 'plan.regenerate' : 'plan.generate')}
        </Button>
        {running ? (
          <Button type="button" variant="outline" className="h-11 sm:h-9" onClick={cancel}>
            {t('plan.cancel')}
          </Button>
        ) : null}
        {disabledReason ? (
          <p id="plan-disabled-reason" className="text-sm text-muted-foreground">
            {disabledReason}
          </p>
        ) : null}
      </div>

      {running ? (
        <div data-testid="plan-progress">
          <progress aria-label={t('plan.running')} className="h-2 w-full accent-primary" />
          <p className="mt-2 text-sm text-muted-foreground">{t('plan.runningHint')}</p>
          <p className="mt-1 text-xs text-muted-foreground" data-testid="plan-cancel-hint">
            {t('plan.cancelCountsHint')}
          </p>
        </div>
      ) : null}
      <output className="sr-only">{running ? t('plan.running') : cancelled ? t('plan.cancelled') : ''}</output>
      {cancelled ? <p className="text-sm text-muted-foreground">{t('plan.cancelled')}</p> : null}

      {error ? (
        <p role="alert" className="text-sm text-destructive" data-problem={error.kind}>
          {planProblemMessage(error, t, {
            isGuest,
            locale: i18n.language,
            timeZone: me.data?.timeZoneId ?? 'UTC',
          })}
        </p>
      ) : null}

      {plan ? (
        <div className="flex flex-col gap-2 text-sm">
          <p className="flex flex-wrap items-center gap-2">
            <Badge variant="outline">{t(plan.source === 'Demo' ? 'plan.sourceDemo' : 'plan.sourceModel')}</Badge>
            <span>{t('plan.generatedAt', { date: dayLabel(plan.generatedAt.slice(0, 10), i18n.language) })}</span>
          </p>
          {plan.notes ? (
            <p>
              <span className="font-medium">{t('plan.notes')}: </span>
              {plan.notes}
            </p>
          ) : null}
          {languageHint ? (
            <p className="break-words text-muted-foreground" data-testid="plan-language-hint">
              {t(`plan.languageHint.${languageHint}`)} {t('plan.languageHintAction')}
            </p>
          ) : null}
          {plan.outdated ? (
            <output className="block rounded-md border border-amber-500/60 p-2" data-testid="plan-outdated">
              {t('plan.outdated')}
            </output>
          ) : null}
          {plan.ambitiousTarget && !plan.outdated ? <AmbitiousTargetNotice goal={goal} /> : null}
          {plan.horizonCapped ? <p className="text-muted-foreground">{t('plan.horizonCapped')}</p> : null}
          {result && result.droppedCount > 0 ? (
            <p className="text-xs text-muted-foreground" data-testid="plan-dropped">
              {t('plan.dropped', { count: result.droppedCount })}
            </p>
          ) : null}
          {result && result.replacedGoalIds.length > 0 ? (
            <p className="text-xs text-muted-foreground">{t('plan.replaced')}</p>
          ) : null}
          <p className="text-xs text-muted-foreground">
            {t('plan.counts', {
              pending: plan.pendingCount,
              dismissed: plan.dismissedCount,
              accepted: plan.acceptedCount,
            })}
          </p>
        </div>
      ) : (
        <p className="text-sm text-muted-foreground">{t('plan.empty')}</p>
      )}

      {plan ? (
        <div className="flex items-center gap-2">
          <Switch id="show-dismissed" checked={showDismissed} onCheckedChange={setShowDismissed} />
          <Label htmlFor="show-dismissed">{t('plan.showDismissed')}</Label>
        </div>
      ) : null}

      {suggestions.query.isError ? (
        <QueryError error={suggestions.query.error} onRetry={() => void suggestions.query.refetch()} />
      ) : !suggestions.ready && plan ? (
        <Skeleton className="h-24 w-full" />
      ) : visible.length > 0 ? (
        <SuggestionWeekList items={visible} />
      ) : plan ? (
        <p className="text-sm text-muted-foreground">{t('plan.noOpen')}</p>
      ) : null}

      <ReplaceOtherGoalDialog
        open={replaceFor !== undefined}
        otherGoalTitle={replaceTitle}
        onCancel={() => setReplaceFor(undefined)}
        onConfirm={() => {
          setReplaceFor(undefined)
          void start(true)
        }}
      />
    </section>
  )
}
