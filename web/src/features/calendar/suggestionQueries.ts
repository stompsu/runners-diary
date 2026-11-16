import { keepPreviousData, useQueryClient } from '@tanstack/react-query'
import { useEffect, useMemo, useSyncExternalStore } from 'react'
import { $api } from '@/api/client'
import { useClientConfig } from '@/auth/useClientConfig'
import { gridBounds, shiftMonth, type DiaryMonth } from '@/features/diary/diaryMonth'
import type { GoalDto, TrainingSuggestion } from '@/features/goals/goalTypes'
import type { PlainDate } from '@/lib/dates/plainDate'

/** Plan §4.8: suggestions are cached for 5 minutes, goals for 1; every write that can change them invalidates. */
export const SUGGESTIONS_STALE_TIME_MS = 5 * 60_000
export const GOALS_STALE_TIME_MS = 60_000

const STORAGE_KEY = 'rd.showSuggestions'
const listeners = new Set<() => void>()
/** Only used when `localStorage` cannot be written (private mode, blocked storage): the choice lasts for the session. */
let memoryChoice: boolean | null = null

function readShowSuggestions(): boolean {
  try {
    const stored = localStorage.getItem(STORAGE_KEY)
    if (stored !== null) return stored !== 'false'
  } catch {
    // storage unavailable: fall back to the in-memory choice
  }
  return memoryChoice ?? true
}

/** Persists the "Show suggestions" choice per browser (plan A11; default on). */
export function setShowSuggestions(show: boolean): void {
  try {
    localStorage.setItem(STORAGE_KEY, String(show))
    memoryChoice = null
  } catch {
    memoryChoice = show
  }
  for (const listener of listeners) listener()
}

const subscribe = (listener: () => void) => {
  listeners.add(listener)
  return () => void listeners.delete(listener)
}

/** The toggle's state, shared by the header control and the grid (and other tabs through the `storage` event). */
export function useShowSuggestions(): boolean {
  useEffect(() => {
    const onStorage = (event: StorageEvent) => {
      if (event.key === STORAGE_KEY || event.key === null) for (const listener of listeners) listener()
    }
    window.addEventListener('storage', onStorage)
    return () => window.removeEventListener('storage', onStorage)
  }, [])
  return useSyncExternalStore(subscribe, readShowSuggestions, () => true)
}

/** `client-config.goals.enabled`: the nav entry, the diary layers and the toggle depend on it. */
export function useGoalsEnabled(): boolean {
  return useClientConfig().data?.goals?.enabled === true
}

/** `GET /api/goals` (`listGoals`): at most 50 rows; the diary takes its goal markers and titles from it. */
export function useGoalList(enabled = true) {
  return $api.useQuery('get', '/api/goals', {}, { staleTime: GOALS_STALE_TIME_MS, enabled })
}

interface Range {
  from: PlainDate
  to: PlainDate
}

const suggestionsInit = ({ from, to }: Range, includeDismissed: boolean) => ({
  // `includeDismissed` is left out when false so the grid has one cache entry (like the proposals).
  params: { query: { from, to, ...(includeDismissed ? { includeDismissed: true } : {}) } },
})

/** `GET /api/training-suggestions` (`listTrainingSuggestions`) for a date range (the diary grid bounds). */
export function useSuggestionRange(range: Range, options: { enabled: boolean; includeDismissed?: boolean }) {
  return $api.useQuery('get', '/api/training-suggestions', suggestionsInit(range, options.includeDismissed ?? false), {
    staleTime: SUGGESTIONS_STALE_TIME_MS,
    placeholderData: keepPreviousData,
    enabled: options.enabled,
  })
}

/**
 * The suggestions of a month's grid (ADR 0041 §4). The grid layer needs the feature on and the toggle on; the Day panel
 * passes `always` and `includeDismissed` and reads them whatever the toggle says.
 */
export function useMonthSuggestions(month: DiaryMonth, options: { always?: boolean; includeDismissed?: boolean } = {}) {
  const goalsEnabled = useGoalsEnabled()
  const shown = useShowSuggestions()
  const enabled = goalsEnabled && (options.always === true || shown)
  const query = useSuggestionRange(gridBounds(month), { enabled, includeDismissed: options.includeDismissed })
  return { goalsEnabled, shown, enabled, query }
}

/** Warms the neighbouring months' suggestions once the visible month has loaded. */
export function usePrefetchAdjacentSuggestions(month: DiaryMonth, enabled: boolean) {
  const queryClient = useQueryClient()
  const { year, month: m } = month
  useEffect(() => {
    if (!enabled) return
    for (const delta of [-1, 1]) {
      const target = shiftMonth({ year, month: m }, delta)
      if (!target) continue
      void queryClient.prefetchQuery({
        ...$api.queryOptions('get', '/api/training-suggestions', suggestionsInit(gridBounds(target), false)),
        staleTime: SUGGESTIONS_STALE_TIME_MS,
      })
    }
  }, [queryClient, year, m, enabled])
}

/** Goals that get a marker: Active ones, keyed by their target date. */
export function useGoalMarkers(goals: readonly GoalDto[] | undefined): ReadonlyMap<PlainDate, GoalDto[]> {
  return useMemo(() => {
    const map = new Map<PlainDate, GoalDto[]>()
    for (const goal of goals ?? []) {
      if (goal.status !== 'Active') continue
      map.set(goal.targetDate, [...(map.get(goal.targetDate) ?? []), goal])
    }
    return map
  }, [goals])
}

/** Suggestions grouped by day, in the order the API returned them (by date, then goal). */
export function groupSuggestionsByDate(items: readonly TrainingSuggestion[]): Map<PlainDate, TrainingSuggestion[]> {
  const map = new Map<PlainDate, TrainingSuggestion[]>()
  for (const item of items) map.set(item.date, [...(map.get(item.date) ?? []), item])
  return map
}
