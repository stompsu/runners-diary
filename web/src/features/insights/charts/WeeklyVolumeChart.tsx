import { useId, useState } from 'react'
import { Bar, BarChart, CartesianGrid, ResponsiveContainer, XAxis, YAxis } from 'recharts'
import { Button } from '@/components/ui/button'
import { BoxSwatch, ChartFigure, HatchPattern } from '../ChartFigure'
import { safeSvgId } from '../insightsFormat'
import type { InsightsReport } from '../insightsTypes'
import { useInsightsT } from '../useInsightsT'
import {
  MAX_WEEKLY_DAYS,
  METRICS,
  formatVolumeValue,
  volumeRows,
  type VolumeMetric,
  type VolumeRow,
} from './volumeData'
import { AXIS_TICK, CHART_COLORS, CHART_HEIGHT, CHART_INITIAL, CHART_WRAPPER_STYLE, compactNumber } from './chartTheme'

export default function WeeklyVolumeChart({ report }: { report: InsightsReport }) {
  const { t, i18n } = useInsightsT()
  const locale = i18n.language
  const [metric, setMetric] = useState<VolumeMetric>('distance')
  const patternId = safeSvgId(useId())
  const weekly = report.period.days <= MAX_WEEKLY_DAYS
  const rows = volumeRows(report, metric, weekly, locale, (date) => t('volume.week', { date }))
  const showPlanned = metric !== 'load' && rows.some((row) => row.planned > 0)

  const best = rows.reduce<VolumeRow | null>(
    (top, row) => (top === null || row.completed > top.completed ? row : top),
    null,
  )
  const average = rows.length > 0 ? rows.reduce((sum, row) => sum + row.completed, 0) / rows.length : 0
  const summary =
    best === null || best.completed === 0
      ? t('chart.noData')
      : t(weekly ? 'volume.summaryWeek' : 'volume.summaryMonth', {
          label: best.label,
          value: formatVolumeValue(metric, best.completed, locale),
          average: formatVolumeValue(metric, average, locale),
          total: rows.length,
        })

  const headers = [t('chart.period'), t('chart.completed'), ...(showPlanned ? [t('chart.planned')] : [])]
  const tableRows = rows.map((row) => [
    row.label,
    formatVolumeValue(metric, row.completed, locale),
    ...(showPlanned ? [formatVolumeValue(metric, row.planned, locale)] : []),
  ])

  const controls = (
    <fieldset className="m-0 flex min-w-0 flex-wrap gap-1 border-0 p-0">
      <legend className="sr-only">{t('volume.metricLabel')}</legend>
      {METRICS.map((m) => (
        <Button
          key={m}
          type="button"
          size="sm"
          variant={m === metric ? 'default' : 'outline'}
          aria-pressed={m === metric}
          className="min-h-11 sm:min-h-9"
          onClick={() => setMetric(m)}
        >
          {t(`volume.metrics.${m}`)}
        </Button>
      ))}
    </fieldset>
  )

  return (
    <ChartFigure
      title={t(weekly ? 'volume.titleWeek' : 'volume.titleMonth')}
      summary={summary}
      table={{ headers, rows: tableRows }}
      controls={controls}
      note={showPlanned ? t('volume.plannedNote') : undefined}
      legend={[
        { label: t('chart.completed'), swatch: <BoxSwatch color={CHART_COLORS.completed} /> },
        ...(showPlanned
          ? [{ label: t('chart.planned'), swatch: <BoxSwatch color={CHART_COLORS.planned} hatched /> }]
          : []),
      ]}
    >
      <div style={CHART_WRAPPER_STYLE}>
        <ResponsiveContainer width="100%" height={CHART_HEIGHT} initialDimension={CHART_INITIAL}>
          <BarChart
            data={rows}
            accessibilityLayer={false}
            role="presentation"
            margin={{ top: 8, right: 8, bottom: 0, left: 0 }}
          >
            <HatchPattern id={patternId} color={CHART_COLORS.planned} />
            <CartesianGrid vertical={false} stroke={CHART_COLORS.grid} />
            <XAxis dataKey="tick" tick={AXIS_TICK} interval="preserveStartEnd" tickMargin={4} />
            <YAxis tick={AXIS_TICK} width={40} tickFormatter={(value: number) => compactNumber(value, locale)} />
            <Bar dataKey="completed" stackId="volume" fill={CHART_COLORS.completed} isAnimationActive={false} />
            {showPlanned ? (
              <Bar
                dataKey="planned"
                stackId="volume"
                fill={`url(#${patternId})`}
                stroke={CHART_COLORS.planned}
                strokeDasharray="3 2"
                isAnimationActive={false}
              />
            ) : null}
          </BarChart>
        </ResponsiveContainer>
      </div>
    </ChartFigure>
  )
}
