import { Mountain } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { formatDuration } from '@/lib/format/duration'
import { formatDistance, formatElevation } from '@/lib/format/units'
import type { CalendarWeek } from './types'

/**
 * ISO week row header: week number and completed totals (distance, time, elevation gain) plus planned distance.
 * On a phone only the distance is visible; the rest stays in the accessibility tree (`max-sm:sr-only`).
 */
export function WeekTotalsCell({ isoWeek, week }: { isoWeek: number; week: CalendarWeek | undefined }) {
  const { t, i18n } = useTranslation('calendar')
  const locale = i18n.language
  const completed = week?.completed
  const plannedMeters = week?.planned.distanceMeters ?? 0

  return (
    <th
      scope="row"
      className="overflow-hidden border bg-muted/30 p-0.5 text-left align-top text-[10px] leading-3 font-normal sm:p-2 sm:text-xs sm:leading-4"
    >
      <div className="flex min-w-0 flex-col gap-0.5">
        <span className="font-semibold">
          <span aria-hidden="true">{t('weekShort', { week: isoWeek })}</span>
          <span className="sr-only">{t('week', { week: isoWeek })}</span>
        </span>
        <span>
          <span className="sr-only">{t('totals.distance')} </span>
          {formatDistance(completed?.distanceMeters ?? 0, locale, 1)}
        </span>
        <span className="max-sm:sr-only">
          <span className="sr-only">{t('totals.time')} </span>
          {formatDuration(completed?.durationSeconds ?? 0)}
        </span>
        <span className="flex items-center gap-1 max-sm:sr-only">
          <Mountain className="size-3 shrink-0" aria-hidden="true" />
          <span className="sr-only">{t('totals.elevation')} </span>
          {formatElevation(completed?.elevationGainMeters ?? 0, locale)}
        </span>
        {plannedMeters > 0 ? (
          <span className="text-muted-foreground italic max-sm:sr-only">
            {t('plannedDistance', { distance: formatDistance(plannedMeters, locale, 1) })}
          </span>
        ) : null}
      </div>
    </th>
  )
}
