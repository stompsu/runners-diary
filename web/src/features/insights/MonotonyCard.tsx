import { rangeLabel } from '@/lib/dates/plainDate'
import { formatLoad, formatRatio, lastCompleteWeek } from './insightsFormat'
import type { InsightsReport } from './insightsTypes'
import { useInsightsT } from './useInsightsT'

export function MonotonyCard({ report }: { report: InsightsReport }) {
  const { t, i18n } = useInsightsT()
  const locale = i18n.language
  const week = lastCompleteWeek(report.weeks)
  const high = report.thresholds.monotonyHigh

  return (
    <section
      aria-labelledby="monotony-title"
      className="flex flex-col gap-3 rounded-lg border p-4"
      data-testid="monotony-card"
    >
      <h3 id="monotony-title" className="font-medium">
        {t('monotony.title')}
      </h3>
      {week === null ? (
        <p className="text-sm">{t('monotony.noWeek')}</p>
      ) : (
        <>
          <p className="text-sm text-muted-foreground">
            {t('monotony.week', { range: rangeLabel(week.start, week.end, locale) })}
          </p>
          {week.monotony === null ? (
            <p className="text-sm">{t('monotony.unavailable')}</p>
          ) : (
            <>
              <dl className="grid grid-cols-2 gap-2 text-sm">
                <div>
                  <dt className="text-muted-foreground">{t('monotony.value')}</dt>
                  <dd className="text-2xl font-semibold tabular-nums">{formatRatio(week.monotony, locale)}</dd>
                </div>
                <div>
                  <dt className="text-muted-foreground">{t('monotony.strain')}</dt>
                  <dd className="text-2xl font-semibold tabular-nums">
                    {week.strain === null ? '—' : formatLoad(week.strain, locale)}
                  </dd>
                </div>
              </dl>
              <p className="text-sm font-medium">
                {week.monotony >= high ? t('monotony.uniform') : t('monotony.varied')}
              </p>
            </>
          )}
        </>
      )}
      <p className="border-t pt-3 text-sm text-muted-foreground">
        {t('monotony.explain', { high: formatRatio(high, locale) })}
      </p>
    </section>
  )
}
