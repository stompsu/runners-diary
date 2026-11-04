import { expect } from '@playwright/test'
import { DEMO_REPORT_TODAY } from '../src/test/fakeApi/index.ts'
import { horizontalOverflow, mockApi, restoreGuest, seriousViolations, test } from './support.ts'

// Runs only in the `phone-la` project: 360x640, touch, browser zone America/Los_Angeles (M5 AC12).

test('the dashboard is one column that never scrolls sideways, with usable touch targets', async ({ page }) => {
  const fake = await mockApi(page, { today: DEMO_REPORT_TODAY })
  await restoreGuest(page, fake)
  await page.goto('/insights')
  await expect(page.getByTestId('kpi-grid')).toBeVisible()
  await expect(page.getByRole('figure', { name: 'Easy-run pace' })).toBeVisible()

  expect(await horizontalOverflow(page)).toBeLessThanOrEqual(0)
  for (const name of ['This week', 'This month', 'Last 12 weeks', 'Year to date', 'Custom']) {
    const box = await page.getByRole('button', { name, exact: true }).boundingBox()
    expect(box?.height, name).toBeGreaterThanOrEqual(43.5)
  }
  // Charts keep a readable height.
  const chart = await page.getByRole('figure', { name: 'Volume by week' }).locator('.recharts-wrapper').boundingBox()
  expect(chart?.height).toBeGreaterThanOrEqual(180)

  // A wide table scrolls inside its own labelled, focusable region.
  const races = page.getByRole('region', { name: 'Race results table' })
  await races.focus()
  await expect(races).toBeFocused()
  expect(await horizontalOverflow(page)).toBeLessThanOrEqual(0)
  expect(await seriousViolations(page)).toEqual([])
})
