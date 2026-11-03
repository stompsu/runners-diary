import { describe, expect, it, vi } from 'vitest'
import { getSession, registerEntraController, signOut, startEntraSession, startGuestSession } from '@/auth/session'
import { parseProblem } from '@/lib/api/problem'
import { faults } from '@/test/fakeApi'
import { getFakeApi } from '@/test/fake'
import { fetchClient, isAnonymousEndpoint } from './client'

function lastRequest(): Request {
  const calls = vi.mocked(fetch).mock.calls
  return calls[calls.length - 1]![0] as Request
}

function signInGuest(): string {
  const token = getFakeApi().issueToken('Guest')
  startGuestSession({ accessToken: token, expiresAt: Date.now() + 3_600_000, userId: 'u' })
  return token
}

describe('auth middleware: requests', () => {
  it('attaches the guest bearer token to API calls', async () => {
    const token = signInGuest()

    const { response } = await fetchClient.GET('/api/me')

    expect(lastRequest().headers.get('Authorization')).toBe(`Bearer ${token}`)
    expect(response.status).toBe(200)
  })

  it('attaches the MSAL token for real users', async () => {
    const token = getFakeApi().issueToken('User')
    registerEntraController({
      acquireToken: () => Promise.resolve(token),
      login: () => Promise.resolve(),
      logout: () => Promise.resolve(),
    })
    startEntraSession('Ada')

    const { data } = await fetchClient.GET('/api/me')

    expect(lastRequest().headers.get('Authorization')).toBe(`Bearer ${token}`)
    expect(data?.kind).toBe('User')
  })

  it.each([
    ['GET', '/api/client-config'],
    ['GET', '/api/health'],
    ['POST', '/api/guest-sessions'],
  ])('sends no token to the anonymous endpoint %s %s', async (method, path) => {
    signInGuest()

    const send = fetchClient.request as (method: string, path: string, init: object) => Promise<unknown>
    await send(method.toLowerCase(), path, {})

    expect(lastRequest().headers.has('Authorization')).toBe(false)
  })

  it('sends no Authorization header when signed out', async () => {
    await fetchClient.GET('/api/me')

    expect(lastRequest().headers.has('Authorization')).toBe(false)
  })

  it('knows the anonymous endpoints exactly', () => {
    expect(isAnonymousEndpoint('POST', 'http://localhost/api/guest-sessions')).toBe(true)
    expect(isAnonymousEndpoint('GET', 'http://localhost/api/guest-sessions')).toBe(false)
    expect(isAnonymousEndpoint('GET', 'http://localhost/api/me')).toBe(false)
  })
})

describe('auth middleware: 401', () => {
  it('signs a guest out with reason expired', async () => {
    signInGuest()
    getFakeApi().addFault(faults.status({ path: '/api/me' }, 401))

    await fetchClient.GET('/api/me')

    expect(getSession()).toEqual({ kind: 'anonymous', reason: 'expired' })
  })

  it('signs an Entra user out with reason sessionEnded', async () => {
    registerEntraController({
      acquireToken: () => Promise.resolve('rejected-token'),
      login: () => Promise.resolve(),
      logout: () => Promise.resolve(),
    })
    startEntraSession('Ada')

    await fetchClient.GET('/api/me') // unknown token -> the fake answers 401

    expect(getSession()).toEqual({ kind: 'anonymous', reason: 'sessionEnded' })
  })

  it('ignores a late 401 that belongs to an earlier session', async () => {
    signInGuest()
    getFakeApi().addFault(faults.delay({ path: '/api/me' }, 20, 1))
    const pending = fetchClient.GET('/api/me')
    signOut('signedOut')
    const second = getFakeApi().issueToken('Guest')
    startGuestSession({ accessToken: second, expiresAt: Date.now() + 3_600_000, userId: 'u2' })
    getFakeApi().addFault(faults.status({ path: '/api/me' }, 401, 1))

    await pending

    expect(getSession().kind).toBe('guest')
  })

  it('does not treat 401 without a token as a sign-out of anything', async () => {
    signOut('signedOut')
    await fetchClient.GET('/api/me')

    expect(getSession()).toEqual({ kind: 'anonymous', reason: 'signedOut' })
  })
})

describe('auth middleware: error shape', () => {
  it('turns an HTML proxy error into a { status } object', async () => {
    getFakeApi().addFault(faults.html({ path: '/api/client-config' }, 502))

    const { error } = await fetchClient.GET('/api/client-config')

    expect(error).toMatchObject({ status: 502 })
    expect(parseProblem(error).kind).toBe('unavailable')
  })

  it('embeds Retry-After so hook consumers (which only see the body) can read it', async () => {
    getFakeApi().addFault(faults.rateLimited({ path: '/api/guest-sessions' }, 42))

    const { error } = await fetchClient.POST('/api/guest-sessions')

    expect(parseProblem(error)).toEqual({ kind: 'rateLimited', status: 429, retryAfterSeconds: 42 })
  })

  it('keeps problem+json bodies intact', async () => {
    signInGuest()

    const { error, response } = await fetchClient.GET('/api/runs/{id}', { params: { path: { id: 'missing' } } })

    expect(response.status).toBe(404)
    expect(parseProblem(error).kind).toBe('notFound')
  })

  it('leaves network failures as TypeError', async () => {
    getFakeApi().addFault(faults.network({ path: '/api/client-config' }))

    await expect(fetchClient.GET('/api/client-config')).rejects.toBeInstanceOf(TypeError)
  })
})
