import { QueryClient } from '@tanstack/react-query'
import { describe, expect, it, vi } from 'vitest'
import { createQueryClient, MAX_QUERY_RETRIES, retryDelay, shouldRetryQuery } from './queryClient'

describe('query retry policy', () => {
  it('retries retryable errors up to the limit', () => {
    for (let n = 0; n < MAX_QUERY_RETRIES; n++) expect(shouldRetryQuery(n, { status: 503 })).toBe(true)
    expect(shouldRetryQuery(MAX_QUERY_RETRIES, { status: 503 })).toBe(false)
  })

  it('never retries client errors', () => {
    for (const status of [400, 401, 404, 409, 429]) expect(shouldRetryQuery(0, { status })).toBe(false)
  })

  it('retries network errors', () => {
    expect(shouldRetryQuery(0, new TypeError('Failed to fetch'))).toBe(true)
  })

  it('backs off exponentially with a cap', () => {
    expect([0, 1, 2, 3, 4].map(retryDelay)).toEqual([1000, 2000, 4000, 8000, 8000])
  })

  it('is installed as the default for queries, and mutations do not retry', async () => {
    const client: QueryClient = createQueryClient()
    expect(client.getDefaultOptions().queries?.retry).toBe(shouldRetryQuery)
    expect(client.getDefaultOptions().mutations?.retry).toBe(false)

    const fn = vi.fn<() => Promise<string>>().mockRejectedValue({ status: 404 })
    await expect(client.fetchQuery({ queryKey: ['x'], queryFn: fn, retryDelay: 0 })).rejects.toEqual({ status: 404 })
    expect(fn).toHaveBeenCalledTimes(1)
  })

  it('retries a flaky 503 query until it succeeds', async () => {
    const client = createQueryClient()
    const fn = vi
      .fn<() => Promise<string>>()
      .mockRejectedValueOnce({ status: 503 })
      .mockRejectedValueOnce({ status: 502 })
      .mockResolvedValue('ok')
    await expect(client.fetchQuery({ queryKey: ['y'], queryFn: fn, retryDelay: 0 })).resolves.toBe('ok')
    expect(fn).toHaveBeenCalledTimes(3)
  })
})
