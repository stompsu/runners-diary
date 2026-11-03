import { screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { jsonResponse, overrideEndpoint } from '@/test/api'
import { renderRoute } from '@/test/render'

describe('ApiStatus', () => {
  it('shows healthy when /api/health reports Healthy', async () => {
    renderRoute('/insights')

    expect(await screen.findByText('API healthy')).toBeInTheDocument()
    expect(screen.getByRole('status', { name: 'API status' })).toHaveTextContent('API healthy')
    const paths = vi.mocked(fetch).mock.calls.map(([input]) => new URL((input as Request).url).pathname)
    expect(paths).toContain('/api/health')
  })

  it('shows degraded when the API reports Degraded', async () => {
    overrideEndpoint('/api/health', () => jsonResponse({ status: 'Degraded', version: 'test' }))
    renderRoute('/insights')

    expect(await screen.findByText('API degraded')).toBeInTheDocument()
  })

  it('shows unavailable on 503', async () => {
    overrideEndpoint('/api/health', () => jsonResponse({ status: 'Unhealthy', version: 'test' }, 503))
    renderRoute('/insights')

    expect(await screen.findByText('API unavailable')).toBeInTheDocument()
  })

  it('shows unavailable on network error', async () => {
    overrideEndpoint('/api/health', () => {
      throw new TypeError('Failed to fetch')
    })
    renderRoute('/insights')

    expect(await screen.findByText('API unavailable')).toBeInTheDocument()
  })
})
