import {
  AuthError,
  BrowserAuthError,
  InteractionRequiredAuthError,
  type AccountInfo,
  type AuthenticationResult,
} from '@azure/msal-browser'
import { describe, expect, it, vi } from 'vitest'
import { buildMsalConfig, createEntraController, type EntraConfig, type MsalApp } from './msalClient'

const config: EntraConfig = {
  clientId: '11111111-1111-4111-8111-111111111111',
  authority: 'https://contoso.ciamlogin.com/',
  apiScope: 'api://22222222-2222-4222-8222-222222222222/access_as_user',
}
const ORIGIN = 'http://localhost:5173'
const account = { homeAccountId: 'h', username: 'ada@example.com', name: 'Ada' } as AccountInfo

function mockApp(overrides: Partial<MsalApp> = {}): MsalApp {
  return {
    loginRedirect: vi.fn<MsalApp['loginRedirect']>(() => Promise.resolve()),
    logoutRedirect: vi.fn<MsalApp['logoutRedirect']>(() => Promise.resolve()),
    acquireTokenSilent: vi.fn<MsalApp['acquireTokenSilent']>(() =>
      Promise.resolve({ accessToken: 'api-token' } as AuthenticationResult),
    ),
    getActiveAccount: vi.fn<MsalApp['getActiveAccount']>(() => account),
    getAllAccounts: vi.fn<MsalApp['getAllAccounts']>(() => [account]),
    ...overrides,
  }
}

describe('buildMsalConfig (ADR 0014 §2)', () => {
  it('uses the redirect bridge as redirectUri, sessionStorage and /signin after logout', () => {
    expect(buildMsalConfig(config, ORIGIN)).toEqual({
      auth: {
        clientId: config.clientId,
        authority: 'https://contoso.ciamlogin.com/',
        redirectUri: `${ORIGIN}/redirect.html`,
        postLogoutRedirectUri: `${ORIGIN}/signin`,
      },
      cache: { cacheLocation: 'sessionStorage' },
    })
  })

  it('does not set knownAuthorities (no custom domain)', () => {
    expect(buildMsalConfig(config, ORIGIN).auth).not.toHaveProperty('knownAuthorities')
  })
})

describe('createEntraController', () => {
  it('starts loginRedirect with only the API scope (no state: returnTo lives on /signin)', async () => {
    const app = mockApp()

    await createEntraController(app, config).login()

    expect(app.loginRedirect).toHaveBeenCalledExactlyOnceWith({ scopes: [config.apiScope] })
  })

  it('acquires the token silently for the API scope and the current account', async () => {
    const app = mockApp()

    expect(await createEntraController(app, config).acquireToken()).toBe('api-token')
    expect(app.acquireTokenSilent).toHaveBeenCalledWith({ scopes: [config.apiScope], account })
  })

  it('falls back to the first cached account when none is active', async () => {
    const app = mockApp({ getActiveAccount: vi.fn<MsalApp['getActiveAccount']>(() => null) })

    await createEntraController(app, config).acquireToken()

    expect(app.acquireTokenSilent).toHaveBeenCalledWith({ scopes: [config.apiScope], account })
  })

  it('returns null when there is no account', async () => {
    const app = mockApp({
      getActiveAccount: vi.fn<MsalApp['getActiveAccount']>(() => null),
      getAllAccounts: vi.fn<MsalApp['getAllAccounts']>(() => []),
    })

    expect(await createEntraController(app, config).acquireToken()).toBeNull()
    expect(app.acquireTokenSilent).not.toHaveBeenCalled()
  })

  it('returns null when interaction is required', async () => {
    const app = mockApp({
      acquireTokenSilent: vi.fn<MsalApp['acquireTokenSilent']>(() =>
        Promise.reject(new InteractionRequiredAuthError('interaction_required', 'user interaction needed')),
      ),
    })

    expect(await createEntraController(app, config).acquireToken()).toBeNull()
  })

  it.each(['monitor_window_timeout', 'timed_out', 'no_tokens_found', 'no_account_error'])(
    'returns null for the non-network BrowserAuthError %s (renewal blocked, e.g. Safari) so the user can sign in again',
    async (code) => {
      const app = mockApp({
        acquireTokenSilent: vi.fn<MsalApp['acquireTokenSilent']>(() =>
          Promise.reject(new BrowserAuthError(code, 'renewal failed')),
        ),
      })

      expect(await createEntraController(app, config).acquireToken()).toBeNull()
    },
  )

  it.each(['network_error', 'post_request_failed'])('rethrows the transient MSAL error %s', async (code) => {
    const error = new AuthError(code, 'unreachable')
    const app = mockApp({ acquireTokenSilent: vi.fn<MsalApp['acquireTokenSilent']>(() => Promise.reject(error)) })

    await expect(createEntraController(app, config).acquireToken()).rejects.toBe(error)
  })

  it('rethrows other errors (e.g. offline) so the request fails instead of signing out', async () => {
    const app = mockApp({
      acquireTokenSilent: vi.fn<MsalApp['acquireTokenSilent']>(() => Promise.reject(new TypeError('offline'))),
    })

    await expect(createEntraController(app, config).acquireToken()).rejects.toThrow('offline')
  })

  it('logs out through the redirect flow with the current account', async () => {
    const app = mockApp()

    await createEntraController(app, config).logout()

    expect(app.logoutRedirect).toHaveBeenCalledWith({ account })
  })
})
