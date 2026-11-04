import { Trophy } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { runTypeLabelKey } from '@/features/calendar/runTypePresentation'
import { shortDayLabel } from '@/lib/dates/plainDate'
import { formatDuration } from '@/lib/format/duration'
import { formatDistance } from '@/lib/format/units'
import { formatCount, formatPaceOrDash, sortRaces } from './insightsFormat'
import type { InsightsReport } from './insightsTypes'
import { ScrollRegion } from './ScrollRegion'
import { useInsightsT } from './useInsightsT'

export function RaceHistoryTable({ races }: { races: InsightsReport['races'] }) {
  const { t, i18n } = useInsightsT()
  const { t: tRun } = useTranslation('runs')
  const { t: tType } = useTranslation('runTypes')
  const locale = i18n.language

  return (
    <section aria-labelledby="races-title" className="flex flex-col gap-2">
      <h2 id="races-title" className="text-lg font-semibold">
        {t('races.title')}
      </h2>
      {races.length === 0 ? (
        <p className="text-sm text-muted-foreground">{t('races.none')}</p>
      ) : (
        <ScrollRegion label={t('races.region')}>
          <table className="w-full min-w-max text-left text-sm">
            <caption className="sr-only">{t('races.title')}</caption>
            <thead className="bg-muted">
              <tr>
                {(['date', 'name', 'distance', 'result', 'pace', 'place', 'rating'] as const).map((column) => (
                  <th key={column} scope="col" className="px-3 py-2 font-medium">
                    {t(`races.${column}`)}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {sortRaces(races).map((race) => (
                <tr key={race.runId} className="border-t align-top" data-pb={race.isPersonalBest}>
                  <th scope="row" className="px-3 py-2 font-normal whitespace-nowrap">
                    {shortDayLabel(race.date, locale)}
                  </th>
                  <td className="px-3 py-2">
                    <span className="font-medium">{race.name}</span>
                    <span className="block text-xs text-muted-foreground">
                      {tType(runTypeLabelKey(race.runTypeCode))}
                    </span>
                  </td>
                  <td className="px-3 py-2 whitespace-nowrap">
                    {tRun(`race.categories.${race.distanceCategory}`)}
                    {race.distanceMeters !== null ? (
                      <span className="block text-xs text-muted-foreground">
                        {formatDistance(race.distanceMeters, locale, 2)}
                      </span>
                    ) : null}
                  </td>
                  <td className="px-3 py-2 whitespace-nowrap tabular-nums">
                    {race.status === 'Planned' ? (
                      <>
                        <span>{t('races.planned')}</span>
                        {race.targetTimeSeconds !== null ? (
                          <span className="block text-xs text-muted-foreground">
                            {t('races.target', { time: formatDuration(race.targetTimeSeconds) })}
                          </span>
                        ) : null}
                      </>
                    ) : race.resultTimeSeconds !== null ? (
                      <>
                        {formatDuration(race.resultTimeSeconds)}
                        {race.isPersonalBest ? (
                          <span
                            className="mt-0.5 flex items-center gap-1 text-xs font-medium"
                            title={t('races.personalBestHint')}
                          >
                            <Trophy className="size-3.5" aria-hidden="true" />
                            {t('races.personalBest')}
                          </span>
                        ) : null}
                      </>
                    ) : (
                      '—'
                    )}
                  </td>
                  <td className="px-3 py-2 whitespace-nowrap tabular-nums">
                    {formatPaceOrDash(race.paceSecondsPerKm)}
                  </td>
                  <td className="px-3 py-2 whitespace-nowrap tabular-nums">
                    {race.overallPlace === null && race.categoryPlace === null ? (
                      '—'
                    ) : (
                      <>
                        {race.overallPlace !== null ? (
                          <span className="block">
                            {t('races.placeOverall', { place: formatCount(race.overallPlace, locale) })}
                          </span>
                        ) : null}
                        {race.categoryPlace !== null ? (
                          <span className="block text-xs text-muted-foreground">
                            {t('races.placeCategory', {
                              place: formatCount(race.categoryPlace, locale),
                              category: race.categoryName ?? '',
                            })}
                          </span>
                        ) : null}
                      </>
                    )}
                  </td>
                  <td className="px-3 py-2 whitespace-nowrap">
                    {race.ratingStars === null ? '—' : t('races.stars', { count: race.ratingStars })}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </ScrollRegion>
      )}
    </section>
  )
}
