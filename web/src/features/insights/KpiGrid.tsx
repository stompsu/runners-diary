import { formatDuration } from '@/lib/format/duration'
import { formatDistance, formatElevation } from '@/lib/format/units'
import { DeltaText } from './DeltaText'
import { formatCount, formatLoad, formatPaceOrDash, type DeltaKind } from './insightsFormat'
import type { Delta, InsightsReport } from './insightsTypes'
import { useInsightsT } from './useInsightsT'

type Kpi = {
  key: 'distance' | 'duration' | 'runs' | 'elevation' | 'load' | 'pace'
  kind: DeltaKind
  value: string
  delta: Delta | null
}

/** Pace has no server delta (it is not in `deltas`): the change in seconds per km is derived from both totals. */
function paceDelta(report: InsightsReport): Delta | null {
  const current = report.totals.averagePaceSecondsPerKm
  const previous = report.comparison?.totals.averagePaceSecondsPerKm ?? null
  if (current === null || previous === null) return null
  return { absolute: current - previous, percent: previous === 0 ? null : ((current - previous) / previous) * 100 }
}

export function KpiGrid({ report }: { report: InsightsReport }) {
  const { t, i18n } = useInsightsT()
  const locale = i18n.language
  const { totals, comparison } = report
  const deltas = comparison?.deltas
  const kpis: Kpi[] = [
    {
      key: 'distance',
      kind: 'distance',
      value: formatDistance(totals.distanceMeters, locale, 1),
      delta: deltas?.distanceMeters ?? null,
    },
    {
      key: 'duration',
      kind: 'duration',
      value: formatDuration(totals.durationSeconds),
      delta: deltas?.durationSeconds ?? null,
    },
    { key: 'runs', kind: 'count', value: formatCount(totals.runCount, locale), delta: deltas?.runCount ?? null },
    {
      key: 'elevation',
      kind: 'elevation',
      value: formatElevation(totals.elevationGainMeters, locale),
      delta: deltas?.elevationGainMeters ?? null,
    },
    { key: 'load', kind: 'load', value: formatLoad(totals.load, locale), delta: deltas?.load ?? null },
    {
      key: 'pace',
      kind: 'pace',
      value: formatPaceOrDash(totals.averagePaceSecondsPerKm),
      delta: comparison ? paceDelta(report) : null,
    },
  ]

  return (
    <dl className="grid grid-cols-2 gap-3 lg:grid-cols-3" data-testid="kpi-grid">
      {kpis.map((kpi) => (
        <div key={kpi.key} className="flex flex-col gap-1 rounded-lg border p-3 sm:p-4" data-kpi={kpi.key}>
          <dt className="text-sm text-muted-foreground">{t(`kpi.${kpi.key}`)}</dt>
          <dd className="text-xl font-semibold tabular-nums sm:text-2xl">{kpi.value}</dd>
          {kpi.delta ? (
            <dd>
              <DeltaText kind={kpi.kind} delta={kpi.delta} />
            </dd>
          ) : null}
        </div>
      ))}
    </dl>
  )
}
