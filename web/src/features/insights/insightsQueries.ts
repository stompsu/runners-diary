import { keepPreviousData } from '@tanstack/react-query'
import { $api } from '@/api/client'
import type { CompareMode } from './insightsTypes'

export const INSIGHTS_STALE_TIME_MS = 60_000

/** Query-key prefix shared with `useInvalidateRuns`: run create/update/delete makes every report stale. */
export const INSIGHTS_QUERY_KEY = ['get', '/api/insights/report'] as const

/**
 * `GET /api/insights/report` (`getInsightsReport`). The server sends `private, no-store`, so this query cache is the
 * only cache. The previous period stays on screen while paging (`keepPreviousData`), marked as updating by the page.
 */
export function useInsightsReport(range: { from: string; to: string; compare: CompareMode }, enabled = true) {
  return $api.useQuery(
    'get',
    '/api/insights/report',
    { params: { query: { from: range.from, to: range.to, compare: range.compare } } },
    { staleTime: INSIGHTS_STALE_TIME_MS, placeholderData: keepPreviousData, enabled },
  )
}
