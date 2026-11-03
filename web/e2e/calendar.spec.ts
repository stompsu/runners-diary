import { AxeBuilder } from '@axe-core/playwright'
import { expect, test, type Page } from '@playwright/test'
import { createFakeApi } from '../src/test/fakeApi/index.ts'

// The in-memory fake API (src/test/fakeApi, ADR 0019) answers every /api call; today is 2026-11-03.
async function openDiary(page: Page, path: string) {
  const fake = createFakeApi({ today: '2026-11-03' })
  const accessToken = fake.issueToken('Guest')
  await page.route('**/api/**', (route) => fake.fulfillRoute(route))
  await page.addInitScript((session) => localStorage.setItem('rd.guestSession', JSON.stringify(session)), {
    accessToken,
    expiresAt: Date.now() + 24 * 60 * 60 * 1000,
    userId: 'e2e',
  })
  await page.goto(path)
  await expect(page.getByRole('grid', { name: /^Calendar for / })).toBeVisible()
}

const cell = (page: Page, date: string) => page.locator(`[data-date="${date}"]`)

test('the calendar grid is keyboard-operable and the URL follows', async ({ page }) => {
  await openDiary(page, '/diary/2026/11')
  await expect(page.getByRole('grid')).toHaveAccessibleName('Calendar for November 2026')
  await expect(page.getByRole('rowheader')).toHaveCount(6)
  await expect(cell(page, '2026-11-14').locator('[data-family="race"] svg')).toBeVisible()

  await cell(page, '2026-11-03').focus()
  await page.keyboard.press('ArrowDown')
  await expect(cell(page, '2026-11-10')).toBeFocused()

  await page.keyboard.press('Enter')
  await expect(page).toHaveURL(/\/diary\/2026\/11\/day\/2026-11-10$/)

  await page.goBack()
  await expect(page).toHaveURL(/\/diary\/2026\/11$/)

  await cell(page, '2026-11-30').focus()
  await page.keyboard.press('PageDown')
  await expect(page).toHaveURL(/\/diary\/2026\/12$/)
  await expect(cell(page, '2026-12-30')).toBeFocused()

  await page.goBack()
  await expect(page.getByRole('heading', { level: 1, name: 'Diary – November 2026' })).toBeVisible()
})

test('the calendar has no serious or critical accessibility violations', async ({ page }) => {
  await openDiary(page, '/diary/2026/11')

  const { violations } = await new AxeBuilder({ page }).analyze()

  expect(violations.filter((v) => v.impact === 'serious' || v.impact === 'critical')).toEqual([])
})

test.describe('phone', () => {
  test.use({ viewport: { width: 360, height: 640 } })

  test('fits 360 px without horizontal scrolling and shows compact bubbles', async ({ page }) => {
    await openDiary(page, '/diary/2026/11')

    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)
    expect(overflow).toBeLessThanOrEqual(0)
    // Desktop bubbles are hidden, the compact dots with the day total are shown.
    await expect(cell(page, '2026-11-06').locator('button[data-run-id]')).toBeHidden()
    await expect(cell(page, '2026-11-06').locator('span[data-run-id]')).toBeVisible()
    // The week column keeps the distance and hides time/elevation.
    const week = page.getByRole('rowheader', { name: /Week 45/ })
    await expect(week).toContainText('44 km')
    // Visually hidden (1 px box) but still in the accessibility tree.
    const box = await week.getByText('4:10:00').boundingBox()
    expect(box?.width).toBeLessThanOrEqual(1)
  })
})
