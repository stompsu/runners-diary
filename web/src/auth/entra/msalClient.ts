import {
  createStandardPublicClientApplication,
  AuthError,
  type AccountInfo,
  type Configuration,
  type IPublicClientApplication,
} from '@azure/msal-browser'
import type { EntraController } from '../session'

/** MSAL error codes meaning "could not reach the service": transient, so the session must survive them. */
const NETWORK_ERROR_CODES: ReadonlySet<string> = new Set(['network_error', 'post_request_failed'])

/** The user backed out at the IdP (closed/cancelled the sign-in page): not an error worth a "session ended" notice. */
export function isUserCancel(error: unknown): boolean {
  if (!(error instanceof AuthError)) return false
  return (
    error.errorCode === 'user_cancelled' ||
    error.errorCode === 'access_denied' ||
    // Entra External ID / B2C report the cancel button as AADB2C90091 in the error description.
    error.errorMessage.includes('AADB2C90091')
  )
}

/**
 * Any MSAL error that is not a network failure means the silent renewal cannot succeed without the user:
 * `interaction_required`, `monitor_window_timeout`/`timed_out` (iframe renewal blocked, e.g. Safari ITP),
 * `no_tokens_found`, `no_account_error`... Treating them all as "session ended" avoids a user stuck on a failing app.
 */
export function isSessionEndingError(error: unknown): boolean {
  return error instanceof AuthError && !NETWORK_ERROR_CODES.has(error.errorCode)
}

/** Non-secret settings from `GET /api/client-config` (ADR 0015). */
export interface EntraConfig {
  clientId: string
  authority: string
  apiScope: string
}

/** The slice of the MSAL application the controller uses (lets tests pass a mock). */
export type MsalApp = Pick<
  IPublicClientApplication,
  'loginRedirect' | 'logoutRedirect' | 'acquireTokenSilent' | 'getActiveAccount' | 'getAllAccounts'
>

/**
 * ADR 0014 §2: redirect flow with the v5 redirect bridge as `redirectUri` (the page that handles the IdP response),
 * sessionStorage cache (the MSAL default and the bridge's hard-wired location), and `/signin` after logout.
 */
export function buildMsalConfig(config: EntraConfig, origin: string): Configuration {
  return {
    auth: {
      clientId: config.clientId,
      authority: config.authority,
      redirectUri: `${origin}/redirect.html`,
      postLogoutRedirectUri: `${origin}/signin`,
    },
    cache: { cacheLocation: 'sessionStorage' },
  }
}

export function currentAccount(app: Pick<MsalApp, 'getActiveAccount' | 'getAllAccounts'>): AccountInfo | null {
  return app.getActiveAccount() ?? app.getAllAccounts()[0] ?? null
}

export function createEntraController(app: MsalApp, config: EntraConfig): EntraController {
  return {
    async acquireToken() {
      const account = currentAccount(app)
      if (!account) return null
      try {
        const result = await app.acquireTokenSilent({ scopes: [config.apiScope], account })
        return result.accessToken
      } catch (error) {
        if (isSessionEndingError(error)) return null
        throw error
      }
    },
    async login() {
      // No `state`: the redirect lands back on /signin?returnTo=..., which validates and applies returnTo itself.
      await app.loginRedirect({ scopes: [config.apiScope] })
    },
    async logout() {
      const account = currentAccount(app)
      await app.logoutRedirect(account ? { account } : undefined)
    },
  }
}

export interface EntraBootResult {
  app: IPublicClientApplication
  controller: EntraController
  /** Account from a completed redirect or the sessionStorage cache; null when nobody is signed in. */
  account: AccountInfo | null
  /** Set when handling the redirect response failed (e.g. the user cancelled at the IdP). */
  redirectError: unknown
}

let booting: Promise<EntraBootResult> | null = null
let bootedFor: string | null = null

/** Creates and initializes MSAL once, completes a pending redirect, and finds the cached account. */
export function bootEntra(config: EntraConfig, origin: string = window.location.origin): Promise<EntraBootResult> {
  const key = `${config.clientId}|${config.authority}`
  if (booting && bootedFor === key) return booting
  bootedFor = key
  booting = (async () => {
    const app = await createStandardPublicClientApplication(buildMsalConfig(config, origin))
    let redirectError: unknown = null
    let account: AccountInfo | null = null
    try {
      const result = await app.handleRedirectPromise()
      account = result?.account ?? null
    } catch (error) {
      redirectError = error
    }
    account ??= currentAccount(app)
    if (account) app.setActiveAccount(account)
    return { app, controller: createEntraController(app, config), account, redirectError }
  })()
  return booting
}
