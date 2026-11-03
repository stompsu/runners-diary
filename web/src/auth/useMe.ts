import { $api } from '@/api/client'

export const ME_STALE_TIME_MS = 5 * 60 * 1000

/** `GET /api/me`: kind, profile zone, `today` (user's zone) and guest expiry. Shared by guard-side components. */
export function useMe() {
  return $api.useQuery(
    'get',
    '/api/me',
    {},
    {
      staleTime: ME_STALE_TIME_MS,
      // `today` (user's zone) and the guest expiry move with the clock: a tab left open overnight must catch up when the
      // user returns. Only this query; the calendar and run queries keep the app-wide `false` (a refetch there could
      // overwrite what the user is looking at).
      refetchOnWindowFocus: true,
    },
  )
}
