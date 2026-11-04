import { expect } from '@playwright/test'
import { DEMO_REPORT_TODAY, type InsightsScenario } from '../src/test/fakeApi/index.ts'
import { horizontalOverflow, mockApi, restoreGuest, seriousViolations, test } from './support.ts'

// Phone (360x640, Los Angeles zone, AC12/AC10): the empty and insufficient-history states and the custom form must not
// scroll sideways and must pass axe, like the populated dashboard in insights.phone.spec.ts.

for (const [name, path, scenario] of [
  ['empty period', '/insights?view=month&at=2025-03-10', 'demo'],
  ['insufficient history', '/insights', 'insufficientHistory'],
  ['year to date', '/insights?view=ytd', 'demo'],
] as [string, string, InsightsScenario][]) {
  test(`${name}: one column, no sideways scroll, no serious a11y violations`, async ({ page }) => {
    const fake = await mockApi(page, { today: DEMO_REPORT_TODAY, insights: scenario })
    await restoreGuest(page, fake)
    await page.goto(path)
    await expect(page.getByRole('heading', { level: 1, name: 'Insights' })).toBeVisible()
    await expect(page.getByTestId('period-label')).toBeVisible()

    expect(await horizontalOverflow(page)).toBeLessThanOrEqual(0)
    expect(await seriousViolations(page)).toEqual([])
  })
}

test('the custom range form fits the phone and stays touch friendly', async ({ page }) => {
  const fake = await mockApi(page, { today: DEMO_REPORT_TODAY })
  await restoreGuest(page, fake)
  await page.goto('/insights')
  await page.getByRole('button', { name: 'Custom', exact: true }).click()

  expect(await horizontalOverflow(page)).toBeLessThanOrEqual(0)
  const apply = await page.getByRole('button', { name: 'Show period' }).boundingBox()
  expect(apply?.height).toBeGreaterThanOrEqual(43.5)
  expect(await seriousViolations(page)).toEqual([])
})
