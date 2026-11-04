import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fetchClient } from '@/api/client'
import { getMapsToken, resetMapsTokenCache } from './mapsToken'

vi.mock('@/api/client', () => ({ fetchClient: { GET: vi.fn<(path: string) => Promise<unknown>>() } }))

const get = vi.mocked(fetchClient.GET) as unknown as ReturnType<typeof vi.fn<(path: string) => Promise<unknown>>>
const respond = (token: string, expiresAtMs: number) => ({
  data: { token, expiresAt: new Date(expiresAtMs).toISOString() },
})

beforeEach(() => {
  resetMapsTokenCache()
  get.mockReset()
})

describe('getMapsToken', () => {
  it('fetches the token from the API and caches it in memory until shortly before it expires', async () => {
    let now = Date.parse('2026-11-04T12:00:00Z')
    get.mockResolvedValue(respond('sas-1', now + 2 * 3600_000))

    expect(await getMapsToken(() => now)).toBe('sas-1')
    now += 100 * 60_000 // 20 minutes left: still cached? no, the margin is 5 minutes
    expect(await getMapsToken(() => now)).toBe('sas-1')

    expect(get).toHaveBeenCalledTimes(1)
    expect(get).toHaveBeenCalledWith('/api/maps/token')
  })

  it('asks again when less than five minutes remain', async () => {
    let now = Date.parse('2026-11-04T12:00:00Z')
    get.mockResolvedValueOnce(respond('sas-1', now + 3600_000)).mockResolvedValueOnce(respond('sas-2', now + 7200_000))

    await getMapsToken(() => now)
    now += 56 * 60_000

    expect(await getMapsToken(() => now)).toBe('sas-2')
  })

  it('shares one request between concurrent callers', async () => {
    get.mockResolvedValue(respond('sas-1', Date.now() + 3600_000))

    const tokens = await Promise.all([getMapsToken(), getMapsToken(), getMapsToken()])

    expect(tokens).toEqual(['sas-1', 'sas-1', 'sas-1'])
    expect(get).toHaveBeenCalledTimes(1)
  })

  it('rejects on an API error and retries on the next call', async () => {
    get.mockResolvedValueOnce({ error: { status: 503 } }).mockResolvedValueOnce(respond('sas-2', Date.now() + 3600_000))

    await expect(getMapsToken()).rejects.toThrow('could not be loaded')
    expect(await getMapsToken()).toBe('sas-2')
  })

  it('rejects on a network failure', async () => {
    get.mockRejectedValueOnce(new TypeError('Failed to fetch'))

    await expect(getMapsToken()).rejects.toThrow('Failed to fetch')
  })

  it('never writes the token to web storage', async () => {
    get.mockResolvedValue(respond('sas-secret', Date.now() + 3600_000))

    await getMapsToken()

    expect(JSON.stringify({ ...localStorage })).not.toContain('sas-secret')
    expect(JSON.stringify({ ...sessionStorage })).not.toContain('sas-secret')
  })
})
