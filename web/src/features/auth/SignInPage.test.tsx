import { act, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { GUEST_STORAGE_KEY } from '@/auth/guestSession'
import { getSession } from '@/auth/session'
import { getFakeApi } from '@/test/fake'
import { faults } from '@/test/fakeApi'
import { renderRoute } from '@/test/render'

const guestButton = () => screen.findByRole('button', { name: 'Try as guest' })
const postsToGuestSessions = () => getFakeApi().requests.filter((r) => r === 'POST /api/guest-sessions').length

/** Answers POST /api/guest-sessions with a hand-made body; everything else still goes to the fake API. */
function answerGuestSessions(body: unknown, headers: Record<string, string> = {}) {
  const fallback = vi.mocked(fetch).getMockImplementation()!
  vi.mocked(fetch).mockImplementation((input, init) => {
    const request = input instanceof Request ? input : new Request(input, init)
    if (request.method === 'POST' && new URL(request.url).pathname === '/api/guest-sessions') {
      return Promise.resolve(
        new Response(JSON.stringify(body), {
          status: 201,
          headers: { 'Content-Type': 'application/json', ...headers },
        }),
      )
    }
    return fallback(input, init)
  })
}

describe('SignInPage', () => {
  afterEach(() => vi.useRealTimers())

  it('is public and offers guest access without waiting for the config', async () => {
    renderRoute('/signin', { session: 'none' })

    expect(await screen.findByRole('heading', { level: 1, name: 'Sign in to Runners Diary' })).toBeInTheDocument()
    expect(await guestButton()).toBeEnabled()
  })

  it('starts a guest session, stores the token and continues to returnTo', async () => {
    const user = userEvent.setup()
    const { router } = renderRoute('/signin?returnTo=%2Fdiary%2F2026%2F05', { session: 'none' })

    await user.click(await guestButton())

    await waitFor(() => expect(router.state.location.pathname).toBe('/diary/2026/05'))
    expect(getSession().kind).toBe('guest')
    expect(JSON.parse(localStorage.getItem(GUEST_STORAGE_KEY)!)).toHaveProperty('accessToken')
  })

  it('corrects the guest expiry for a client clock that differs from the server (Date header)', async () => {
    const user = userEvent.setup()
    const HOUR = 3_600_000
    const serverNow = Date.now() - 3 * HOUR // the client clock is 3 h ahead
    answerGuestSessions(
      {
        accessToken: getFakeApi().issueToken('Guest'),
        tokenType: 'Bearer',
        expiresAt: new Date(serverNow + 24 * HOUR).toISOString(),
        userId: 'g-skew',
      },
      { Date: new Date(serverNow).toUTCString() },
    )
    renderRoute('/signin', { session: 'none' })

    await user.click(await guestButton())

    await waitFor(() => expect(getSession().kind).toBe('guest'))
    const stored = JSON.parse(localStorage.getItem(GUEST_STORAGE_KEY)!) as { expiresAt: number }
    expect(Math.abs(stored.expiresAt - Date.now() - 24 * HOUR)).toBeLessThan(5000)
  })

  it('does not start a session from a malformed expiry', async () => {
    const user = userEvent.setup()
    answerGuestSessions({ accessToken: 't', tokenType: 'Bearer', expiresAt: 'soon', userId: 'u' })
    renderRoute('/signin', { session: 'none' })

    await user.click(await guestButton())

    expect(await screen.findByText('Your demo has ended.')).toBeInTheDocument()
    expect(getSession().kind).toBe('anonymous')
    expect(localStorage.getItem(GUEST_STORAGE_KEY)).toBeNull()
  })

  it('ignores an unsafe returnTo and lands on the current month', async () => {
    const user = userEvent.setup()
    const { router } = renderRoute('/signin?returnTo=%2F%2Fevil.example', { session: 'none' })

    await user.click(await guestButton())

    await waitFor(() => expect(router.state.location.pathname).toBe('/diary/2026/11'))
  })

  it('sends a signed-in visitor straight to returnTo', async () => {
    const { router } = renderRoute('/signin?returnTo=%2Finsights')

    await waitFor(() => expect(router.state.location.pathname).toBe('/insights'))
  })

  it.each([
    ['expired', 'Your demo has ended.'],
    ['sessionEnded', 'Your session has ended. Please sign in again.'],
    ['signedOut', 'You have been signed out.'],
  ])('explains reason=%s', async (reason, message) => {
    renderRoute(`/signin?reason=${reason}`, { session: 'none' })

    expect(await screen.findByText(message)).toBeInTheDocument()
  })

  it('ignores an unknown reason', async () => {
    renderRoute('/signin?reason=<b>x</b>', { session: 'none' })

    await guestButton()
    expect(screen.queryByText('x')).not.toBeInTheDocument()
  })

  it('shows the "waking up" notice only after 3 seconds of waiting', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime })
    getFakeApi().addFault(faults.delay({ method: 'POST', path: '/api/guest-sessions' }, 5000))
    renderRoute('/signin', { session: 'none' })

    await user.click(await guestButton())
    expect(screen.getByRole('button', { name: 'Starting your demo…' })).toBeDisabled()
    await act(() => vi.advanceTimersByTimeAsync(2500))
    expect(screen.queryByText(/Waking up the server/)).not.toBeInTheDocument()

    await act(() => vi.advanceTimersByTimeAsync(1000))
    expect(screen.getByText(/Waking up the server/)).toBeInTheDocument()

    await act(() => vi.advanceTimersByTimeAsync(2000))
    await waitFor(() => expect(getSession().kind).toBe('guest'))
    expect(postsToGuestSessions()).toBe(1)
  })

  it('shows the rate-limit message with Retry-After and does not retry by itself', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime })
    getFakeApi().addFault(faults.rateLimited({ method: 'POST', path: '/api/guest-sessions' }, 42))
    renderRoute('/signin', { session: 'none' })

    await user.click(await guestButton())

    expect(await screen.findByRole('alert')).toHaveTextContent('Too many requests. Please try again in 42 s.')
    await act(() => vi.advanceTimersByTimeAsync(30_000))
    expect(postsToGuestSessions()).toBe(1)
    expect(getSession().kind).toBe('anonymous')
    expect(screen.getByRole('button', { name: 'Try as guest' })).toBeEnabled()
  })

  it('shows the capacity message for 503 with Retry-After', async () => {
    const user = userEvent.setup()
    getFakeApi().addFault(faults.guestCapacity({ method: 'POST', path: '/api/guest-sessions' }, 30))
    renderRoute('/signin', { session: 'none' })

    await user.click(await guestButton())

    expect(await screen.findByRole('alert')).toHaveTextContent('The demo is full right now. Please try again in 30 s.')
    expect(postsToGuestSessions()).toBe(1)
  })

  it('shows the capacity message without a wait when Retry-After is absent', async () => {
    const user = userEvent.setup()
    getFakeApi().addFault(faults.status({ method: 'POST', path: '/api/guest-sessions' }, 500))
    renderRoute('/signin', { session: 'none' })

    await user.click(await guestButton())

    expect(await screen.findByRole('alert')).toHaveTextContent('Something went wrong. Please try again.')
  })

  it('succeeds on a manual second attempt after a failure', async () => {
    const user = userEvent.setup()
    getFakeApi().addFault(faults.guestCapacity({ method: 'POST', path: '/api/guest-sessions' }, 1, 1))
    renderRoute('/signin', { session: 'none' })

    await user.click(await guestButton())
    await screen.findByRole('alert')
    await user.click(screen.getByRole('button', { name: 'Try as guest' }))

    await waitFor(() => expect(getSession().kind).toBe('guest'))
    expect(postsToGuestSessions()).toBe(2)
  })

  it('hides "Sign in" when Entra is not configured', async () => {
    renderRoute('/signin', { session: 'none' })

    await guestButton()
    await waitFor(() => expect(getFakeApi().requests).toContain('GET /api/client-config'))
    await waitFor(() => expect(screen.queryByText('Checking sign-in options…')).not.toBeInTheDocument())
    expect(screen.queryByRole('button', { name: 'Sign in' })).not.toBeInTheDocument()
  })

  it('hides "Sign in" and keeps guests working when the config cannot be loaded', async () => {
    getFakeApi().addFault(faults.status({ path: '/api/client-config' }, 500))
    renderRoute('/signin', { session: 'none' })

    await guestButton()
    await waitFor(() => expect(screen.queryByText('Checking sign-in options…')).not.toBeInTheDocument())
    expect(screen.queryByRole('button', { name: 'Sign in' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Try as guest' })).toBeEnabled()
  })
})
