import { useQueryClient } from '@tanstack/react-query'
import { useEffect, useMemo } from 'react'
import { $api } from '@/api/client'
import { shiftMonth, type DiaryMonth } from '@/features/diary/diaryMonth'

const calendarInit = ({ year, month }: DiaryMonth) => ({ params: { path: { year, month } } })

/** `GET /api/calendar/months/{year}/{month}`: grid bounds, runs and ISO week totals in one response (ADR 0016 §3). */
export function useCalendarMonth(month: DiaryMonth) {
  return $api.useQuery('get', '/api/calendar/months/{year}/{month}', calendarInit(month))
}

/** `GET /api/run-types`: static catalogue, cached for the session. */
export function useRunTypeCodes() {
  const query = $api.useQuery('get', '/api/run-types', {}, { staleTime: Infinity })
  const codes = useMemo(() => new Map((query.data ?? []).map((type) => [type.id, type.code])), [query.data])
  return { query, codes }
}

/** Warms the cache for the neighbouring months once the visible one has loaded, so prev/next feel instant. */
export function usePrefetchAdjacentMonths(month: DiaryMonth, enabled: boolean) {
  const queryClient = useQueryClient()
  const { year, month: m } = month

  useEffect(() => {
    if (!enabled) return
    for (const delta of [-1, 1]) {
      const target = shiftMonth({ year, month: m }, delta)
      if (!target) continue
      void queryClient.prefetchQuery(
        $api.queryOptions('get', '/api/calendar/months/{year}/{month}', calendarInit(target)),
      )
    }
  }, [queryClient, year, m, enabled])
}
