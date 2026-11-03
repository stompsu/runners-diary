import { useMemo } from 'react'
import { useTranslation } from 'react-i18next'
import { ResponsiveDialog } from '@/components/ResponsiveDialog'
import { Skeleton } from '@/components/ui/skeleton'
import { useCalendarMonth, useRunTypeCodes } from '@/features/calendar/calendarQueries'
import type { CalendarWeek, Run } from '@/features/calendar/types'
import { QueryError } from '@/features/errors/QueryError'
import { compare, firstOfMonth, lastOfMonth, monthLabel } from '@/lib/dates/plainDate'
import { formatDuration } from '@/lib/format/duration'
import { formatDistance, formatElevation } from '@/lib/format/units'
import { ManagedRunList } from './ManagedRunList'
import { useClosePanel, usePanelMonth } from './usePanel'

/** `/diary/:year/:month/list`: every run of the month, by ISO week with its totals (time and elevation live here on phones). */
export function MonthListPanel() {
  const { t, i18n } = useTranslation('runs')
  const month = usePanelMonth()
  const close = useClosePanel()
  const calendar = useCalendarMonth(month)
  const { query: types, codes } = useRunTypeCodes()
  const locale = i18n.language

  const weeks = useMemo(() => {
    if (!calendar.data) return []
    const first = firstOfMonth(month.year, month.month)
    const last = lastOfMonth(month.year, month.month)
    const inMonth = (run: Run) => compare(run.date, first) >= 0 && compare(run.date, last) <= 0
    return calendar.data.weeks
      .map((week) => ({
        week,
        runs: calendar.data.runs.filter((run) => inMonth(run) && run.date >= week.start && run.date <= week.end),
      }))
      .filter((entry) => entry.runs.length > 0)
  }, [calendar.data, month.year, month.month])

  const failed = [calendar, types].find((query) => query.isError)
  const ready = calendar.data && types.data

  return (
    <ResponsiveDialog
      title={t('list.title', { month: monthLabel(month.year, month.month, locale) })}
      onClose={close}
      size="lg"
      panel="list"
    >
      {failed ? (
        <QueryError
          error={failed.error}
          onRetry={() => {
            for (const query of [calendar, types]) if (query.isError) void query.refetch()
          }}
        />
      ) : !ready ? (
        <Skeleton className="h-32 w-full" />
      ) : weeks.length === 0 ? (
        <p className="text-muted-foreground">{t('list.empty')}</p>
      ) : (
        <div className="flex flex-col gap-6">
          {weeks.map(({ week, runs }) => (
            <section key={week.start} aria-labelledby={`week-${week.start}`}>
              <h3 id={`week-${week.start}`} className="mb-1 font-semibold">
                {t('list.week', { week: week.isoWeek })}
              </h3>
              <p className="mb-3 text-sm text-muted-foreground">{weekSummary(week, locale, t)}</p>
              <ManagedRunList runs={runs} codes={codes} month={month} showDate />
            </section>
          ))}
        </div>
      )}
    </ResponsiveDialog>
  )
}

function weekSummary(week: CalendarWeek, locale: string, t: ReturnType<typeof useTranslation<'runs'>>['t']): string {
  const parts = [
    formatDistance(week.completed.distanceMeters, locale),
    formatDuration(week.completed.durationSeconds),
    formatElevation(week.completed.elevationGainMeters, locale),
  ]
  const text = t('list.completedTotals', { totals: parts.join(' · ') })
  return week.planned.distanceMeters > 0
    ? `${text} · ${t('list.plannedTotals', { distance: formatDistance(week.planned.distanceMeters, locale) })}`
    : text
}
