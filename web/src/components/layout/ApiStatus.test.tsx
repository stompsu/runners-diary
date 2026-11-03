import { screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { jsonResponse } from '@/test/api'
import { renderRoute } from '@/test/render'

describe('ApiStatus', () => {
  it('shows healthy when /api/health reports Healthy', async () => {
    renderRoute('/insights')

    expect(await screen.findByText('API healthy')).toBeInTheDocument()
    expect(screen.getByRole('status', { name: 'API status' })).toHaveTextContent('API healthy')
    const request = vi.mocked(fetch).mock.calls[0]?.[0] as Request
    expect(new URL(request.url).pathname).toBe('/api/health')
  })

  it('shows degraded when the API reports Degraded', async () => {
    vi.mocked(fetch).mockResolvedValueOnce(jsonResponse({ status: 'Degraded', version: 'test' }))
    renderRoute('/insights')

    expect(await screen.findByText('API degraded')).toBeInTheDocument()
  })

  it('shows unavailable on 503', async () => {
    vi.mocked(fetch).mockResolvedValueOnce(jsonResponse({ status: 'Unhealthy', version: 'test' }, 503))
    renderRoute('/insights')

    expect(await screen.findByText('API unavailable')).toBeInTheDocument()
  })

  it('shows unavailable on network error', async () => {
    vi.mocked(fetch).mockRejectedValueOnce(new TypeError('Failed to fetch'))
    renderRoute('/insights')

    expect(await screen.findByText('API unavailable')).toBeInTheDocument()
  })
})
