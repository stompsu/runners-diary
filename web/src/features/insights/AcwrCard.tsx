import { CircleCheck, TrendingDown, TrendingUp, type LucideIcon } from 'lucide-react'
import { shortDayLabel } from '@/lib/dates/plainDate'
import { formatLoad, formatRatio, loggedDays } from './insightsFormat'
import type { Band, InsightsReport } from './insightsTypes'
import { useInsightsT } from './useInsightsT'

const BAND_ICON: Record<Band, LucideIcon> = { low: TrendingDown, optimal: CircleCheck, high: TrendingUp }

/**
 * ACWR as a descriptive "load change vs your recent norm" (ADR 0024): value and band in words, acute and chronic, why it
 * may be unavailable, and what it does and does not mean. Never an injury prediction.
 */
export function AcwrCard({ report }: { report: InsightsReport }) {
  const { t, i18n } = useInsightsT()
  const locale = i18n.language
  const { atAsOf, peak, bandDays, historyStart } = report.load
  const { thresholds } = report
  const band = atAsOf.band
  const BandIcon = band ? BAND_ICON[band] : null

  return (
    <section aria-labelledby="acwr-title" className="flex flex-col gap-3 rounded-lg border p-4" data-testid="acwr-card">
      <h3 id="acwr-title" className="font-medium">
        {t('acwr.title')}
      </h3>

      {atAsOf.value !== null && band ? (
        <div>
          <p className="text-sm text-muted-foreground">
            {t('acwr.value', { date: shortDayLabel(atAsOf.date, locale) })}
          </p>
          <p className="flex items-center gap-2 text-3xl font-semibold tabular-nums" data-band={band}>
            {BandIcon ? <BandIcon className="size-6 shrink-0" aria-hidden="true" /> : null}
            <span>{formatRatio(atAsOf.value, locale)}</span>
          </p>
          <p className="mt-1 text-sm font-medium">{t(`acwr.band.${band}`)}</p>
        </div>
      ) : atAsOf.unavailableReason === 'insufficientHistory' ? (
        <p className="text-sm" data-reason="insufficientHistory">
          {t('acwr.insufficientHistory', {
            days: thresholds.acwrMinHistoryDays,
            logged: loggedDays(historyStart, report.period.asOf),
          })}
        </p>
      ) : (
        <p className="text-sm" data-reason="noChronicLoad">
          {t('acwr.noChronicLoad')}
        </p>
      )}

      <dl className="grid grid-cols-2 gap-2 text-sm">
        <div>
          <dt className="text-muted-foreground">{t('acwr.acute')}</dt>
          <dd className="font-medium tabular-nums">{formatLoad(atAsOf.acute, locale)}</dd>
        </div>
        <div>
          <dt className="text-muted-foreground">{t('acwr.chronic')}</dt>
          <dd className="font-medium tabular-nums">
            {atAsOf.chronic === null ? '—' : formatLoad(atAsOf.chronic, locale)}
          </dd>
        </div>
      </dl>

      {peak?.value != null && peak.band ? (
        <p className="text-sm text-muted-foreground">
          {t('acwr.peak', {
            value: formatRatio(peak.value, locale),
            date: shortDayLabel(peak.date, locale),
            band: t(`acwr.bandShort.${peak.band}`),
          })}
        </p>
      ) : null}
      <p className="text-sm text-muted-foreground">
        {t('acwr.bandDays', {
          low: bandDays.low,
          optimal: bandDays.optimal,
          high: bandDays.high,
          unavailable: bandDays.unavailable,
        })}
      </p>

      <div className="space-y-1 border-t pt-3 text-sm text-muted-foreground">
        <p>
          <span className="font-medium text-foreground">{t('acwr.meansTitle')}</span> {t('acwr.means')}
        </p>
        <p>
          <span className="font-medium text-foreground">{t('acwr.notTitle')}</span> {t('acwr.not')}
        </p>
        <p>
          {t('acwr.thresholds', {
            low: formatRatio(thresholds.acwrLow, locale),
            high: formatRatio(thresholds.acwrHigh, locale),
          })}
        </p>
      </div>
    </section>
  )
}
