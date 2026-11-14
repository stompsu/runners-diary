import { Plus } from 'lucide-react'
import { useMemo } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useParams } from 'react-router'
import { ResponsiveDialog } from '@/components/ResponsiveDialog'
import { Button } from '@/components/ui/button'
import { useCalendarMonth, useRunTypeCodes } from '@/features/calendar/calendarQueries'
import { diaryPath } from '@/features/diary/diaryMonth'
import { NotFoundPage } from '@/features/errors/NotFoundPage'
import { QueryError } from '@/features/errors/QueryError'
import { Skeleton } from '@/components/ui/skeleton'
import { compare, dayLabel, isValid } from '@/lib/dates/plainDate'
import '@/i18n/lazyBundles'
import { ProposalRow } from '@/features/races/ProposalRow'
import { bubbleProposals, useMonthProposals } from '@/features/races/raceQueries'
import { ManagedRunList } from './ManagedRunList'
import { useClosePanel, usePanelMonth } from './usePanel'

/** `/diary/:year/:month/day/:date`: the runs of one day, read from the cached calendar response (ADR 0016 §3). */
export function DayPanel() {
  const { date } = useParams()
  return date && isValid(date) ? <DayPanelView date={date} /> : <NotFoundPage />
}

function DayPanelView({ date }: { date: string }) {
  const { t, i18n } = useTranslation('runs')
  const { t: tRaces } = useTranslation('races')
  const month = usePanelMonth()
  const close = useClosePanel()
  const calendar = useCalendarMonth(month)
  const { query: types, codes } = useRunTypeCodes()
  const runs = useMemo(() => calendar.data?.runs.filter((run) => run.date === date) ?? [], [calendar.data, date])
  // The same cached response as the grid's (the day section also shows when the grid layer is switched off).
  const { query: proposalsQuery } = useMonthProposals(month, { always: true })
  const proposals = useMemo(
    () => bubbleProposals(proposalsQuery.data?.items).filter((proposal) => proposal.date === date),
    [proposalsQuery.data, date],
  )
  const failed = [calendar, types].find((query) => query.isError)
  const ready = calendar.data && types.data
  const outsideGrid = ready && (compare(date, calendar.data.gridStart) < 0 || compare(date, calendar.data.gridEnd) > 0)

  if (outsideGrid) return <NotFoundPage />

  return (
    <ResponsiveDialog title={dayLabel(date, i18n.language)} onClose={close} restoreFocusDate={date} panel="day">
      {failed ? (
        <QueryError
          error={failed.error}
          onRetry={() => {
            for (const query of [calendar, types]) if (query.isError) void query.refetch()
          }}
        />
      ) : !ready ? (
        <Skeleton className="h-24 w-full" />
      ) : runs.length === 0 ? (
        <p className="text-muted-foreground">{t('day.empty')}</p>
      ) : (
        <ManagedRunList runs={runs} codes={codes} month={month} />
      )}
      {proposals.length > 0 ? (
        <section aria-labelledby="day-proposals" className="mt-6">
          <h3 id="day-proposals" className="mb-2 font-semibold">
            {tRaces('proposals.daySection')}
          </h3>
          <ul className="flex flex-col gap-3">
            {proposals.map((proposal) => (
              <ProposalRow key={proposal.raceId} proposal={proposal} base={diaryPath(month)} panel="day" />
            ))}
          </ul>
        </section>
      ) : null}
      <Button asChild className="mt-4 h-11 sm:h-9">
        <Link to={`${diaryPath(month)}/runs/new?date=${date}`}>
          <Plus aria-hidden="true" />
          {t('actions.addForDay')}
        </Link>
      </Button>
    </ResponsiveDialog>
  )
}
