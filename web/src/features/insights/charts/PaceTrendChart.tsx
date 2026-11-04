import { CartesianGrid, Line, LineChart, ResponsiveContainer, XAxis, YAxis } from 'recharts'
import { shortDayLabel } from '@/lib/dates/plainDate'
import { formatDistance } from '@/lib/format/units'
import { ChartFigure, LineSwatch } from '../ChartFigure'
import { formatCount, formatPaceClock, formatPaceOrDash } from '../insightsFormat'
import type { InsightsReport } from '../insightsTypes'
import { useInsightsT } from '../useInsightsT'
import { AXIS_TICK, CHART_COLORS, CHART_HEIGHT, CHART_INITIAL, CHART_WRAPPER_STYLE } from './chartTheme'

/** Weekly easy-run pace (distance weighted), faster up, with the fitted slope stated in words. */
export default function PaceTrendChart({ report }: { report: InsightsReport }) {
  const { t, i18n } = useInsightsT()
  const locale = i18n.language
  const { weeks, slopeSecondsPerKmPerWeek: slope, excludedRunCount } = report.paceTrend
  const data = weeks.map((week) => ({ ...week, tick: shortDayLabel(week.start, locale) }))

  if (weeks.length === 0) {
    return (
      <section
        aria-labelledby="pace-empty-title"
        className="flex flex-col gap-2 rounded-lg border p-3 sm:p-4"
        data-testid="pace-empty"
      >
        <h3 id="pace-empty-title" className="font-medium">
          {t('pace.title')}
        </h3>
        <p className="text-sm text-muted-foreground">{t('pace.none')}</p>
        {excludedRunCount > 0 ? (
          <p className="text-sm text-muted-foreground">{t('pace.excluded', { count: excludedRunCount })}</p>
        ) : null}
      </section>
    )
  }

  const rounded = slope === null ? null : Math.round(Math.abs(slope) * 10) / 10
  const amount =
    rounded === null ? '' : `${new Intl.NumberFormat(locale, { maximumFractionDigits: 1 }).format(rounded)} s/km`
  const trend =
    slope === null
      ? t('pace.notEnough')
      : rounded === 0
        ? t('pace.summaryFlat')
        : t(slope < 0 ? 'pace.summaryFaster' : 'pace.summarySlower', { amount })
  const summary = excludedRunCount > 0 ? `${trend} ${t('pace.excluded', { count: excludedRunCount })}` : trend

  return (
    <ChartFigure
      title={t('pace.title')}
      summary={summary}
      note={t('pace.fasterIsUp')}
      table={{
        headers: [t('pace.week'), t('pace.runs'), t('pace.distance'), t('pace.pace'), t('pace.heartRate')],
        rows: weeks.map((week) => [
          shortDayLabel(week.start, locale),
          formatCount(week.runCount, locale),
          formatDistance(week.distanceMeters, locale, 1),
          formatPaceOrDash(week.paceSecondsPerKm),
          week.avgHeartRate === null ? '—' : formatCount(week.avgHeartRate, locale),
        ]),
      }}
      legend={[{ label: t('pace.pace'), swatch: <LineSwatch color={CHART_COLORS.pace} /> }]}
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
            <XAxis dataKey="tick" tick={AXIS_TICK} interval="preserveStartEnd" minTickGap={24} tickMargin={4} />
            <YAxis
              reversed
              tick={AXIS_TICK}
              width={44}
              domain={[(min: number) => Math.floor(min - 10), (max: number) => Math.ceil(max + 10)]}
              tickFormatter={(value: number) => formatPaceClock(value)}
            />
            <Line
              dataKey="paceSecondsPerKm"
              stroke={CHART_COLORS.pace}
              strokeWidth={2.5}
              dot={{ r: 3 }}
              isAnimationActive={false}
            />
          </LineChart>
        </ResponsiveContainer>
      </div>
    </ChartFigure>
  )
}
