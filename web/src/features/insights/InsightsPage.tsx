import { useEffect } from 'react'
import { useSearchParams } from 'react-router'
import { useMe } from '@/auth/useMe'
import { Skeleton } from '@/components/ui/skeleton'
import { QueryError } from '@/features/errors/QueryError'
import { rangeLabel, todayFromMe } from '@/lib/dates/plainDate'
import { AcwrCard } from './AcwrCard'
import { ComparisonSelect } from './ComparisonSelect'
import { DataQualityNote } from './DataQualityNote'
import { InsightsCharts } from './InsightsCharts'
import { InsightsEmptyState } from './InsightsEmptyState'
import { KpiGrid } from './KpiGrid'
import { MonotonyCard } from './MonotonyCard'
import { PeriodPicker } from './PeriodPicker'
import { PlannedSummary } from './PlannedSummary'
import { RaceHistoryTable } from './RaceHistoryTable'
import { SummaryCard } from './summary/SummaryCard'
import { parseInsightsParams, serializeInsightsRange, type InsightsRange } from './insightsRange'
import { useInsightsReport } from './insightsQueries'
import type { InsightsReport } from './insightsTypes'
import { useInsightsT } from './useInsightsT'

/** `/insights`: the period lives in the URL (`?view&at&from&to&compare`), the data in one report query. */
export function InsightsPage() {
  const { t } = useInsightsT()
  const me = useMe()

  return (
    <div className="flex flex-col gap-6">
      <header>
        <h1 className="text-2xl font-semibold">{t('title')}</h1>
        <p className="mt-1 text-muted-foreground">{t('intro')}</p>
      </header>
      {me.data ? (
        <InsightsDashboard today={todayFromMe(me.data)} />
      ) : me.isError ? (
        <QueryError error={me.error} onRetry={() => void me.refetch()} />
      ) : (
        <DashboardSkeleton />
      )}
    </div>
  )
}

function DashboardSkeleton() {
  const { t } = useInsightsT()
  return (
    <output aria-label={t('loading')} className="flex flex-col gap-4">
      <Skeleton className="h-10 w-full max-w-lg" />
      <div className="grid grid-cols-2 gap-3 lg:grid-cols-3">
        {Array.from({ length: 6 }, (_, index) => (
          <Skeleton key={index} className="h-24" />
        ))}
      </div>
      <Skeleton className="h-56" />
    </output>
  )
}

function InsightsDashboard({ today }: { today: string }) {
  const { t, i18n } = useInsightsT()
  const [searchParams, setSearchParams] = useSearchParams()
  const { range, canonical } = parseInsightsParams(searchParams, today)
  const report = useInsightsReport(range)

  // A hand-edited or stale URL falls back to the default view and replaces itself (Back must not return to it).
  useEffect(() => {
    if (!canonical) setSearchParams(serializeInsightsRange(range, today), { replace: true })
  }, [canonical, range, today, setSearchParams])

  function go(next: InsightsRange) {
    setSearchParams(serializeInsightsRange(next, today))
  }

  const data = report.data
  const updating = report.isPlaceholderData || (report.isFetching && !!data)

  return (
    <>
      <section aria-labelledby="period-title" className="flex flex-col gap-3">
        <h2 id="period-title" className="sr-only">
          {t('period.groupLabel')}
        </h2>
        <PeriodPicker range={range} today={today} onChange={go} />
        <ComparisonSelect value={range.compare} onChange={(compare) => go({ ...range, compare })} />
        {data ? <PeriodSummary report={data} locale={i18n.language} /> : null}
      </section>

      {report.isError && !data ? (
        <QueryError error={report.error} onRetry={() => void report.refetch()} />
      ) : !data ? (
        <DashboardSkeleton />
      ) : (
        <div
          className="flex flex-col gap-6 transition-opacity data-[updating=true]:opacity-60"
          data-updating={updating}
          aria-busy={updating}
        >
          <ReportView report={data} range={range} />
        </div>
      )}
    </>
  )
}

/** The period, the elapsed part when it is not over, and what it is compared with: all labelled from the response. */
function PeriodSummary({ report, locale }: { report: InsightsReport; locale: string }) {
  const { t } = useInsightsT()
  const { period, comparison } = report

  return (
    <div className="space-y-1 text-sm" aria-live="polite">
      <p className="font-medium" data-testid="period-label">
        {t('period.current', { range: rangeLabel(period.from, period.to, locale) })}
      </p>
      {period.isPartial ? (
        <p className="text-muted-foreground" data-testid="partial-note">
          {t('period.partial', {
            date: rangeLabel(period.asOf, period.asOf, locale),
            elapsed: period.elapsedDays,
            days: period.days,
          })}
        </p>
      ) : null}
      {comparison ? (
        <p className="text-muted-foreground" data-testid="comparison-label">
          {t('compare.comparedWith', {
            range: rangeLabel(comparison.from, comparison.to, locale),
            kind: t(`compare.${comparison.kind}`),
          })}
        </p>
      ) : null}
    </div>
  )
}

function ReportView({ report, range }: { report: InsightsReport; range: InsightsRange }) {
  const { t } = useInsightsT()
  const empty = report.totals.runCount === 0 && report.totals.activeDays === 0

  if (empty) {
    return (
      <>
        <InsightsEmptyState
          diaryDate={report.period.asOf >= report.period.from ? report.period.asOf : report.period.from}
        />
        <PlannedSummary planned={report.planned} />
        <SummaryCard key={`${range.from}|${range.to}|${range.compare}`} range={range} />
        {report.races.length > 0 ? <RaceHistoryTable races={report.races} /> : null}
      </>
    )
  }

  return (
    <>
      <section aria-labelledby="summary-title" className="flex flex-col gap-3">
        <h2 id="summary-title" className="text-lg font-semibold">
          {t('sections.summary')}
        </h2>
        <KpiGrid report={report} />
        <PlannedSummary planned={report.planned} />
      </section>

      <SummaryCard key={`${range.from}|${range.to}|${range.compare}`} range={range} />

      <section aria-labelledby="load-title" className="flex flex-col gap-3">
        <h2 id="load-title" className="text-lg font-semibold">
          {t('sections.load')}
        </h2>
        <div className="grid gap-3 lg:grid-cols-2">
          <AcwrCard report={report} />
          <MonotonyCard report={report} />
        </div>
      </section>

      <section aria-labelledby="charts-title" className="flex flex-col gap-3">
        <h2 id="charts-title" className="text-lg font-semibold">
          {t('sections.charts')}
        </h2>
        <InsightsCharts report={report} />
      </section>

      <RaceHistoryTable races={report.races} />
      <DataQualityNote quality={report.dataQuality} />
    </>
  )
}
