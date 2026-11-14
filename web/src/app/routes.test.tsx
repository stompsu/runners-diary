import { screen, waitFor, within } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { getFakeApi, resetFakeApi } from '@/test/fake'
import { faults } from '@/test/fakeApi'
import { renderRoute } from '@/test/render'

describe('routes', () => {
  afterEach(() => vi.useRealTimers())

  it('redirects / to the month of me.today, not the browser clock', async () => {
    vi.useFakeTimers({ toFake: ['Date'], now: new Date(2031, 4, 20, 12, 0) })
    resetFakeApi({ today: '2026-11-03' })
    const { router } = renderRoute('/')

    expect(await screen.findByRole('heading', { level: 1, name: /November 2026/ })).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/diary/2026/11')
  })

  it('renders the diary for a valid month', async () => {
    renderRoute('/diary/2026/11')

    expect(await screen.findByRole('heading', { level: 1, name: 'Diary – November 2026' })).toBeInTheDocument()
  })

  it.each(['/diary/2026/13', '/diary/abc/1', '/nope'])('renders Not found for %s', async (path) => {
    renderRoute(path)

    expect(await screen.findByRole('heading', { level: 1, name: 'Page not found' })).toBeInTheDocument()
  })

  it('renders insights', async () => {
    renderRoute('/insights')

    expect(await screen.findByRole('heading', { level: 1, name: 'Insights' })).toBeInTheDocument()
  })

  it('redirects /settings to the race interests page', async () => {
    const { router } = renderRoute('/settings')

    expect(await screen.findByRole('heading', { level: 1, name: 'Race interests' })).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/settings/races')
  })

  it('shows Settings in the navigation only while race proposals are enabled', async () => {
    renderRoute('/insights')
    const nav = await screen.findByRole('navigation', { name: 'Main' })
    expect(await within(nav).findByRole('link', { name: 'Settings' })).toHaveAttribute('href', '/settings')
  })

  it('hides Settings in the navigation when race proposals are disabled', async () => {
    resetFakeApi({ racesEnabled: false })
    renderRoute('/insights')
    const nav = await screen.findByRole('navigation', { name: 'Main' })
    await screen.findByRole('heading', { level: 1, name: 'Insights' })
    // The client config has arrived once the API status settles; the link must still be absent.
    await waitFor(() => expect(getFakeApi().requests).toContain('GET /api/client-config'))
    await new Promise((resolve) => setTimeout(resolve, 50))
    expect(within(nav).queryByRole('link', { name: 'Settings' })).not.toBeInTheDocument()
    expect(within(nav).getByRole('link', { name: 'Diary' })).toBeInTheDocument()
  })

  it('shows translated navigation, not raw keys', async () => {
    renderRoute('/insights')

    const nav = await screen.findByRole('navigation', { name: 'Main' })
    expect(nav).toHaveTextContent('Diary')
    expect(nav).not.toHaveTextContent('nav.diary')
  })

  describe('auth guard', () => {
    it.each(['/', '/diary', '/diary/2026/11', '/insights', '/settings', '/settings/races', '/nope'])(
      'redirects anonymous %s to /signin',
      async (path) => {
        const { router } = renderRoute(path, { session: 'none' })

        expect(await screen.findByRole('heading', { level: 1, name: 'Sign in to Runners Diary' })).toBeInTheDocument()
        expect(router.state.location.pathname).toBe('/signin')
      },
    )

    it('remembers where the visitor wanted to go', async () => {
      const { router } = renderRoute('/diary/2026/11?x=1', { session: 'none' })

      await screen.findByRole('heading', { level: 1, name: 'Sign in to Runners Diary' })
      expect(router.state.location.search).toBe('?returnTo=%2Fdiary%2F2026%2F11%3Fx%3D1')
    })

    it('does not call the API for protected data while signed out', async () => {
      renderRoute('/diary/2026/11', { session: 'none' })

      await screen.findByRole('heading', { level: 1, name: 'Sign in to Runners Diary' })
      expect(getFakeApi().requests).not.toContain('GET /api/me')
      expect(getFakeApi().requests).not.toContain('GET /api/health')
    })

    it('a 401 in the middle of a session ends it and explains why', async () => {
      getFakeApi().addFault(faults.status({ path: '/api/me' }, 401))
      const { router } = renderRoute('/diary/2026/11')

      expect(await screen.findByText('Your demo has ended.')).toBeInTheDocument()
      expect(router.state.location.pathname).toBe('/signin')
      expect(new URLSearchParams(router.state.location.search).get('returnTo')).toBe('/diary/2026/11')
      expect(localStorage.getItem('rd.guestSession')).toBeNull()
    })

    it('keeps /signin reachable without a session', async () => {
      const { router } = renderRoute('/signin', { session: 'none' })

      expect(await screen.findByRole('button', { name: 'Try as guest' })).toBeInTheDocument()
      expect(router.state.location.pathname).toBe('/signin')
    })
  })
})
