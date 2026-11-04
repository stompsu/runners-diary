import { useTranslation } from 'react-i18next'
import type { TrackSplit } from '@/features/tracks/trackGeoJson'
import type { TrackSummary, TrackWarning } from '@/features/tracks/trackSummary'
import { formatDuration } from '@/lib/format/duration'
import { formatDistance, formatElevation, formatElevationDelta, formatPace } from '@/lib/format/units'
import '@/i18n/lazyBundles'

export interface TrackDataTableProps {
  summary: TrackSummary
  warnings: readonly TrackWarning[]
  splits: readonly TrackSplit[]
  /** Open the splits table initially (desktop); it is collapsed on a phone. */
  splitsOpen?: boolean
}

/**
 * The non-visual alternative to the map and the profile (plan §3.6): a summary list, notes about the file in words, and
 * the kilometre splits as a real table. Always rendered with the track, so the map is never the only way to read it.
 */
export function TrackDataTable({ summary, warnings, splits, splitsOpen = false }: TrackDataTableProps) {
  const { t, i18n } = useTranslation('tracks')
  const locale = i18n.language
  const none = t('summary.none')
  const rows: { label: string; value: string }[] = [
    { label: t('summary.distance'), value: formatDistance(summary.distanceMeters, locale) },
    {
      label: t('summary.elapsedTime'),
      value: summary.elapsedSeconds === null ? none : formatDuration(summary.elapsedSeconds),
    },
    {
      label: t('summary.movingTime'),
      value: summary.movingSeconds === null ? none : formatDuration(summary.movingSeconds),
    },
    {
      label: t('summary.pace'),
      // Elapsed-based, like the run's own pace (the prefill uses elapsed time, plan §9 Q3).
      value: formatPace(summary.elapsedSeconds, summary.distanceMeters) ?? none,
    },
    {
      label: t('summary.elevationGain'),
      value: summary.elevationGainMeters === null ? none : formatElevation(summary.elevationGainMeters, locale),
    },
    {
      label: t('summary.elevationLoss'),
      value: summary.elevationLossMeters === null ? none : formatElevation(summary.elevationLossMeters, locale),
    },
    {
      label: t('summary.minElevation'),
      value: summary.minElevationMeters === null ? none : formatElevation(summary.minElevationMeters, locale),
    },
    {
      label: t('summary.maxElevation'),
      value: summary.maxElevationMeters === null ? none : formatElevation(summary.maxElevationMeters, locale),
    },
  ]

  return (
    <div className="space-y-3">
      <section aria-labelledby="track-summary-title">
        <h3 id="track-summary-title" className="sr-only">
          {t('summary.title')}
        </h3>
        <dl className="grid grid-cols-2 gap-x-4 gap-y-2 text-sm sm:grid-cols-4">
          {rows.map((row) => (
            <div key={row.label}>
              <dt className="text-muted-foreground">{row.label}</dt>
              <dd className="font-medium tabular-nums">{row.value}</dd>
            </div>
          ))}
        </dl>
      </section>

      {warnings.length > 0 && (
        <section aria-labelledby="track-warnings-title" className="rounded-md border bg-muted/50 p-3 text-sm">
          <h3 id="track-warnings-title" className="font-medium">
            {t('warnings.title')}
          </h3>
          <ul className="mt-1 list-disc space-y-1 pl-5">
            {warnings.map((warning) => (
              <li key={warning}>{t(`warnings.${warning}`)}</li>
            ))}
          </ul>
        </section>
      )}

      <details open={splitsOpen} className="rounded-md border">
        <summary className="min-h-11 cursor-pointer px-3 py-2.5 text-sm font-medium outline-none focus-visible:ring-[3px] focus-visible:ring-ring/50">
          {t('splits.show')}
        </summary>
        {splits.length === 0 ? (
          <p className="px-3 pb-3 text-sm text-muted-foreground">{t('splits.none')}</p>
        ) : (
          <div className="overflow-x-auto px-3 pb-3">
            <table className="w-full text-sm tabular-nums">
              <caption className="sr-only">{t('splits.caption')}</caption>
              <thead>
                <tr className="text-left text-muted-foreground">
                  <th scope="col" className="py-1 pr-3 font-medium">
                    {t('splits.distance')}
                  </th>
                  <th scope="col" className="py-1 pr-3 font-medium">
                    {t('splits.time')}
                  </th>
                  <th scope="col" className="py-1 pr-3 font-medium">
                    {t('splits.pace')}
                  </th>
                  <th scope="col" className="py-1 font-medium">
                    {t('splits.elevation')}
                  </th>
                </tr>
              </thead>
              <tbody>
                {splits.map((split) => (
                  <tr key={split.index} className="border-t">
                    {/* Full splits are numbered; a shorter last split shows its actual length. */}
                    <th scope="row" className="py-1 pr-3 text-left font-medium">
                      {split.distanceMeters >= 1000 ? split.index : formatDistance(split.distanceMeters, locale, 3)}
                    </th>
                    <td className="py-1 pr-3">
                      {split.elapsedSeconds === null ? none : formatDuration(split.elapsedSeconds)}
                    </td>
                    <td className="py-1 pr-3">{formatPace(split.elapsedSeconds, split.distanceMeters) ?? none}</td>
                    <td className="py-1">
                      {split.elevationDeltaMeters === null
                        ? none
                        : formatElevationDelta(split.elevationDeltaMeters, locale)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </details>
    </div>
  )
}
