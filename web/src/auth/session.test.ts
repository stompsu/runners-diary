import { describe, expect, it, vi } from 'vitest'
import { GUEST_STORAGE_KEY, readGuestSession } from './guestSession'
import {
  ENTRA_HINT_KEY,
  bearerMatchesSession,
  clientSideExpiry,
  finishRestoring,
  getAccessToken,
  getSession,
  onSignOut,
  registerEntraController,
  resetSessionForTests,
  signInWithEntra,
  signOut,
  signOutEntra,
  startEntraSession,
  startGuestSession,
  type EntraController,
} from './session'

const HOUR = 3_600_000

function controller(overrides: Partial<EntraController> = {}): EntraController {
  return {
    acquireToken: vi.fn<EntraController['acquireToken']>(() => Promise.resolve('entra-token')),
    login: vi.fn<EntraController['login']>(() => Promise.resolve()),
    logout: vi.fn<EntraController['logout']>(() => Promise.resolve()),
    ...overrides,
  }
}

describe('guest storage', () => {
  it('persists the session in localStorage under rd.guestSession', () => {
    startGuestSession({ accessToken: 'g1', expiresAt: Date.now() + HOUR, userId: 'u1' })

    expect(JSON.parse(localStorage.getItem(GUEST_STORAGE_KEY)!)).toMatchObject({ accessToken: 'g1', userId: 'u1' })
    expect(getSession()).toMatchObject({ kind: 'guest', userId: 'u1' })
    expect(sessionStorage.length).toBe(0)
  })

  it('accepts an ISO expiry from the API response', () => {
    const expiresAt = new Date(Date.now() + HOUR).toISOString()
    startGuestSession({ accessToken: 'g1', expiresAt, userId: 'u1' })

    expect(readGuestSession()?.expiresAt).toBe(Date.parse(expiresAt))
  })

  it('restores a valid stored session after a reload', () => {
    startGuestSession({ accessToken: 'g1', expiresAt: Date.now() + HOUR, userId: 'u1' })
    resetSessionForTests() // simulates a new page load: module state gone, storage kept

    expect(getSession()).toMatchObject({ kind: 'guest', userId: 'u1' })
  })

  it('discards an expired stored session and removes it', () => {
    localStorage.setItem(
      GUEST_STORAGE_KEY,
      JSON.stringify({ accessToken: 'old', expiresAt: Date.now() - 1000, userId: 'u1' }),
    )
    resetSessionForTests()

    expect(getSession()).toEqual({ kind: 'anonymous', reason: null })
    expect(localStorage.getItem(GUEST_STORAGE_KEY)).toBeNull()
  })

  it.each(['not json', '{}', '{"accessToken":"","expiresAt":9999999999999,"userId":"u"}', '[]', 'null'])(
    'discards malformed content %s',
    (raw) => {
      localStorage.setItem(GUEST_STORAGE_KEY, raw)
      resetSessionForTests()

      expect(getSession().kind).toBe('anonymous')
      expect(localStorage.getItem(GUEST_STORAGE_KEY)).toBeNull()
    },
  )
})

describe('getAccessToken', () => {
  it('returns the guest token while it is valid', async () => {
    startGuestSession({ accessToken: 'g1', expiresAt: Date.now() + HOUR, userId: 'u1' })

    expect(await getAccessToken()).toBe('g1')
  })

  it('signs out with reason expired once the guest token has expired', async () => {
    const now = Date.now()
    startGuestSession({ accessToken: 'g1', expiresAt: now + 1000, userId: 'u1' })
    vi.spyOn(Date, 'now').mockReturnValue(now + 2000)

    expect(await getAccessToken()).toBeNull()
    expect(getSession()).toEqual({ kind: 'anonymous', reason: 'expired' })
    expect(localStorage.getItem(GUEST_STORAGE_KEY)).toBeNull()
    vi.restoreAllMocks()
  })

  it('returns null when anonymous', async () => {
    expect(await getAccessToken()).toBeNull()
  })

  it('uses the Entra controller for real users', async () => {
    const c = controller()
    registerEntraController(c)
    startEntraSession('Ada')

    expect(await getAccessToken()).toBe('entra-token')
    expect(c.acquireToken).toHaveBeenCalledOnce()
  })

  it('signs out with reason sessionEnded when interaction is required', async () => {
    registerEntraController(
      controller({ acquireToken: vi.fn<EntraController['acquireToken']>(() => Promise.resolve(null)) }),
    )
    startEntraSession('Ada')

    expect(await getAccessToken()).toBeNull()
    expect(getSession()).toEqual({ kind: 'anonymous', reason: 'sessionEnded' })
  })

  it('lets other token errors through without ending the session', async () => {
    registerEntraController(
      controller({
        acquireToken: vi.fn<EntraController['acquireToken']>(() => Promise.reject(new TypeError('offline'))),
      }),
    )
    startEntraSession('Ada')

    await expect(getAccessToken()).rejects.toThrow('offline')
    expect(getSession().kind).toBe('entra')
  })
})

