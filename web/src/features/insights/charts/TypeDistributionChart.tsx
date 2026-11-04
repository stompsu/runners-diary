import { useTranslation } from 'react-i18next'
import { Bar, BarChart, Cell, LabelList, ResponsiveContainer, XAxis, YAxis } from 'recharts'
import { familyOf, runTypeLabelKey } from '@/features/calendar/runTypePresentation'
import { formatDuration } from '@/lib/format/duration'
import { formatDistance } from '@/lib/format/units'
import { ChartFigure } from '../ChartFigure'
import { formatCount, formatLoad, formatShare } from '../insightsFormat'
import type { InsightsReport } from '../insightsTypes'
import { useInsightsT } from '../useInsightsT'
import { AXIS_TICK, CHART_WRAPPER_STYLE } from './chartTheme'

/** Share of time per run type as horizontal bars labelled with the type name and the percentage. */
export default function TypeDistributionChart({ report }: { report: InsightsReport }) {
  const { t, i18n } = useInsightsT()
  const { t: tType } = useTranslation('runTypes')
  const locale = i18n.language
  const types = report.typeDistribution.filter((type) => type.durationSeconds > 0)
  const totalSeconds = types.reduce((sum, type) => sum + type.durationSeconds, 0)
  const rows = [...types]
    .sort((a, b) => b.durationSeconds - a.durationSeconds)
    .map((type) => ({
      ...type,
      name: tType(runTypeLabelKey(type.code)),
      share: totalSeconds > 0 ? type.durationSeconds / totalSeconds : 0,
    }))
  const top = rows[0]

  const summary = top
    ? t('types.summary', {
        label: top.name,
        share: formatShare(top.share, locale),
        time: formatDuration(totalSeconds),
      })
    : t('chart.noData')

  return (
    <ChartFigure
      title={t('types.title')}
      summary={summary}
      table={{
        headers: [
          t('types.type'),
          t('types.runs'),
          t('types.distance'),
          t('types.time'),
          t('types.share'),
          t('types.load'),
        ],
        rows: rows.map((row) => [
          row.name,
          formatCount(row.runCount, locale),
          formatDistance(row.distanceMeters, locale, 1),
          formatDuration(row.durationSeconds),
          formatShare(row.share, locale),
          formatLoad(row.load, locale),
        ]),
      }}
    >
      <div style={CHART_WRAPPER_STYLE}>
        <ResponsiveContainer
          width="100%"
          height={Math.max(180, rows.length * 30 + 16)}
          initialDimension={{ width: 600, height: Math.max(180, rows.length * 30 + 16) }}
        >
          <BarChart
            data={rows}
            layout="vertical"
            accessibilityLayer={false}
            role="presentation"
            margin={{ top: 0, right: 44, bottom: 0, left: 0 }}
          >
            <XAxis type="number" hide domain={[0, 'dataMax']} />
            <YAxis type="category" dataKey="name" width={112} tick={AXIS_TICK} interval={0} tickLine={false} />
            <Bar dataKey="share" isAnimationActive={false} barSize={18}>
              {rows.map((row) => (
                <Cell key={row.runTypeId} fill={`var(--run-${familyOf(row.code)})`} />
              ))}
              <LabelList
                dataKey="share"
                position="right"
                fontSize={11}
                fill="currentColor"
                formatter={(value: unknown) => formatShare(Number(value), locale)}
              />
            </Bar>
          </BarChart>
        </ResponsiveContainer>
      </div>
    </ChartFigure>
  )
}
