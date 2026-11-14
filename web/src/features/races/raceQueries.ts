import { keepPreviousData, useQueryClient } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { $api } from '@/api/client'
import { getSessionEpoch } from '@/auth/session'
import { useClientConfig } from '@/auth/useClientConfig'
import { gridBounds, shiftMonth, type DiaryMonth } from '@/features/diary/diaryMonth'
import type { PlainDate } from '@/lib/dates/plainDate'
import type { RaceProposal } from './raceTypes'

/** Plan §4.6: the interests rarely change, and a save writes the response straight into the cache. */
export const RACE_INTERESTS_STALE_TIME_MS = 10 * 60_000

/** Plan §4.6: proposals and one race are cached for 5 minutes; every write that can change them invalidates. */
export const RACE_PROPOSALS_STALE_TIME_MS = 5 * 60_000

/** Prefix of every `listRaceProposals` query; a profile, state or run change makes them all stale. */
export const RACE_PROPOSALS_QUERY_KEY = ['get', '/api/races/proposals'] as const
/** Prefix of every `getRace` query. */
export const RACE_DETAILS_QUERY_KEY = ['get', '/api/races/{raceId}'] as const

/** `GET /api/races/interests` (`getRaceInterests`). */
export function useRaceInterests(enabled = true) {
  return $api.useQuery('get', '/api/races/interests', {}, { staleTime: RACE_INTERESTS_STALE_TIME_MS, enabled })
}

/**
 * `PUT /api/races/interests` (`updateRaceInterests`): sets the interests cache from the response and invalidates the
 * proposals (which depend on the profile). Ignored when the session changed while the request was in flight.
 */
export function useUpdateRaceInterests() {
  const queryClient = useQueryClient()
  const [epoch] = useState(getSessionEpoch)
  return $api.useMutation('put', '/api/races/interests', {
    onSuccess: async (data) => {
      if (getSessionEpoch() !== epoch) return
      queryClient.setQueryData($api.queryOptions('get', '/api/races/interests', {}).queryKey, data)
      await queryClient.invalidateQueries({ queryKey: RACE_PROPOSALS_QUERY_KEY })
    },
  })
}

interface ProposalRange {
  from: PlainDate
  to: PlainDate
}

const proposalsInit = ({ from, to }: ProposalRange, includeDismissed: boolean) => ({
  // `includeDismissed` is left out when false so the calendar and the list panel share one cache entry.
  params: { query: { from, to, ...(includeDismissed ? { includeDismissed: true } : {}) } },
})

/** `GET /api/races/proposals` (`listRaceProposals`) for a date range (the diary grid bounds). */
export function useRaceProposals(range: ProposalRange, options: { enabled: boolean; includeDismissed?: boolean }) {
  return $api.useQuery('get', '/api/races/proposals', proposalsInit(range, options.includeDismissed ?? false), {
    staleTime: RACE_PROPOSALS_STALE_TIME_MS,
    placeholderData: keepPreviousData,
    enabled: options.enabled,
  })
}

/**
 * Whether the diary asks for proposals (ADR 0037 §4): the feature is on, a profile is saved and `showInCalendar` is
 * set. `listable` is the weaker "a profile exists" that the list panel and the day section need.
 */
export function useProposalsGate() {
  const config = useClientConfig()
  const racesEnabled = config.data?.races.enabled === true
  const interests = useRaceInterests(racesEnabled)
  const configured = racesEnabled && interests.data?.configured === true
  return {
    racesEnabled,
    interests,
    configured,
    shown: configured && interests.data?.showInCalendar === true,
  }
}

/** The proposals of a month's grid, or nothing while they are off. Signed-up races are not proposals (their run shows). */
export function useMonthProposals(month: DiaryMonth, options: { includeDismissed?: boolean; always?: boolean } = {}) {
  const gate = useProposalsGate()
  const range = gridBounds(month)
  const enabled = options.always ? gate.configured : gate.shown
  const query = useRaceProposals(range, { enabled, includeDismissed: options.includeDismissed })
  return { gate, query, enabled, range }
}

/** Warms the neighbouring months' proposals once the visible month has loaded (same trigger as the calendar). */
export function usePrefetchAdjacentProposals(month: DiaryMonth, enabled: boolean) {
  const queryClient = useQueryClient()
  const { year, month: m } = month
  useEffect(() => {
    if (!enabled) return
    for (const delta of [-1, 1]) {
      const target = shiftMonth({ year, month: m }, delta)
      if (!target) continue
      void queryClient.prefetchQuery({
        ...$api.queryOptions('get', '/api/races/proposals', proposalsInit(gridBounds(target), false)),
        staleTime: RACE_PROPOSALS_STALE_TIME_MS,
      })
    }
  }, [queryClient, year, m, enabled])
}

/** Proposals that get a bubble: everything but signed-up races (the run's own bubble shows) and dismissed ones. */
export function bubbleProposals(items: readonly RaceProposal[] | undefined): RaceProposal[] {
  return (items ?? []).filter((item) => item.state !== 'SignedUp' && item.state !== 'Dismissed')
}

const raceInit = (raceId: string) => ({ params: { path: { raceId } } })

/** `GET /api/races/{raceId}` (`getRace`). */
export function useRaceDetails(raceId: string) {
  return $api.useQuery('get', '/api/races/{raceId}', raceInit(raceId), { staleTime: RACE_PROPOSALS_STALE_TIME_MS })
}

/** Proposals and race details are stale after a state change, a signup or a run write. */
export function useInvalidateRaces() {
  const queryClient = useQueryClient()
  const [epoch] = useState(getSessionEpoch)
  return async () => {
    if (getSessionEpoch() !== epoch) return
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: RACE_PROPOSALS_QUERY_KEY }),
      queryClient.invalidateQueries({ queryKey: RACE_DETAILS_QUERY_KEY }),
    ])
  }
}

/** `PUT /api/races/{raceId}/state` (`setRaceProposalState`); success refreshes the proposals and the race. */
export function useSetRaceProposalState() {
  const invalidate = useInvalidateRaces()
  return $api.useMutation('put', '/api/races/{raceId}/state', { onSuccess: () => invalidate() })
}

/** `POST /api/races/proposals/seen` (`markRaceProposalsSeen`): the "New" badges are cleared in the cached lists. */
export function useMarkRaceProposalsSeen() {
  const queryClient = useQueryClient()
  const [epoch] = useState(getSessionEpoch)
  return $api.useMutation('post', '/api/races/proposals/seen', {
    onSuccess: () => {
      if (getSessionEpoch() !== epoch) return
      // No refetch: the open panel keeps its badges, a later open must not show them again.
      queryClient.setQueriesData<{ items: RaceProposal[] }>({ queryKey: RACE_PROPOSALS_QUERY_KEY }, (data) =>
        data ? { ...data, items: data.items.map((item) => ({ ...item, isNew: false })) } : data,
      )
    },
  })
}

/** `POST /api/races/{raceId}/signup` (`signUpForRace`). The caller invalidates with the shared run-write helper. */
export const useSignUpForRace = () => $api.useMutation('post', '/api/races/{raceId}/signup')