describe('signOut', () => {
  it('clears storage, records the reason and notifies sign-out listeners', () => {
    const listener = vi.fn<() => void>()
    onSignOut(listener)
    startGuestSession({ accessToken: 'g1', expiresAt: Date.now() + HOUR, userId: 'u1' })

    signOut('signedOut')

    expect(getSession()).toEqual({ kind: 'anonymous', reason: 'signedOut' })
    expect(localStorage.getItem(GUEST_STORAGE_KEY)).toBeNull()
    expect(listener).toHaveBeenCalledOnce()
  })

  it('does not notify again when already signed out', () => {
    const listener = vi.fn<() => void>()
    onSignOut(listener)

    signOut('expired')

    expect(listener).not.toHaveBeenCalled()
  })

  it('bearerMatchesSession only accepts the current guest token', () => {
    startGuestSession({ accessToken: 'g1', expiresAt: Date.now() + HOUR, userId: 'u1' })

    expect(bearerMatchesSession('Bearer g1')).toBe(true)
    expect(bearerMatchesSession('Bearer other')).toBe(false)
    expect(bearerMatchesSession(null)).toBe(false)
  })
})

describe('Entra session', () => {
  it('starting an Entra session removes a guest session', () => {
    startGuestSession({ accessToken: 'g1', expiresAt: Date.now() + HOUR, userId: 'u1' })
    startEntraSession('Ada')

    expect(localStorage.getItem(GUEST_STORAGE_KEY)).toBeNull()
    expect(sessionStorage.getItem(ENTRA_HINT_KEY)).toBe('active')
  })

  it('signing in from a guest session clears the guest first, then starts the redirect', async () => {
    const c = controller()
    registerEntraController(c)
    startGuestSession({ accessToken: 'g1', expiresAt: Date.now() + HOUR, userId: 'u1' })

    await signInWithEntra()

    expect(localStorage.getItem(GUEST_STORAGE_KEY)).toBeNull()
    expect(c.login).toHaveBeenCalledWith()
    expect(sessionStorage.getItem(ENTRA_HINT_KEY)).toBe('pending')
  })

  it('drops the pending hint when the redirect cannot start', async () => {
    registerEntraController(
      controller({ login: vi.fn<EntraController['login']>(() => Promise.reject(new Error('blocked'))) }),
    )

    await expect(signInWithEntra()).rejects.toThrow('blocked')
    expect(sessionStorage.getItem(ENTRA_HINT_KEY)).toBeNull()
  })

  it('a reload with the hint is "restoring" until the boundary resolves it', () => {
    sessionStorage.setItem(ENTRA_HINT_KEY, 'pending')
    resetSessionForTests()
    expect(getSession().kind).toBe('restoring')

    finishRestoring('sessionEnded')

    expect(getSession()).toEqual({ kind: 'anonymous', reason: 'sessionEnded' })
    expect(sessionStorage.getItem(ENTRA_HINT_KEY)).toBeNull()
  })

  it('signOutEntra ends the local session first and then logs out at the identity provider', async () => {
    const c = controller()
    registerEntraController(c)
    startEntraSession('Ada')

    await signOutEntra()

    expect(getSession()).toEqual({ kind: 'anonymous', reason: 'signedOut' })
    expect(c.logout).toHaveBeenCalledOnce()
  })
})

