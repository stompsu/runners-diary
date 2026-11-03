import { QueryClient } from '@tanstack/react-query'
import { isRetryable } from '@/lib/api/problem'

export const MAX_QUERY_RETRIES = 3

/** Queries retry (≤3, backoff) only for network errors and 502/503/504; never 4xx. Mutations never retry. */
export function shouldRetryQuery(failureCount: number, error: unknown): boolean {
  return failureCount < MAX_QUERY_RETRIES && isRetryable(error)
}

export function retryDelay(attempt: number): number {
  return Math.min(1000 * 2 ** attempt, 8000)
}

export function createQueryClient() {
  return new QueryClient({
    defaultOptions: {
      queries: { staleTime: 30_000, refetchOnWindowFocus: false, retry: shouldRetryQuery, retryDelay },
      mutations: { retry: false },
    },
  })
}
