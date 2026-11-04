import { formatDuration } from '@/lib/format/duration'
import { formatDistance } from '@/lib/format/units'
import type { InsightsReport } from './insightsTypes'
import { useInsightsT } from './useInsightsT'

/** Planned runs of the whole period (also the part after today), with how many are still ahead or were missed. */
export function PlannedSummary({ planned }: { planned: InsightsReport['planned'] }) {
  const { t, i18n } = useInsightsT()

  return (
    <section aria-labelledby="planned-title" className="rounded-lg border p-3 sm:p-4">
      <h3 id="planned-title" className="text-sm font-medium">
        {t('planned.title')}
      </h3>
      {planned.runCount === 0 ? (
        <p className="mt-1 text-sm text-muted-foreground">{t('planned.none')}</p>
      ) : (
        <>
          <p className="mt-1 text-sm">
            {t('planned.summary', {
              count: planned.runCount,
              distance: formatDistance(planned.distanceMeters, i18n.language, 1),
              time: formatDuration(planned.durationSeconds),
            })}
          </p>
          <ul className="mt-1 text-sm text-muted-foreground">
            <li>{t('planned.upcoming', { count: planned.upcomingCount })}</li>
            <li>{t('planned.missed', { count: planned.missedCount })}</li>
          </ul>
        </>
      )}
    </section>
  )
}
