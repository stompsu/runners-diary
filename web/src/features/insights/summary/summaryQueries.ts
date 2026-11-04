import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useCallback, useEffect, useRef } from 'react'
import { $api, fetchClient } from '@/api/client'
import { INSIGHTS_STALE_TIME_MS } from '../insightsQueries'
import type { CompareMode, SummaryResponse } from '../insightsTypes'
import { classifySummaryError, isAbortError, type SummaryError } from './summaryProblem'

export interface SummaryRange {
  from: string
  to: string
  compare: CompareMode
}

/** Query-key prefix shared with `useInvalidateRuns`: a run create/update/delete makes every stored summary stale. */
export const SUMMARY_QUERY_KEY = ['get', '/api/insights/summary'] as const

const summaryInit = (range: SummaryRange) => ({
  params: { query: { from: range.from, to: range.to, compare: range.compare, lang: 'en' } },
})

const summaryKey = (range: SummaryRange) =>
  $api.queryOptions('get', '/api/insights/summary', summaryInit(range)).queryKey

/**
 * `GET /api/insights/summary` (`getInsightsSummary`). Never calls the model: it reads the stored summary, the demo
 * example or the automatic note, plus the quota. The server sends `private, no-store`, so this cache is the only one.
 */
export function useInsightsSummary(range: SummaryRange) {
  return $api.useQuery('get', '/api/insights/summary', summaryInit(range), { staleTime: INSIGHTS_STALE_TIME_MS })
}

interface GenerateVariables {
  regenerate: boolean
  signal: AbortSignal
}

/**
 * `POST /api/insights/summary` (`generateInsightsSummary`), only ever on an explicit click. No retry: a generation
 * costs quota. Success replaces the cached GET (the answer has the same shape); any other outcome (a cancel included)
 * refetches it, because attempts may count toward the quota and availability may have changed.
 * `cancel()` aborts the request; the page leaving (or the period changing) aborts it too.
 */
export function useGenerateSummary(range: SummaryRange) {
  const queryClient = useQueryClient()
  const controller = useRef<AbortController | null>(null)

  const mutation = useMutation<SummaryResponse, unknown, GenerateVariables>({
    retry: false,
    mutationFn: async ({ regenerate, signal }) => {
      const { data, error } = await fetchClient.POST('/api/insights/summary', {
        body: { from: range.from, to: range.to, compare: range.compare, lang: 'en', regenerate },
        signal,
      })
      if (data === undefined) throw error ?? new Error('Empty response')
      return data
    },
    onSuccess: (data) => {
      queryClient.setQueryData(summaryKey(range), data)
    },
    onError: () => {
      // A cancel is recorded by the server too (Cancelled ledger row), so the remaining count is refreshed either way.
      void queryClient.invalidateQueries({ queryKey: SUMMARY_QUERY_KEY })
    },
  })

  const { mutate } = mutation
  const generate = useCallback(
    (regenerate: boolean) => {
      controller.current?.abort()
      const next = new AbortController()
      controller.current = next
      mutate({ regenerate, signal: next.signal })
    },
    [mutate],
  )
  const cancel = useCallback(() => controller.current?.abort(), [])
  useEffect(() => () => controller.current?.abort(), [])

  const aborted = mutation.isError && isAbortError(mutation.error)
  const error: SummaryError | null = mutation.isError && !aborted ? classifySummaryError(mutation.error) : null

  return {
    generate,
    cancel,
    isPending: mutation.isPending,
    isSuccess: mutation.isSuccess,
    aborted,
    error,
    /** Changes with every request, so a result can be told from the one before even when the status stays the same. */
    attempt: mutation.submittedAt,
  }
}
