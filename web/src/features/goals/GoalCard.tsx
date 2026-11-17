import { MapPin, Target } from 'lucide-react'
import { useId } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { dayLabel } from '@/lib/dates/plainDate'
import { formatDuration } from '@/lib/format/duration'
import { formatDistance, formatElevation } from '@/lib/format/units'
import type { GoalDto } from './goalTypes'
import '@/i18n/lazyBundles'

/** One goal in the list: title, type, date, a Short-/Long-term badge, distance, target time and the plan state. */
export function GoalCard({ goal }: { goal: GoalDto }) {
  const { t, i18n } = useTranslation('goals')
  const locale = i18n.language
  const headingId = useId()
  const plan = goal.plan
  const planLine = !plan
    ? t('list.noPlan')
    : plan.pendingCount > 0
      ? t('list.planState', { count: plan.pendingCount, date: dayLabel(plan.generatedAt.slice(0, 10), locale) })
      : t('list.planStateEmpty', { date: dayLabel(plan.generatedAt.slice(0, 10), locale) })

  return (
    <li data-goal-id={goal.id} className="rounded-lg border p-4">
      <div className="flex flex-wrap items-center gap-2">
        <h3 id={headingId} className="flex min-w-0 items-center gap-1.5 text-base font-semibold">
          <Target className="size-4 shrink-0" aria-hidden="true" />
          <span className="min-w-0 break-words">{goal.title}</span>
        </h3>
        <Badge variant="outline">{t(`types.${goal.type}`)}</Badge>
        {goal.status === 'Active' ? <Badge variant="secondary">{t(`term.${goal.term}`)}</Badge> : null}
        {goal.plan?.outdated ? <Badge variant="outline">{t('list.planOutdated')}</Badge> : null}
      </div>
      <p className="mt-1 text-sm">
        {[
          dayLabel(goal.targetDate, locale),
          formatDistance(goal.distanceMeters, locale, 1),
          goal.elevationGainMeters ? `+${formatElevation(goal.elevationGainMeters, locale)}` : '',
          goal.targetDurationSeconds ? formatDuration(goal.targetDurationSeconds) : '',
        ]
          .filter(Boolean)
          .join(' · ')}
      </p>
      {goal.location?.name ? (
        <p className="mt-1 flex items-center gap-1.5 text-sm">
          <MapPin className="size-4 shrink-0" aria-hidden="true" />
          <span className="min-w-0 break-words">{goal.location.name}</span>
        </p>
      ) : null}
      <p className="mt-1 text-sm text-muted-foreground">{planLine}</p>
      <div className="mt-3">
        <Button asChild variant="outline" className="h-11 sm:h-9">
          <Link to={`/goals/${goal.id}`} aria-describedby={headingId}>
            {t('list.open')}
          </Link>
        </Button>
      </div>
    </li>
  )
}
