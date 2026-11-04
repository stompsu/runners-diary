import { expect, type Page } from '@playwright/test'
import { test } from './support.ts'
import { createFakeApi } from '../src/test/fakeApi/index.ts'

// Calendar and run-type reads are answered by the in-memory fake API; everything else is mocked per test below.
const fake = createFakeApi({ today: '2026-11-03' })

// A guest session the app restores from localStorage (ADR 0014 §4); the API itself is mocked per test.
const GUEST_SESSION = {
  accessToken: fake.issueToken('Guest'),
  expiresAt: Date.now() + 24 * 60 * 60 * 1000,
  userId: '00000000-0000-4000-8000-000000000001',
}

test.beforeEach(async ({ page }) => {
  await page.route(/\/api\/(calendar|run-types)/, (route) => fake.fulfillRoute(route))
  await page.route('**/api/health', (route) => route.fulfill({ json: { status: 'Healthy', version: 'e2e' } }))
  await page.route('**/api/me', (route) =>
    route.fulfill({
      json: {
        id: GUEST_SESSION.userId,
        kind: 'Guest',
        displayName: null,
        timeZoneId: 'Europe/Warsaw',
        timeZoneIsDefault: true,
        unitSystem: 'Metric',
        today: '2026-11-03',
        guestExpiresAt: new Date(GUEST_SESSION.expiresAt).toISOString(),
      },
    }),
  )
  await page.route('**/api/client-config', (route) => route.fulfill({ json: { entra: null } }))
})

async function signInAsGuest(page: Page) {
  await page.addInitScript((session) => localStorage.setItem('rd.guestSession', JSON.stringify(session)), GUEST_SESSION)
}

test('anonymous visitors are sent to the sign-in page and remember where they wanted to go', async ({ page }) => {
  await page.goto('/insights')

  await expect(page).toHaveURL(/\/signin\?returnTo=%2Finsights$/)
  await expect(page.getByRole('heading', { level: 1, name: 'Sign in to Runners Diary' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Try as guest' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Sign in', exact: true })).toHaveCount(0)
})

test('"Try as guest" creates a demo session and returns to the requested page', async ({ page }) => {
  await page.route('**/api/guest-sessions', (route) =>
    route.fulfill({
      status: 201,
      json: {
        accessToken: 'e2e-new-guest',
        tokenType: 'Bearer',
        expiresAt: new Date(Date.now() + 3_600_000).toISOString(),
        userId: '1',
      },
    }),
  )
  await page.goto('/insights')

  await page.getByRole('button', { name: 'Try as guest' }).click()

  await expect(page).toHaveURL(/\/insights$/)
  await expect(page.getByText('You are in demo mode. Data is deleted after 24 hours.')).toBeVisible()
  expect(await page.evaluate(() => localStorage.getItem('rd.guestSession'))).toContain('e2e-new-guest')
})

test('End demo returns to the sign-in page and forgets the session', async ({ page }) => {
  await signInAsGuest(page)
  await page.goto('/insights')

  await page.getByRole('button', { name: 'End demo' }).click()

  await expect(page).toHaveURL(/\/signin\?reason=signedOut$/)
  expect(await page.evaluate(() => localStorage.getItem('rd.guestSession'))).toBeNull()
})

test('a 401 from the API ends the session with the "demo ended" message', async ({ page }) => {
  await signInAsGuest(page)
  await page.route('**/api/me', (route) =>
    route.fulfill({ status: 401, contentType: 'application/problem+json', json: { status: 401 } }),
  )
  await page.goto('/insights')

  await expect(page).toHaveURL(/\/signin\?returnTo=%2Finsights&reason=expired$/)
  await expect(page.getByText('Your demo has ended.')).toBeVisible()
})

test('the MSAL redirect bridge page is built and served as a static page', async ({ page }) => {
  const response = await page.goto('/redirect.html')

  expect(response?.status()).toBe(200)
  await expect(page).toHaveTitle(/Redirect|Microsoft Authentication/)
})

test('redirects to the current month and shows the shell', async ({ page }) => {
  await signInAsGuest(page)
  await page.goto('/')

  await expect(page).toHaveURL(/\/diary\/\d{4}\/\d{2}$/)
  await expect(page.getByRole('heading', { level: 1, name: /^Diary – / })).toBeVisible()
  await expect(page.getByRole('status', { name: 'API status' })).toHaveText('API healthy')
})

test('main navigation is reachable with the keyboard', async ({ page }) => {
  await signInAsGuest(page)
  await page.goto('/insights')

  const nav = page.getByRole('navigation', { name: 'Main' })
  const diaryLink = nav.getByRole('link', { name: 'Diary' })
  for (let i = 0; i < 10 && !(await diaryLink.evaluate((el) => el === document.activeElement)); i++) {
    await page.keyboard.press('Tab')
  }
  await expect(diaryLink).toBeFocused()

  await page.keyboard.press('Enter')
  await expect(page).toHaveURL(/\/diary\/\d{4}\/\d{2}$/)
})

test('deep link to an invalid month shows Not found', async ({ page }) => {
  await signInAsGuest(page)
  await page.goto('/diary/2026/13')

  await expect(page.getByRole('heading', { level: 1, name: 'Page not found' })).toBeVisible()
})
