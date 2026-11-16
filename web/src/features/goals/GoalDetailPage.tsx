import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate, useParams } from 'react-router'
import { toast } from 'sonner'
import { useMe } from '@/auth/useMe'
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { useGoalList } from '@/features/calendar/suggestionQueries'
import { diaryPath } from '@/features/diary/diaryMonth'
import { QueryError } from '@/features/errors/QueryError'
import { parseProblem } from '@/lib/api/problem'
import { dayLabel, monthOf, todayFromMe } from '@/lib/dates/plainDate'
import { formatDuration } from '@/lib/format/duration'
import { formatDistance, formatElevation, formatPaceValue } from '@/lib/format/units'
import { useDeleteGoal, useGoal, useInvalidateGoals, useUpdateGoal } from './goalQueries'
import { toGoalFormValues, toGoalRequest } from './goalSchema'
import type { GoalDto } from './goalTypes'
import { PlanSection } from './PlanSection'
import '@/i18n/lazyBundles'

/** `/goals/:goalId`: the goal's summary, the date-changed banner and the plan section. */
export default function GoalDetailPage() {
  const { goalId = '' } = useParams()
  const { t } = useTranslation('goals')
  const goal = useGoal(goalId)
  const me = useMe()
  const goals = useGoalList()

  if (goal.isError && parseProblem(goal.error).kind === 'notFound') {
    return (
      <div className="flex flex-col items-start gap-3">
        <p>{t('detail.notFound')}</p>
        <Button asChild variant="outline" className="h-11 sm:h-9">
          <Link to="/goals">{t('detail.notFoundBack')}</Link>
        </Button>
      </div>
    )
  }
  if (goal.isError) return <QueryError error={goal.error} onRetry={() => void goal.refetch()} />
  if (!goal.data || !me.data) return <Skeleton className="h-96 w-full" />
  return <GoalDetail goal={goal.data} goals={goals.data?.items ?? []} today={todayFromMe(me.data)} />
}

function Row({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="flex flex-col sm:flex-row sm:gap-2">
      <dt className="text-sm text-muted-foreground sm:w-40">{label}</dt>
      <dd>{children}</dd>
    </div>
  )
}

function GoalDetail({ goal, goals, today }: { goal: GoalDto; goals: readonly GoalDto[]; today: string }) {
  const { t, i18n } = useTranslation('goals')
  const navigate = useNavigate()
  const update = useUpdateGoal()
  const remove = useDeleteGoal()
  const invalidate = useInvalidateGoals()
  const [deleting, setDeleting] = useState(false)
  const locale = i18n.language
  const pace =
    goal.targetDurationSeconds && goal.distanceMeters
      ? formatPaceValue(goal.targetDurationSeconds / (goal.distanceMeters / 1000))
      : null
  const dateChanged = goal.raceRunDate !== null && goal.raceRunDate !== goal.targetDate

  async function applyRaceDate() {
    if (!goal.raceRunDate) return
    try {
      await update.mutateAsync({
        params: { path: { goalId: goal.id } },
        body: { ...toGoalRequest({ ...toGoalFormValues(goal), targetDate: goal.raceRunDate }, goal.version) },
      })
      await invalidate()
      toast.success(t('detail.raceDateUpdated'))
    } catch (error) {
      const problem = parseProblem(error)
      toast.error(problem.kind === 'concurrency' ? t('errors.concurrency') : t('errors.unknown'))
      await invalidate()
    }
  }

  async function confirmDelete() {
    try {
      await remove.mutateAsync({ params: { path: { goalId: goal.id } } })
      await invalidate()
      toast.success(t('form.deleted'))
      void navigate('/goals', { replace: true })
    } catch {
      toast.error(t('errors.unknown'))
      setDeleting(false)
    }
  }

  return (
    <div className="flex max-w-3xl flex-col gap-6">
      <header>
        <Link to="/goals" className="text-sm underline">
          {t('detail.back')}
        </Link>
        <div className="mt-1 flex flex-wrap items-center gap-2">
          <h1 className="text-2xl font-semibold">{goal.title}</h1>
          <Badge variant="outline">{t(`types.${goal.type}`)}</Badge>
          <Badge variant="secondary">{t(`status.${goal.status}`)}</Badge>
          {goal.status === 'Active' ? <Badge variant="outline">{t(`term.${goal.term}`)}</Badge> : null}
        </div>
        <div className="mt-3 flex flex-wrap gap-2">
          <Button asChild variant="outline" className="h-11 sm:h-9">
            <Link to={`/goals/${goal.id}/edit`}>{t('detail.edit')}</Link>
          </Button>
          <Button
            type="button"
            variant="outline"
            className="h-11 text-destructive sm:h-9"
            onClick={() => setDeleting(true)}
          >
            {t('form.delete')}
          </Button>
        </div>
      </header>

      {dateChanged ? (
        <output
          className="flex flex-wrap items-center gap-3 rounded-md border border-amber-500/60 p-3 text-sm"
          data-testid="race-date-banner"
        >
          <p>{t('detail.raceDateChanged', { date: dayLabel(goal.raceRunDate!, locale) })}</p>
          <Button type="button" variant="outline" className="h-11 sm:h-9" onClick={() => void applyRaceDate()}>
            {t('detail.useRaceDate')}
          </Button>
        </output>
      ) : null}

      <section aria-labelledby="goal-summary">
        <h2 id="goal-summary" className="sr-only">
          {t('detail.summary')}
        </h2>
        <dl className="flex flex-col gap-2">
          <Row label={t('detail.date')}>{dayLabel(goal.targetDate, locale)}</Row>
          <Row label={t('detail.distance')}>{formatDistance(goal.distanceMeters, locale)}</Row>
          {goal.elevationGainMeters ? (
            <Row label={t('detail.elevation')}>{formatElevation(goal.elevationGainMeters, locale)}</Row>
          ) : null}
          {goal.surface ? <Row label={t('detail.surface')}>{goal.surface}</Row> : null}
          {goal.targetDurationSeconds ? (
            <Row label={t('detail.targetTime')}>{formatDuration(goal.targetDurationSeconds)}</Row>
          ) : null}
          {pace ? <Row label={t('detail.targetPace')}>{pace}</Row> : null}
          {goal.eventName ? <Row label={t('detail.event')}>{goal.eventName}</Row> : null}
          {goal.raceRunId && goal.raceRunDate ? (
            <Row label={t('detail.linkedRun')}>
              <Link className="underline" to={`${diaryPath(monthOf(goal.raceRunDate))}/runs/${goal.raceRunId}`}>
                {t('detail.viewRun')}
              </Link>
            </Row>
          ) : null}
          {goal.notes ? <Row label={t('detail.notes')}>{goal.notes}</Row> : null}
        </dl>
      </section>

      <PlanSection goal={goal} goals={goals} today={today} />

      <AlertDialog open={deleting} onOpenChange={setDeleting}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>{t('form.delete')}</AlertDialogTitle>
            <AlertDialogDescription>{goal.title}</AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel className="h-11 sm:h-9">{t('form.cancel')}</AlertDialogCancel>
            <AlertDialogAction className="h-11 sm:h-9" onClick={() => void confirmDelete()}>
              {t('form.delete')}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  )
}
