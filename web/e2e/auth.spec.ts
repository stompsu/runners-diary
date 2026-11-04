import { expect, type Page } from '@playwright/test'
import { faults } from '../src/test/fakeApi/index.ts'
import { mockApi, restoreGuest, seriousViolations, test } from './support.ts'

// Sign-in page states, demo expiry, Entra visibility/redirect and returnTo hardening (AC1-AC5).

const GUEST_POST = { method: 'POST', path: '/api/guest-sessions' }
const tryAsGuest = (page: Page) => page.getByRole('button', { name: 'Try as guest' })

test.describe('sign-in page', () => {
  test('shows the waking-up notice after 3 s without retrying the POST', async ({ page }) => {
    const fake = await mockApi(page)
    fake.addFault(faults.delay(GUEST_POST, 4500, 1))
    await page.goto('/signin')

    await tryAsGuest(page).click()
    await expect(page.getByRole('button', { name: 'Starting your demo…' })).toBeDisabled()
    await expect(page.getByText('Waking up the server, this can take a moment…')).toHaveCount(0)
    await expect(page.getByText('Waking up the server, this can take a moment…')).toBeVisible({ timeout: 6000 })

    await expect(page).toHaveURL(/\/diary\/2026\/11$/, { timeout: 10_000 })
    expect(fake.requests.filter((r) => r === 'POST /api/guest-sessions')).toHaveLength(1)
  })

  test('429 shows the Retry-After wait and allows trying again', async ({ page }) => {
    const fake = await mockApi(page)
    fake.addFault(faults.rateLimited(GUEST_POST, 42, 1))
    await page.goto('/signin')

    await tryAsGuest(page).click()

    await expect(page.getByRole('alert')).toHaveText('Too many requests. Please try again in 42 s.')
    await expect(tryAsGuest(page)).toBeEnabled()
    await tryAsGuest(page).click()
    await expect(page).toHaveURL(/\/diary\/2026\/11$/)
  })

  test('503 (guest capacity) shows the Retry-After wait', async ({ page }) => {
    const fake = await mockApi(page)
    fake.addFault(faults.guestCapacity(GUEST_POST, 30, 1))
    await page.goto('/signin')

    await tryAsGuest(page).click()

    await expect(page.getByRole('alert')).toHaveText('The demo is full right now. Please try again in 30 s.')
    await expect(page).toHaveURL(/\/signin/)
  })

  test('a network failure shows a generic message', async ({ page }) => {
    const fake = await mockApi(page)
    fake.addFault(faults.network(GUEST_POST, 1))
    await page.goto('/signin')

    await tryAsGuest(page).click()

    await expect(page.getByRole('alert')).toBeVisible()
    await expect(tryAsGuest(page)).toBeEnabled()
  })

  test('has no serious or critical accessibility violations', async ({ page }) => {
    await mockApi(page)
    await page.goto('/signin?reason=expired')
    await expect(page.getByText('Your demo has ended.')).toBeVisible()
    await expect(tryAsGuest(page)).toBeVisible()

    expect(await seriousViolations(page)).toEqual([])
  })
})

test.describe('demo expiry', () => {
  test('when the countdown ends the session is cleared and the sign-in page says the demo ended', async ({ page }) => {
    const fake = await mockApi(page)
    await restoreGuest(page, fake, { expiresInMs: 4000 })
    await page.goto('/diary/2026/11')
    await expect(page.getByRole('timer')).toContainText('left')

    await expect(page).toHaveURL(/\/signin\?.*reason=expired/, { timeout: 15_000 })
    await expect(page.getByText('Your demo has ended.')).toBeVisible()
    expect(await page.evaluate(() => localStorage.getItem('rd.guestSession'))).toBeNull()
  })

  test('an already expired stored session is not restored', async ({ page }) => {
    const fake = await mockApi(page)
    await restoreGuest(page, fake, { expiresInMs: -1000 })
    await page.goto('/diary/2026/11')

    await expect(page).toHaveURL(/\/signin/)
    await expect(tryAsGuest(page)).toBeVisible()
  })
})

const ENTRA = {
  clientId: '11111111-2222-4333-8444-555555555555',
  authority: 'https://e2etenant.ciamlogin.com/e2etenant.onmicrosoft.com/',
  apiScope: 'api://e2e-api/access_as_user',
}

