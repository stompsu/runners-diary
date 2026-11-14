import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, renderHook } from '@testing-library/react'
import type { ReactNode } from 'react'
import { describe, expect, it, vi } from 'vitest'
import { signOut, startGuestSession } from '@/auth/session'
import { useInvalidateRuns } from './runQueries'

const HOUR = 3_600_000

function setup() {
  const queryClient = new QueryClient()
  const invalidate = vi.spyOn(queryClient, 'invalidateQueries').mockResolvedValue()
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  )
  return { invalidate, ...renderHook(() => useInvalidateRuns(), { wrapper }) }
}

describe('useInvalidateRuns', () => {
  it('refreshes the calendar and run lists in the session the panel was opened in', async () => {
    startGuestSession({ accessToken: 'g1', expiresAt: Date.now() + HOUR, userId: 'u1' })
    const { result, invalidate } = setup()

    await result.current('run-1')

    expect(invalidate).toHaveBeenCalledTimes(7)
    // Signed-up and dateChanged derive from runs: proposals and the linked race go stale with every run write (M8 §4.6).
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['get', '/api/races/proposals'] })
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['get', '/api/races/{raceId}'] })
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['get', '/api/insights/report'] })
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['get', '/api/insights/summary'] })
  })

  it('does nothing when the session ended or changed while the mutation was in flight', async () => {
    startGuestSession({ accessToken: 'g1', expiresAt: Date.now() + HOUR, userId: 'u1' })
    const { result, invalidate } = setup()

    act(() => signOut('signedOut'))
    await result.current('run-1')
    act(() => startGuestSession({ accessToken: 'g2', expiresAt: Date.now() + HOUR, userId: 'u2' }))
    await result.current('run-1')

    expect(invalidate).not.toHaveBeenCalled()
  })
})
