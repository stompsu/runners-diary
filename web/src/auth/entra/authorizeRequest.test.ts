import { PublicClientApplication } from '@azure/msal-browser'
import { describe, expect, it } from 'vitest'
import { buildMsalConfig, createEntraController, type EntraConfig } from './msalClient'

// Real MSAL v5 (no tenant needed): authority metadata is supplied offline and navigation is captured, so the test
// sees the exact /authorize request that loginRedirect would send (M3 plan §4 step 3 spike).
const config: EntraConfig = {
  clientId: '11111111-1111-4111-8111-111111111111',
  authority: 'https://contoso.ciamlogin.com/',
  apiScope: 'api://22222222-2222-4222-8222-222222222222/access_as_user',
}
const ORIGIN = 'http://localhost:5173'

async function captureAuthorizeUrl(): Promise<URL> {
  let captured = ''
  const base = buildMsalConfig(config, ORIGIN)
  const app = new PublicClientApplication({
    ...base,
    auth: {
      ...base.auth,
      knownAuthorities: ['contoso.ciamlogin.com'],
      authorityMetadata: JSON.stringify({
        authorization_endpoint: 'https://contoso.ciamlogin.com/tenant/oauth2/v2.0/authorize',
        token_endpoint: 'https://contoso.ciamlogin.com/tenant/oauth2/v2.0/token',
        issuer: 'https://tenant.ciamlogin.com/tenant/v2.0',
        end_session_endpoint: 'https://contoso.ciamlogin.com/tenant/oauth2/v2.0/logout',
      }),
    },
    system: {
      navigationClient: {
        navigateExternal: (url: string) => {
          captured = url
          return Promise.resolve(true)
        },
        navigateInternal: () => Promise.resolve(true),
      },
    },
  })
  await app.initialize()
  await createEntraController(app, config).login()
  return new URL(captured)
}

describe('loginRedirect authorize request (real MSAL v5)', () => {
  it('goes to the tenant authorize endpoint with the SPA client, API scope and the bridge as redirect_uri', async () => {
    const url = await captureAuthorizeUrl()

    expect(url.origin + url.pathname).toBe('https://contoso.ciamlogin.com/tenant/oauth2/v2.0/authorize')
    expect(url.searchParams.get('client_id')).toBe(config.clientId)
    expect(url.searchParams.get('redirect_uri')).toBe(`${ORIGIN}/redirect.html`)
    expect(url.searchParams.get('response_type')).toBe('code')
    expect(url.searchParams.get('code_challenge_method')).toBe('S256')
    expect(url.searchParams.get('scope')?.split(' ')).toEqual(
      expect.arrayContaining([config.apiScope, 'openid', 'profile', 'offline_access']),
    )
  })

  it('marks the interaction as redirect in the library state, which is what the bridge page reads', async () => {
    const url = await captureAuthorizeUrl()

    const [libraryState, userState] = (url.searchParams.get('state') ?? '').split('|')
    expect(JSON.parse(atob(libraryState!))).toMatchObject({ meta: { interactionType: 'redirect' } })
    // returnTo is not round-tripped through the IdP; /signin?returnTo= keeps it (sanitised on the way back).
    expect(userState).toBeUndefined()
  })
})