describe('guest expiry vs client clock skew', () => {
  const serverNow = Date.parse('2026-11-03T12:00:00Z')
  const header = new Date(serverNow).toUTCString()

  it('measures the remaining lifetime on the server clock and applies it to the client clock', () => {
    const clientNow = serverNow + 10 * 60_000 // client clock runs 10 minutes ahead
    const expiresAt = new Date(serverNow + HOUR).toISOString()

    expect(clientSideExpiry(expiresAt, header, clientNow)).toBe(clientNow + HOUR)
  })

  it('works for a client clock that runs behind', () => {
    const clientNow = serverNow - 2 * HOUR
    expect(clientSideExpiry(new Date(serverNow + HOUR).toISOString(), header, clientNow)).toBe(clientNow + HOUR)
  })

  it.each([undefined, null, '', 'garbage'])('falls back to the raw timestamp when the Date header is %j', (date) => {
    const expiresAt = new Date(serverNow + HOUR).toISOString()
    expect(clientSideExpiry(expiresAt, date, 123)).toBe(serverNow + HOUR)
  })

  it.each(['not a date', '', 'NaN'])('is NaN for the malformed expiry %j', (value) => {
    expect(clientSideExpiry(value, header)).toBeNaN()
  })

  it('stores the skew-corrected expiry', () => {
    const skewMs = 3 * HOUR // the client clock is 3 h ahead of the server
    const serverDate = new Date(Date.now() - skewMs)
    const expiresAt = new Date(serverDate.getTime() + 24 * HOUR).toISOString()

    startGuestSession({ accessToken: 'g1', expiresAt, userId: 'u1', serverDate: serverDate.toUTCString() })

    // Without the correction this would be 21 h; with it the guest gets the real 24 h (give or take the header's 1 s).
    const remaining = readGuestSession()!.expiresAt - Date.now()
    expect(Math.abs(remaining - 24 * HOUR)).toBeLessThan(5000)
  })

  it('treats a malformed or already-past expiry as an expired session instead of starting one', () => {
    startGuestSession({ accessToken: 'g1', expiresAt: 'soon', userId: 'u1' })
    expect(getSession()).toEqual({ kind: 'anonymous', reason: 'expired' })
    expect(localStorage.getItem(GUEST_STORAGE_KEY)).toBeNull()

    startGuestSession({ accessToken: 'g1', expiresAt: Date.now() - 1000, userId: 'u1' })
    expect(getSession()).toEqual({ kind: 'anonymous', reason: 'expired' })
  })
})

describe('guest session in other tabs (storage event)', () => {
  const store = (accessToken: string, userId = 'u2') =>
    localStorage.setItem(GUEST_STORAGE_KEY, JSON.stringify({ accessToken, expiresAt: Date.now() + HOUR, userId }))
  const fire = (key: string | null, newValue: string | null) =>
    window.dispatchEvent(new StorageEvent('storage', { key, newValue }))

  it('signs this tab out when another tab removed the guest session', () => {
    startGuestSession({ accessToken: 'g1', expiresAt: Date.now() + HOUR, userId: 'u1' })
    const cleared = vi.fn<() => void>()
    onSignOut(cleared)
    localStorage.removeItem(GUEST_STORAGE_KEY)

    fire(GUEST_STORAGE_KEY, null)

    expect(getSession()).toEqual({ kind: 'anonymous', reason: 'signedOut' })
    expect(cleared).toHaveBeenCalledOnce()
  })

  it('also reacts to localStorage.clear() in another tab (key null)', () => {
    startGuestSession({ accessToken: 'g1', expiresAt: Date.now() + HOUR, userId: 'u1' })
    localStorage.clear()

    fire(null, null)

    expect(getSession().kind).toBe('anonymous')
  })

  it("switches to the other tab's guest session and clears the caches of the old one", async () => {
    startGuestSession({ accessToken: 'g1', expiresAt: Date.now() + HOUR, userId: 'u1' })
    const cleared = vi.fn<() => void>()
    onSignOut(cleared)
    store('g2')

    fire(GUEST_STORAGE_KEY, localStorage.getItem(GUEST_STORAGE_KEY))

    expect(getSession()).toMatchObject({ kind: 'guest', userId: 'u2' })
    expect(await getAccessToken()).toBe('g2')
    expect(bearerMatchesSession('Bearer g1')).toBe(false)
    expect(cleared).toHaveBeenCalledOnce()
  })

  it('ignores the same token and unrelated keys', () => {
    startGuestSession({ accessToken: 'g1', expiresAt: Date.now() + HOUR, userId: 'u1' })
    const cleared = vi.fn<() => void>()
    onSignOut(cleared)

    fire(GUEST_STORAGE_KEY, localStorage.getItem(GUEST_STORAGE_KEY))
    fire('something.else', null)

    expect(getSession()).toMatchObject({ kind: 'guest', userId: 'u1' })
    expect(cleared).not.toHaveBeenCalled()
  })

  it('does not touch an Entra session', () => {
    startEntraSession('Ada')
    store('g2')

    fire(GUEST_STORAGE_KEY, localStorage.getItem(GUEST_STORAGE_KEY))

    expect(getSession()).toEqual({ kind: 'entra', name: 'Ada' })
  })
})
