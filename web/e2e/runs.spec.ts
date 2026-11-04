import { AxeBuilder } from '@axe-core/playwright'
import { expect, type Page } from '@playwright/test'
import { test } from './support.ts'
import { createFakeApi } from '../src/test/fakeApi/index.ts'

// Mocked journeys against the production build: the in-memory fake API answers every /api call (ADR 0019).
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
  // includeHidden: a deep-linked panel is modal and marks the page behind it aria-hidden.
  await expect(page.getByRole('grid', { name: /^Calendar for /, includeHidden: true })).toBeVisible()
}

const cell = (page: Page, date: string) => page.locator(`[role="gridcell"][data-date="${date}"]`)

test('add -> mark as completed -> rate -> delete, with focus back on the day', async ({ page }) => {
  await openDiary(page, '/diary/2026/11')

  // Add a planned 10 km run on an empty day.
  await cell(page, '2026-11-10').click()
  const day = page.getByRole('dialog', { name: 'Tuesday, November 10, 2026' })
  await expect(day.getByText('No runs on this day.')).toBeVisible()
  await day.getByRole('link', { name: 'Add run on this day' }).click()
  const form = page.getByRole('dialog', { name: 'Add run' })
  await expect(form.getByLabel('Date')).toHaveValue('2026-11-10')
  await form.getByLabel('Distance (km)').fill('10')
  await form.getByRole('button', { name: 'Save', exact: true }).click()
  const item = day.getByRole('listitem')
  await expect(item).toHaveAttribute('data-status', 'Planned')
  await expect(cell(page, '2026-11-10').locator('[data-status="Planned"]').first()).toBeAttached()

  // Mark as completed: the cursor lands on the empty duration.
  await item.getByRole('link', { name: 'Mark as completed' }).click()
  const complete = page.getByRole('dialog', { name: 'Complete run' })
  await expect(complete.getByLabel('Duration (h:mm:ss)')).toBeFocused()
  await complete.getByLabel('Duration (h:mm:ss)').fill('50:00')
  await expect(complete.getByTestId('pace')).toHaveText('5:00 /km')
  await complete.getByRole('button', { name: 'Save', exact: true }).click()
  await expect(item).toHaveAttribute('data-status', 'Completed')
  await expect(cell(page, '2026-11-10').locator('[data-status="Completed"]').first()).toBeAttached()

  // Rate it.
  await item.getByRole('link', { name: 'Rate' }).click()
  const rate = page.getByRole('dialog', { name: 'Rate this run' })
  await rate.locator('label', { hasText: '4 stars' }).click()
  await rate.getByLabel('Comment').fill('Smooth')
  await rate.getByRole('button', { name: 'Save', exact: true }).click()
  await expect(item.getByRole('link', { name: 'Rate' })).toHaveCount(0)
  await item.getByRole('link', { name: 'View details' }).click()
  const details = page.getByRole('dialog', { name: 'Easy run' })
  await expect(details.getByText('4 out of 5 stars')).toBeVisible()
  await expect(details.getByText('Smooth')).toBeVisible()
  await page.keyboard.press('Escape')

  // Delete with confirmation.
  await item.getByRole('button', { name: 'Delete' }).click()
  await page.getByRole('alertdialog', { name: 'Delete this run?' }).getByRole('button', { name: 'Delete' }).click()
  await expect(day.getByText('No runs on this day.')).toBeVisible()

  // Closing the panel returns the focus to the day cell.
  await page.keyboard.press('Escape')
  await expect(page.getByRole('dialog')).toHaveCount(0)
  await expect(cell(page, '2026-11-10')).toBeFocused()
})

test('Back closes a panel and the URL follows', async ({ page }) => {
  await openDiary(page, '/diary/2026/11')
  // Enter rather than a click: the middle of the cell is the race bubble, which opens the run itself.
  await cell(page, '2026-11-14').focus()
  await page.keyboard.press('Enter')
  await expect(page).toHaveURL(/\/day\/2026-11-14$/)
  await expect(page.getByRole('dialog')).toBeVisible()

  await page.goBack()

  await expect(page).toHaveURL(/\/diary\/2026\/11$/)
  await expect(page.getByRole('dialog')).toHaveCount(0)
})

test('the day panel and the run form have no serious or critical accessibility violations', async ({ page }) => {
  await openDiary(page, '/diary/2026/11/day/2026-11-14')
  await expect(page.getByRole('dialog', { name: 'Saturday, November 14, 2026' }).getByRole('listitem')).toBeVisible()
  let { violations } = await new AxeBuilder({ page }).analyze()
  expect(violations.filter((v) => v.impact === 'serious' || v.impact === 'critical')).toEqual([])

  await page.getByRole('link', { name: 'Edit' }).click()
  const form = page.getByRole('dialog', { name: 'Edit run' })
  await expect(form.getByLabel('Title')).toHaveValue('Silesia Half')
  ;({ violations } = await new AxeBuilder({ page }).analyze())
  expect(violations.filter((v) => v.impact === 'serious' || v.impact === 'critical')).toEqual([])
})

test.describe('phone', () => {
  test.use({ viewport: { width: 360, height: 640 } })

  test('panels are bottom sheets and the form fits without horizontal scrolling', async ({ page }) => {
    await openDiary(page, '/diary/2026/11/day/2026-11-14')
    const sheet = page.getByRole('dialog', { name: 'Saturday, November 14, 2026' })
    await expect(sheet).toHaveAttribute('data-variant', 'sheet')
    const box = (await sheet.boundingBox())!
    expect(box.y + box.height).toBeGreaterThanOrEqual(639)
    expect(box.width).toBeGreaterThanOrEqual(359)

    await page.getByRole('link', { name: 'Edit' }).click()
    const form = page.getByRole('dialog', { name: 'Edit run' })
    await expect(form.getByLabel('Title')).toBeVisible()
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)
    expect(overflow).toBeLessThanOrEqual(0)
    const formOverflow = await form.evaluate((el) => el.scrollWidth - el.clientWidth)
    expect(formOverflow).toBeLessThanOrEqual(0)
  })
})
