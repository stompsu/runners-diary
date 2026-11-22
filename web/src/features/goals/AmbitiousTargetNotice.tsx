import { useId, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { dayLabel } from '@/lib/dates/plainDate'
import { formatDuration } from '@/lib/format/duration'
import { goalProblem } from './goalProblems'
import { useInvalidateGoals, useUpdateGoal } from './goalQueries'
import { toGoalRequest, toGoalFormValues } from './goalSchema'
import type { GoalDto } from './goalTypes'

/**
 * M9.1: the plan trains toward what recent runs support, not toward a target far beyond it. Says so politely (the
 * target is "very ambitious"; never "unrealistic") and offers the plan's time as the new target. Changing the target
 * marks the plan outdated, so the notice then gives way to the outdated note.
 */
export function AmbitiousTargetNotice({ goal }: { goal: GoalDto }) {
  const { t, i18n } = useTranslation('goals')
  const titleId = useId()
  const update = useUpdateGoal()
  const invalidate = useInvalidateGoals()
  const [failed, setFailed] = useState(false)
  const trainsToward = goal.plan?.trainsTowardSeconds ?? null
  if (goal.targetDurationSeconds == null) return null
  const time = trainsToward == null ? null : formatDuration(trainsToward)
  const canUse = time !== null && goal.type !== 'Distance' && goal.status === 'Active'

  async function use() {
    if (trainsToward == null) return
    setFailed(false)
    try {
      await update.mutateAsync({
        params: { path: { goalId: goal.id } },
        body: toGoalRequest({ ...toGoalFormValues(goal), targetTime: formatDuration(trainsToward) }, goal.version),
      })
      await invalidate()
      toast.success(t('plan.ambitiousUseDone'))
    } catch (caught) {
      setFailed(true)
      if (goalProblem(caught).kind === 'concurrency') await invalidate()
    }
  }

  return (
    <section
      aria-labelledby={titleId}
      className="flex flex-col gap-2 rounded-md border border-amber-500/60 p-3"
      data-testid="plan-ambitious"
    >
      <h3 id={titleId} className="font-medium">
        {t('plan.ambitiousTitle')}
      </h3>
      <p className="break-words">
        {t('plan.ambitiousBody', {
          time: time ?? '',
          target: formatDuration(goal.targetDurationSeconds),
          date: dayLabel(goal.targetDate, i18n.language),
        })}
      </p>
      {canUse ? (
        <div>
          <Button
            type="button"
            variant="outline"
            className="h-11 sm:h-9"
            disabled={update.isPending}
            onClick={() => void use()}
          >
            {t('plan.ambitiousUse', { time })}
          </Button>
        </div>
      ) : null}
      {failed ? (
        <p role="alert" className="text-destructive">
          {t('plan.ambitiousUseFailed')}
        </p>
      ) : null}
    </section>
  )
}
