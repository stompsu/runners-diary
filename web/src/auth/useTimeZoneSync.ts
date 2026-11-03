import { useQueryClient } from '@tanstack/react-query'
import { useEffect, useRef } from 'react'
import { $api } from '@/api/client'
import { useMe } from './useMe'

export function browserTimeZone(): string | null {
  try {
    return Intl.DateTimeFormat().resolvedOptions().timeZone || null
  } catch {
    return null
  }
}

/**
 * Real users start with a default zone (ADR 0008). Once, the browser's IANA zone is written to the profile
 * (`PUT /api/me`), which also makes the zone explicit (`timeZoneIsDefault = false`), so it never repeats.
 * Guests keep Europe/Warsaw (plan §9.3). A failure is silent and not retried until the next page load.
 */
export function useTimeZoneSync(): void {
  const { data: me } = useMe()
  const queryClient = useQueryClient()
  const updateMe = $api.useMutation('put', '/api/me')
  const attempted = useRef(false)
  const { mutate } = updateMe

  useEffect(() => {
    if (!me || attempted.current) return
    if (me.kind !== 'User' || !me.timeZoneIsDefault) return
    const zone = browserTimeZone()
    if (!zone) return
    attempted.current = true
    mutate(
      { body: { displayName: me.displayName, timeZoneId: zone, unitSystem: me.unitSystem } },
      { onSuccess: () => queryClient.invalidateQueries({ queryKey: ['get', '/api/me'] }) },
    )
  }, [me, mutate, queryClient])
}
