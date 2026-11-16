import { keepPreviousData, useQueryClient } from '@tanstack/react-query'
import { useCallback, useMemo, useState } from 'react'
import { $api, fetchClient } from '@/api/client'
import { getSessionEpoch } from '@/auth/session'
import { GOALS_STALE_TIME_MS, useSuggestionRange } from '@/features/calendar/suggestionQueries'
import { useDebouncedValue } from '@/hooks/useDebouncedValue'
import { addDays } from '@/lib/dates/plainDate'
import { GOAL_QUERY_KEY, GOALS_QUERY_KEY, SUGGESTIONS_QUERY_KEY } from './goalKeys'
import type { EstimateGoalRequest, GoalWriteRequest, TrainingSuggestion } from './goalTypes'
import type { RunDto, RunWriteRequest } from '@/features/runs/runFormTypes'

export const ESTIMATE_DEBOUNCE_MS = 400

const goalInit = (goalId: string) => ({ params: { path: { goalId } } })

/** `GET /api/goals/{goalId}` (`getGoal`). */
export function useGoal(goalId: string, enabled = true) {
  return $api.useQuery('get', '/api/goals/{goalId}', goalInit(goalId), { staleTime: GOALS_STALE_TIME_MS, enabled })
}

/**
 * `POST /api/goals/estimate` (`estimateGoal`): a pure read. The input is debounced (400 ms) and the previous answer
 * stays on screen while the next loads. `null` input (the relevant form part is not valid yet) disables the query.
 */
export function useGoalEstimate(input: EstimateGoalRequest | null) {
  const key = input ? JSON.stringify(input) : null
  const debounced = useDebouncedValue(key, ESTIMATE_DEBOUNCE_MS)
  const body = useMemo(() => (debounced ? (JSON.parse(debounced) as EstimateGoalRequest) : null), [debounced])
  return $api.useQuery(
    'post',
    '/api/goals/estimate',
    { body: body ?? { type: 'Distance', distanceMeters: 0, targetDate: '2000-01-01' } },
    { enabled: body !== null, placeholderData: keepPreviousData, staleTime: GOALS_STALE_TIME_MS },
  )
}

/**
 * Goals, one goal, the suggestions and the calendar are stale after a goal write, a generation, a dismissal. Calendar
 * months too: a deleted goal clears `goalId` on its accepted runs. Ignored when the session changed meanwhile.
 */
export function useInvalidateGoals() {
  const queryClient = useQueryClient()
  const [epoch] = useState(getSessionEpoch)
  return useCallback(async () => {
    if (getSessionEpoch() !== epoch) return
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: GOALS_QUERY_KEY }),
      queryClient.invalidateQueries({ queryKey: GOAL_QUERY_KEY }),
      queryClient.invalidateQueries({ queryKey: SUGGESTIONS_QUERY_KEY }),
      queryClient.invalidateQueries({ queryKey: ['get', '/api/calendar/months/{year}/{month}'] }),
    ])
  }, [queryClient, epoch])
}

export const useCreateGoal = () => $api.useMutation('post', '/api/goals')
export const useUpdateGoal = () => $api.useMutation('put', '/api/goals/{goalId}')
export const useDeleteGoal = () => $api.useMutation('delete', '/api/goals/{goalId}')
export const useGenerateTrainingPlan = () => $api.useMutation('post', '/api/goals/{goalId}/plan')
export const useAcceptSuggestion = () => $api.useMutation('post', '/api/training-suggestions/{suggestionId}/accept')
export const useDismissSuggestion = () => $api.useMutation('post', '/api/training-suggestions/{suggestionId}/dismiss')

export type { GoalWriteRequest }

/**
 * "Edit & accept": `acceptTrainingSuggestion` with `{ run }`. The server keys its 400s `run.<member>`; the prefix is
 * removed so the run form's field mapping (ADR 0018) applies unchanged. Throws the error body like the hooks do.
 */
export async function acceptSuggestionWithRun(suggestionId: string, run: RunWriteRequest): Promise<RunDto> {
  const { data, error } = await fetchClient.POST('/api/training-suggestions/{suggestionId}/accept', {
    params: { path: { suggestionId } },
    body: { run },
  })
  if (error) {
    const body = error as { errors?: Record<string, string[]> }
    if (body.errors && typeof body.errors === 'object') {
      const stripped: Record<string, string[]> = {}
      for (const [key, messages] of Object.entries(body.errors)) stripped[key.replace(/^run\./, '')] = messages
      throw { ...body, errors: stripped }
    }
    throw error
  }
  return data
}

export function pendingSuggestions(items: readonly TrainingSuggestion[]): TrainingSuggestion[] {
  return items.filter((item) => item.status === 'Pending')
}

/**
 * The suggestions of one goal for the goal page. The list endpoint spans at most 92 days and a plan reaches 16 weeks,
 * so two consecutive ranges starting at `today` are read (about 26 weeks) and merged.
 */
export function useGoalSuggestions(goalId: string, today: string | undefined, includeDismissed: boolean) {
  const start = today ?? '2000-01-01'
  const enabled = today !== undefined
  const first = useSuggestionRange({ from: start, to: addDays(start, 91) }, { enabled, includeDismissed })
  const second = useSuggestionRange(
    { from: addDays(start, 92), to: addDays(start, 183) },
    { enabled, includeDismissed },
  )
  const items = useMemo(
    () => [...(first.data?.items ?? []), ...(second.data?.items ?? [])].filter((item) => item.goalId === goalId),
    [first.data, second.data, goalId],
  )
  return { items, query: first.isError ? first : second, ready: !!first.data && !!second.data }
}