test.describe('Entra sign-in visibility', () => {
  test('hidden when client-config has entra: null, the guest path works', async ({ page }) => {
    await mockApi(page, { entra: null })
    await page.goto('/signin')

    await expect(tryAsGuest(page)).toBeVisible()
    await expect(page.getByRole('button', { name: 'Sign in', exact: true })).toHaveCount(0)
  })

  test('hidden when client-config fails, the guest path works', async ({ page }) => {
    const fake = await mockApi(page)
    fake.addFault(faults.status({ method: 'GET', path: '/api/client-config' }, 500))
    await page.goto('/signin')

    await expect(tryAsGuest(page)).toBeVisible()
    await expect(page.getByRole('button', { name: 'Sign in', exact: true })).toHaveCount(0)
    await tryAsGuest(page).click()
    await expect(page).toHaveURL(/\/diary\/2026\/11$/)
  })

  test('shown when configured and redirects to the tenant /authorize endpoint, never carrying a token', async ({
    page,
  }) => {
    await mockApi(page, { entra: ENTRA })
    const host = 'https://e2etenant.ciamlogin.com'
    const base = `${host}/e2etenant.onmicrosoft.com`
    const discovered: string[] = []
    const authorizeRequests: string[] = []
    await page.route(`${host}/**`, async (route) => {
      const url = new URL(route.request().url())
      if (url.pathname.endsWith('/.well-known/openid-configuration')) {
        discovered.push(url.pathname)
        await route.fulfill({
          json: {
            issuer: `${base}/v2.0`,
            authorization_endpoint: `${base}/oauth2/v2.0/authorize`,
            token_endpoint: `${base}/oauth2/v2.0/token`,
            end_session_endpoint: `${base}/oauth2/v2.0/logout`,
            jwks_uri: `${base}/discovery/v2.0/keys`,
          },
          headers: { 'access-control-allow-origin': '*' },
        })
      } else if (url.pathname.endsWith('/oauth2/v2.0/authorize')) {
        // This is the navigation under test: capture it instead of reaching the real IdP.
        authorizeRequests.push(url.toString())
        await route.fulfill({ contentType: 'text/html', body: '<title>Stub IdP</title>' })
      } else {
        await route.fulfill({ status: 404, headers: { 'access-control-allow-origin': '*' }, body: '{}' })
      }
    })

    await page.goto('/signin?returnTo=%2Fdiary%2F2026%2F10')
    const signIn = page.getByRole('button', { name: 'Sign in', exact: true })
    await expect(signIn).toBeVisible()
    await expect(tryAsGuest(page)).toBeVisible()
    await expect(signIn).toBeEnabled({ timeout: 15_000 })

    await signIn.click()
    await expect(page).toHaveTitle('Stub IdP')

    expect(authorizeRequests).toHaveLength(1)
    const authorize = new URL(authorizeRequests[0]!)
    expect(authorize.origin + authorize.pathname).toBe(`${base}/oauth2/v2.0/authorize`)
    const q = authorize.searchParams
    expect(q.get('client_id')).toBe(ENTRA.clientId)
    expect(q.get('redirect_uri')).toBe('http://localhost:4173/redirect.html')
    expect(q.get('response_type')).toBe('code')
    expect(q.get('code_challenge_method')).toBe('S256')
    expect(q.get('code_challenge')).toBeTruthy()
    // Only MSAL's opaque state: returnTo is not round-tripped through the IdP, it stays in the /signin URL.
    expect(q.get('state')).toBeTruthy()
    expect(q.get('state')).not.toContain('|')
    expect(q.get('state')).not.toContain('diary')
    expect(q.get('scope')).toContain(ENTRA.apiScope)
    expect(q.get('scope')).toContain('openid')
    // No secrets or tokens travel in the URL (AC17).
    expect([...q.keys()].filter((k) => /token$|secret|^code$|password/i.test(k) && k !== 'code_challenge')).toEqual([])
    expect(authorize.toString()).not.toMatch(/Bearer|client_secret/i)
    expect(discovered.length).toBeGreaterThan(0)
  })
})

test.describe('returnTo hardening (open redirect)', () => {
  const attacks = [
    'https://evil.example/steal',
    '//evil.example/steal',
    '/\\evil.example/steal',
    '/%5Cevil.example/steal',
    '/%2F%2Fevil.example/steal',
    '/\t/evil.example/steal',
    'javascript:alert(1)',
    'data:text/html,<script>alert(1)</script>',
    '/signin?returnTo=https://evil.example',
  ]

  for (const attack of attacks) {
    test(`ignores returnTo=${JSON.stringify(attack)}`, async ({ page }) => {
      await mockApi(page)
      const evilRequests: string[] = []
      await page.route(/^https?:\/\/([^/]*\.)?evil\.example(\/|$)/, (route) => {
        evilRequests.push(route.request().url())
        return route.abort()
      })
      await page.goto(`/signin?returnTo=${encodeURIComponent(attack)}`)

      await tryAsGuest(page).click()

      // Lands in the app (the safe default is the current month), never on another origin.
      await expect(page).toHaveURL(/^http:\/\/localhost:4173\/diary\/2026\/11$/)
      expect(evilRequests).toEqual([])
    })
  }

  test('a safe same-origin path is honoured, including its query', async ({ page }) => {
    await mockApi(page)
    await page.goto(`/signin?returnTo=${encodeURIComponent('/diary/2026/10/list')}`)

    await tryAsGuest(page).click()

    await expect(page).toHaveURL(/\/diary\/2026\/10\/list$/)
    await expect(page.getByRole('dialog', { name: 'Runs in October 2026' })).toBeVisible()
  })
})
