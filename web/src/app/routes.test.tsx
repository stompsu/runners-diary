import { screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { renderRoute } from '@/test/render'

describe('routes', () => {
  afterEach(() => vi.useRealTimers())

  it('redirects / to the current month', async () => {
    vi.useFakeTimers({ toFake: ['Date'], now: new Date(2026, 10, 3, 12, 0) })
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

  it('shows translated navigation, not raw keys', async () => {
    renderRoute('/insights')

    const nav = await screen.findByRole('navigation', { name: 'Main' })
    expect(nav).toHaveTextContent('Diary')
    expect(nav).not.toHaveTextContent('nav.diary')
  })
})
