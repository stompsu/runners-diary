import { useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Outlet, useMatch, useNavigate, useParams } from 'react-router'
import { useMe } from '@/auth/useMe'
import { SlowStartNotice } from '@/features/auth/SlowStartNotice'
import { diaryPath, parseDiaryMonth, shiftMonth, type DiaryMonth } from '@/features/diary/diaryMonth'
import { NotFoundPage } from '@/features/errors/NotFoundPage'
import { QueryError } from '@/features/errors/QueryError'
import { Skeleton } from '@/components/ui/skeleton'
import { firstOfMonth, isValid, monthOf, todayFromMe, type PlainDate } from '@/lib/dates/plainDate'
import { useCalendarMonth, usePrefetchAdjacentMonths, useRunTypeCodes } from './calendarQueries'
import { preloadPanels } from '@/features/runs/panelRoutes'
import { MonthGrid, type FocusRequest } from './MonthGrid'
import { MonthHeader } from './MonthHeader'

/** `/diary/:year/:month`: invalid or out-of-range months are Not found (AC7); the nested routes render in the Outlet. */
export function DiaryMonthPage() {
  const params = useParams()
  const month = parseDiaryMonth(params.year, params.month)
  return month ? <DiaryMonthView month={month} /> : <NotFoundPage />
}

function DiaryMonthView({ month }: { month: DiaryMonth }) {
  const { t } = useTranslation('calendar')
  const navigate = useNavigate()
  const me = useMe()
  const calendar = useCalendarMonth(month)
  const { query: runTypes, codes } = useRunTypeCodes()
  const dayMatch = useMatch('/diary/:year/:month/day/:date')
  const [focusRequest, setFocusRequest] = useState<FocusRequest | null>(null)

  const ready = !!me.data && !!calendar.data && !!runTypes.data
  usePrefetchAdjacentMonths(month, !!calendar.data)
  useEffect(() => {
    if (ready) preloadPanels()
  }, [ready])

  // A request for another month is stale once the URL has moved elsewhere (state adjusted during render, not in an effect).
  const monthKey = `${month.year}-${month.month}`
  const [seenMonthKey, setSeenMonthKey] = useState(monthKey)
  if (seenMonthKey !== monthKey) {
    setSeenMonthKey(monthKey)
    if (focusRequest && `${focusRequest.month.year}-${focusRequest.month.month}` !== monthKey) setFocusRequest(null)
  }
  const clearFocusRequest = useCallback(() => setFocusRequest(null), [])

  const base = diaryPath(month)
  // The URL is user-editable: only a real date may become the highlighted/tab-stop day.
  const dayParam = dayMatch?.params.date
  const selectedDate = dayParam && isValid(dayParam) ? dayParam : undefined
  const today = me.data ? todayFromMe(me.data) : null
  const todayInMonth = today !== null && monthOf(today).year === month.year && monthOf(today).month === month.month
  const addDate: PlainDate = todayInMonth ? today : firstOfMonth(month.year, month.month)

  function goToMonth(target: DiaryMonth, focus?: PlainDate) {
    if (focus) setFocusRequest({ date: focus, month: target })
    navigate(diaryPath(target))
  }

  function goToToday() {
    if (!today) return
    goToMonth(monthOf(today), today)
  }

  const failed = [me, calendar, runTypes].find((query) => query.isError)

  return (
    <section aria-labelledby="diary-title">
      <MonthHeader
        month={month}
        previous={shiftMonth(month, -1)}
        next={shiftMonth(month, 1)}
        addDate={addDate}
        onToday={goToToday}
        onSelectMonth={(target) => goToMonth(target)}
      />
      {failed ? (
        <QueryError
          error={failed.error}
          onRetry={() => {
            for (const query of [me, calendar, runTypes]) if (query.isError) void query.refetch()
          }}
        />
      ) : null}
      {!ready && !failed ? (
        <div aria-busy="true" className="space-y-2">
          <SlowStartNotice pending />
          <Skeleton className="h-96 w-full" />
          <output className="sr-only">{t('loading')}</output>
        </div>
      ) : null}
      {ready && today ? (
        <MonthGrid
          // A fresh grid per month resets the roving tab stop.
          key={`${month.year}-${month.month}`}
          data={calendar.data}
          month={month}
          today={today}
          codes={codes}
          selectedDate={selectedDate}
          focusRequest={focusRequest}
          onFocusHandled={clearFocusRequest}
          onMoveToMonth={(date) => goToMonth(monthOf(date), date)}
          onOpenDay={(date) => navigate(`${base}/day/${date}`)}
          onOpenRun={(runId) => navigate(`${base}/runs/${runId}`)}
        />
      ) : null}
      <Outlet />
    </section>
  )
}
