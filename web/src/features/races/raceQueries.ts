import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { $api } from '@/api/client'
import { getSessionEpoch } from '@/auth/session'

/** Plan §4.6: the interests rarely change, and a save writes the response straight into the cache. */
export const RACE_INTERESTS_STALE_TIME_MS = 10 * 60_000

/** Prefix of every `listRaceProposals` query (step 7 adds the list query); a profile change makes them all stale. */
export const RACE_PROPOSALS_QUERY_KEY = ['get', '/api/races/proposals'] as const

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
