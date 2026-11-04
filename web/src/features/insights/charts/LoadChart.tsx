import { CartesianGrid, Line, LineChart, ResponsiveContainer, XAxis, YAxis } from 'recharts'
import { shortDayLabel } from '@/lib/dates/plainDate'
import { ChartFigure, LineSwatch } from '../ChartFigure'
import { formatLoad } from '../insightsFormat'
import type { InsightsReport } from '../insightsTypes'
import { useInsightsT } from '../useInsightsT'
import { AXIS_TICK, CHART_COLORS, CHART_HEIGHT, CHART_INITIAL, CHART_WRAPPER_STYLE, compactNumber } from './chartTheme'

const CHRONIC_DASH = '6 4'

/** Acute (last 7 days) against chronic (weekly average of the 4 weeks before) load, day by day. */
export default function LoadChart({ report }: { report: InsightsReport }) {
  const { t, i18n } = useInsightsT()
  const locale = i18n.language
  const daily = report.load.daily
  const last = daily[daily.length - 1]
  const data = daily.map((day) => ({ ...day, tick: shortDayLabel(day.date, locale) }))

  const summary = !last
    ? t('chart.noData')
    : last.chronic === null
      ? t('loadChart.summaryNoChronic', {
          date: shortDayLabel(last.date, locale),
          acute: formatLoad(last.acute, locale),
        })
      : t('loadChart.summary', {
          date: shortDayLabel(last.date, locale),
          acute: formatLoad(last.acute, locale),
          chronic: formatLoad(last.chronic, locale),
        })

  return (
    <ChartFigure
      title={t('loadChart.title')}
      summary={summary}
      table={{
        headers: [t('chart.date'), t('loadChart.daily'), t('loadChart.acute'), t('loadChart.chronic')],
        rows: daily.map((day) => [
          shortDayLabel(day.date, locale),
          formatLoad(day.load, locale),
          formatLoad(day.acute, locale),
          day.chronic === null ? '—' : formatLoad(day.chronic, locale),
        ]),
      }}
      legend={[
        { label: t('loadChart.acute'), swatch: <LineSwatch color={CHART_COLORS.acute} /> },
        { label: t('loadChart.chronic'), swatch: <LineSwatch color={CHART_COLORS.chronic} dash={CHRONIC_DASH} /> },
      ]}
    >
      <div style={CHART_WRAPPER_STYLE}>
        <ResponsiveContainer width="100%" height={CHART_HEIGHT} initialDimension={CHART_INITIAL}>
          <LineChart
            data={data}
            accessibilityLayer={false}
            role="presentation"
            margin={{ top: 8, right: 8, bottom: 0, left: 0 }}
          >
            <CartesianGrid vertical={false} stroke={CHART_COLORS.grid} />
            <XAxis dataKey="tick" tick={AXIS_TICK} interval="preserveStartEnd" minTickGap={32} tickMargin={4} />
            <YAxis tick={AXIS_TICK} width={40} tickFormatter={(value: number) => compactNumber(value, locale)} />
            <Line dataKey="acute" stroke={CHART_COLORS.acute} strokeWidth={2.5} dot={false} isAnimationActive={false} />
            <Line
              dataKey="chronic"
              stroke={CHART_COLORS.chronic}
              strokeWidth={2.5}
              strokeDasharray={CHRONIC_DASH}
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
