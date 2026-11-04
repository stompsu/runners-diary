import { useQueryClient, type QueryClient } from '@tanstack/react-query'
import { useCallback, useState } from 'react'
import { getSessionEpoch } from '@/auth/session'
import { $api } from '@/api/client'
import type { RunDto } from './runFormTypes'

const runInit = (id: string) => ({ params: { path: { id } } })

/** `GET /api/runs/{id}`: always fresh, because the edit form needs the current `version` (plan §3.3). */
export function useRun(id: string) {
  return $api.useQuery('get', '/api/runs/{id}', runInit(id), { staleTime: 0, gcTime: 0 })
}

/** Fetches the latest run outside of a hook (409 "Save mine anyway" and "Reload latest"). */
export function fetchRun(queryClient: QueryClient, id: string): Promise<RunDto> {
  return queryClient.fetchQuery({ ...$api.queryOptions('get', '/api/runs/{id}', runInit(id)), staleTime: 0 })
}

/**
 * After a create/update/delete: every calendar month (the prefix also matches the prefetched neighbours), run lists
 * and the run itself are stale (plan §3.3). Resolves once the active queries have refetched, so the grid is current
 * before the dialog closes.
 */
export function useInvalidateRuns() {
  const queryClient = useQueryClient()
  // The session this panel was opened in. A mutation that finishes after a sign-out (or a switch to another guest)
  // must not refresh queries that now belong to somebody else.
  const [epoch] = useState(getSessionEpoch)
  return useCallback(
    async (runId?: string) => {
      if (getSessionEpoch() !== epoch) return
      const jobs = [
        queryClient.invalidateQueries({ queryKey: ['get', '/api/calendar/months/{year}/{month}'] }),
        queryClient.invalidateQueries({ queryKey: ['get', '/api/runs'] }),
        // Insights totals, load and races are computed from the runs (M5 AC14); the report is never cached server-side.
        queryClient.invalidateQueries({ queryKey: ['get', '/api/insights/report'] }),
        // A stored summary is stale once the runs change (M6 AC7); the card shows it with a note.
        queryClient.invalidateQueries({ queryKey: ['get', '/api/insights/summary'] }),
      ]
      if (runId) {
        jobs.push(
          queryClient.invalidateQueries({ queryKey: ['get', '/api/runs/{id}', { params: { path: { id: runId } } }] }),
        )
      }
      await Promise.all(jobs)
    },
    [queryClient, epoch],
  )
}

export const useCreateRun = () => $api.useMutation('post', '/api/runs')
export const useUpdateRun = () => $api.useMutation('put', '/api/runs/{id}')
export const useDeleteRun = () => $api.useMutation('delete', '/api/runs/{id}')
