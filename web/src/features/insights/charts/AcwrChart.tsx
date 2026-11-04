import { CartesianGrid, Line, LineChart, ReferenceArea, ResponsiveContainer, XAxis, YAxis } from 'recharts'
import { shortDayLabel } from '@/lib/dates/plainDate'
import { ChartFigure, LineSwatch } from '../ChartFigure'
import { formatRatio } from '../insightsFormat'
import type { InsightsReport } from '../insightsTypes'
import { useInsightsT } from '../useInsightsT'
import { AXIS_TICK, CHART_COLORS, CHART_HEIGHT, CHART_INITIAL, CHART_WRAPPER_STYLE } from './chartTheme'

/** The ratio over time with the three bands drawn as labelled areas (the label, not the colour, names the band). */
export default function AcwrChart({ report }: { report: InsightsReport }) {
  const { t, i18n } = useInsightsT()
  const locale = i18n.language
  const { low, high } = { low: report.thresholds.acwrLow, high: report.thresholds.acwrHigh }
  const daily = report.load.daily
  const last = daily[daily.length - 1]
  const data = daily.map((day) => ({ ...day, tick: shortDayLabel(day.date, locale) }))
  const maxRatio = daily.reduce((max, day) => Math.max(max, day.acwr ?? 0), 0)
  const yMax = Math.max(2, Math.ceil((maxRatio + 0.1) * 10) / 10)
  const { bandDays } = report.load

  const summary = !last
    ? t('chart.noData')
    : last.acwr === null || last.band === null
      ? t('acwrChart.summaryUnavailable', { date: shortDayLabel(last.date, locale) })
      : t('acwrChart.summary', {
          date: shortDayLabel(last.date, locale),
          value: formatRatio(last.acwr, locale),
          band: t(`acwr.bandShort.${last.band}`),
          low: bandDays.low,
          optimal: bandDays.optimal,
          high: bandDays.high,
        })

  const areaLabel = (value: string) => ({
    value,
    position: 'insideTopLeft' as const,
    fontSize: 11,
    fill: 'currentColor',
  })

  return (
    <ChartFigure
      title={t('acwrChart.title')}
      summary={summary}
      table={{
        headers: [t('chart.date'), t('acwrChart.value'), t('acwrChart.band')],
        rows: daily.map((day) => [
          shortDayLabel(day.date, locale),
          day.acwr === null ? '—' : formatRatio(day.acwr, locale),
          day.band === null ? '—' : t(`acwr.bandShort.${day.band}`),
        ]),
      }}
      legend={[{ label: t('acwrChart.value'), swatch: <LineSwatch color={CHART_COLORS.ratio} /> }]}
    >
      <div style={CHART_WRAPPER_STYLE}>
        <ResponsiveContainer width="100%" height={CHART_HEIGHT} initialDimension={CHART_INITIAL}>
          <LineChart
            data={data}
            accessibilityLayer={false}
            role="presentation"
            margin={{ top: 8, right: 8, bottom: 0, left: 0 }}
          >
            <ReferenceArea
              y1={0}
              y2={low}
              fill={CHART_COLORS.bandLow}
              fillOpacity={0.14}
              label={areaLabel(t('acwr.bandShort.low'))}
              ifOverflow="hidden"
            />
            <ReferenceArea
              y1={low}
              y2={high}
              fill={CHART_COLORS.bandOptimal}
              fillOpacity={0.14}
              label={areaLabel(t('acwr.bandShort.optimal'))}
              ifOverflow="hidden"
            />
            <ReferenceArea
              y1={high}
              y2={yMax}
              fill={CHART_COLORS.bandHigh}
              fillOpacity={0.14}
              label={areaLabel(t('acwr.bandShort.high'))}
              ifOverflow="hidden"
            />
            <CartesianGrid vertical={false} stroke={CHART_COLORS.grid} />
            <XAxis dataKey="tick" tick={AXIS_TICK} interval="preserveStartEnd" minTickGap={32} tickMargin={4} />
            <YAxis
              tick={AXIS_TICK}
              width={40}
              domain={[0, yMax]}
              ticks={[0, low, high, yMax]}
              tickFormatter={(value: number) => formatRatio(value, locale)}
            />
            <Line
              dataKey="acwr"
              stroke={CHART_COLORS.ratio}
              strokeWidth={2.5}
              dot={false}
              connectNulls={false}
              isAnimationActive={false}
            />
          </LineChart>
        </ResponsiveContainer>
      </div>
    </ChartFigure>
  )
}
