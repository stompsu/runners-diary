import { act, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { startGuestSession } from '@/auth/session'
import { getFakeApi } from '@/test/fake'
import { renderRoute } from '@/test/render'

function guestExpiringIn(seconds: number) {
  const token = getFakeApi().issueToken('Guest')
  startGuestSession({ accessToken: token, expiresAt: Date.now() + seconds * 1000, userId: 'g' })
}

describe('DemoBanner', () => {
  afterEach(() => vi.useRealTimers())

  it('shows the demo notice and a countdown to expiry that ticks every second', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    guestExpiringIn(3725)
    renderRoute('/insights', { session: 'none' })

    expect(await screen.findByText('You are in demo mode. Data is deleted after 24 hours.')).toBeInTheDocument()
    expect(screen.getByRole('timer')).toHaveTextContent('1:02:05 left')

    await act(() => vi.advanceTimersByTimeAsync(5000))
    expect(screen.getByRole('timer')).toHaveTextContent('1:02:00 left')
  })

  it('signs out at expiry and shows "Your demo has ended."', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    guestExpiringIn(3)
    const { router } = renderRoute('/insights', { session: 'none' })
    await screen.findByRole('timer')

    await act(() => vi.advanceTimersByTimeAsync(4000))

    expect(await screen.findByText('Your demo has ended.')).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/signin')
    expect(localStorage.getItem('rd.guestSession')).toBeNull()
  })

  it('End demo signs out locally, clears the cache and keeps the diary out of the next session', async () => {
    const user = userEvent.setup()
    const { router, queryClient } = renderRoute('/insights')
    await screen.findByText('API healthy')
    await waitFor(() => expect(queryClient.getQueryCache().getAll().length).toBeGreaterThan(0))

    await user.click(screen.getByRole('button', { name: 'End demo' }))

    expect(await screen.findByText('You have been signed out.')).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/signin')
    expect(router.state.location.search).not.toContain('returnTo')
    expect(localStorage.getItem('rd.guestSession')).toBeNull()
    const keys = queryClient
      .getQueryCache()
      .getAll()
      .map((q) => q.queryKey[1])
    expect(keys).not.toContain('/api/me')
    expect(keys).not.toContain('/api/health')
  })

  it('is not shown to real users, who get a sign-out button instead', async () => {
    const user = userEvent.setup()
    const { router } = renderRoute('/insights', { session: 'user' })

    expect(await screen.findByText('Signed in as Test Runner')).toBeInTheDocument()
    expect(screen.queryByRole('timer')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'End demo' })).not.toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Sign out' }))
    expect(await screen.findByText('You have been signed out.')).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/signin')
  })

  it('offers "Sign in with an account" only when Entra is configured', async () => {
    renderRoute('/insights')
    await screen.findByRole('timer')
    await waitFor(() => expect(getFakeApi().requests).toContain('GET /api/client-config'))
    expect(screen.queryByRole('link', { name: 'Sign in with an account' })).not.toBeInTheDocument()
  })
})
