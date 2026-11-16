import { Plus } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { useGoalList, useGoalsEnabled } from '@/features/calendar/suggestionQueries'
import { QueryError } from '@/features/errors/QueryError'
import { useClientConfig } from '@/auth/useClientConfig'
import { GoalCard } from './GoalCard'
import { splitByStatus } from './goalFormat'
import { GOAL_STATUSES } from './goalTypes'
import '@/i18n/lazyBundles'

/** `/goals`: the caller's goals, grouped Active / Achieved / Archived, with the plan state of each. */
export default function GoalsPage() {
  return <GoalsList />
}

export function GoalsList() {
  const { t } = useTranslation('goals')
  const config = useClientConfig()
  const enabled = useGoalsEnabled()
  const goals = useGoalList(enabled)

  return (
    <div className="flex max-w-3xl flex-col gap-6">
      <header className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold">{t('title')}</h1>
          <p className="mt-1 text-muted-foreground">{t('intro')}</p>
        </div>
        {enabled ? (
          <Button asChild className="h-11 sm:h-9">
            <Link to="/goals/new">
              <Plus aria-hidden="true" />
              {t('list.new')}
            </Link>
          </Button>
        ) : null}
      </header>
      {config.isError ? (
        <QueryError error={config.error} onRetry={() => void config.refetch()} />
      ) : !config.data ? (
        <Skeleton className="h-48 w-full" />
      ) : !enabled ? (
        <output className="block rounded-md border p-4 text-sm">{t('unavailable')}</output>
      ) : goals.isError ? (
        <QueryError error={goals.error} onRetry={() => void goals.refetch()} />
      ) : !goals.data ? (
        <Skeleton className="h-48 w-full" />
      ) : goals.data.items.length === 0 ? (
        <div className="rounded-lg border p-6">
          <p>{t('list.empty')}</p>
          <Button asChild className="mt-3 h-11 sm:h-9">
            <Link to="/goals/new">{t('list.emptyAction')}</Link>
          </Button>
        </div>
      ) : (
        GOAL_STATUSES.map((status) => {
          const items = splitByStatus(goals.data.items)[status]
          if (items.length === 0) return null
          const id = `goals-${status}`
          return (
            <section key={status} aria-labelledby={id}>
              <h2 id={id} className="mb-2 text-lg font-semibold">
                {t(`list.${status === 'Active' ? 'active' : status === 'Achieved' ? 'achieved' : 'archived'}`)}
              </h2>
              <ul className="flex flex-col gap-3">
                {items.map((goal) => (
                  <GoalCard key={goal.id} goal={goal} />
                ))}
              </ul>
            </section>
          )
        })
      )}
    </div>
  )
}
