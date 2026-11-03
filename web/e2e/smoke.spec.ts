import { expect, test } from '@playwright/test'

test.beforeEach(async ({ page }) => {
  await page.route('**/api/health', (route) => route.fulfill({ json: { status: 'Healthy', version: 'e2e' } }))
})

test('redirects to the current month and shows the shell', async ({ page }) => {
  await page.goto('/')

  await expect(page).toHaveURL(/\/diary\/\d{4}\/\d{2}$/)
  await expect(page.getByRole('heading', { level: 1, name: /^Diary – / })).toBeVisible()
  await expect(page.getByRole('status', { name: 'API status' })).toHaveText('API healthy')
})

test('main navigation is reachable with the keyboard', async ({ page }) => {
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
  await page.goto('/diary/2026/13')

  await expect(page.getByRole('heading', { level: 1, name: 'Page not found' })).toBeVisible()
})
