import { expect } from '@playwright/test'
import { DEMO_REPORT_TODAY, type InsightsScenario } from '../src/test/fakeApi/index.ts'
import { mockApi, restoreGuest, seriousViolations, test } from './support.ts'

// Smoke coverage for the Insights dashboard (M5 steps 4-5). The full matrix (compare modes, empty month, axe on every
// state, phone layout) is the qa-tester's step 6; the report itself is a canned fixture of the Domain golden snapshot.

async function openInsights(
  page: import('@playwright/test').Page,
  path = '/insights',
  scenario: InsightsScenario = 'demo',
) {
  const fake = await mockApi(page, { today: DEMO_REPORT_TODAY, insights: scenario })
  await restoreGuest(page, fake)
  await page.goto(path)
  await expect(page.getByRole('heading', { level: 1, name: 'Insights' })).toBeVisible()
  return fake
}

test('shows the 12-week dashboard: KPIs with worded changes, ACWR in words, five charts and the race table', async ({
  page,
}) => {
  await openInsights(page)

  await expect(page.getByTestId('kpi-grid')).toContainText('382.6 km')
  await expect(page.getByTestId('kpi-grid')).toContainText('Up +47.7 km (+14.2%)')
  await expect(page.getByTestId('partial-note')).toContainText('80 of 84 days')
  await expect(page.getByTestId('acwr-card')).toContainText('0.80')
  await expect(page.getByTestId('acwr-card')).toContainText(
    'Optimal: your last 7 days were in line with your recent norm.',
  )
  for (const name of [
    'Volume by week',
    'Acute and chronic load',
    'Load change over time',
    'Time by run type',
    'Easy-run pace',
  ]) {
    await expect(page.getByRole('figure', { name })).toBeVisible()
  }
  await expect(page.getByRole('table', { name: 'Race results' })).toContainText('Labedy Half Marathon')
  await expect(page.getByRole('table', { name: 'Race results' })).toContainText('Personal best')
})

test('the period lives in the URL: This month, Back and the data table of a chart', async ({ page }) => {
  await openInsights(page)

  await page.getByRole('button', { name: 'This month' }).click()
  await expect(page).toHaveURL(/\/insights\?view=month&at=2026-11-04$/)
  await expect(page.getByTestId('period-label')).toContainText('Nov 1')

  await page.goBack()
  await expect(page).toHaveURL(/\/insights$/)
  await expect(page.getByRole('button', { name: 'Last 12 weeks' })).toHaveAttribute('aria-pressed', 'true')

  const figure = page.getByRole('figure', { name: 'Volume by week' })
  await figure.getByText('Show data').click()
  await expect(figure.getByRole('row')).toHaveCount(13) // header + 12 weeks
})

test('an invalid URL falls back to the default view', async ({ page }) => {
  await openInsights(page, '/insights?view=decade')

  await expect(page).toHaveURL(/\/insights$/)
  await expect(page.getByRole('button', { name: 'Last 12 weeks' })).toHaveAttribute('aria-pressed', 'true')
})

test('has no serious accessibility violations with data, with no runs and with insufficient history', async ({
  page,
}) => {
  await openInsights(page)
  await expect(page.getByRole('figure', { name: 'Easy-run pace' })).toBeVisible()
  expect(await seriousViolations(page)).toEqual([])

  await page.goto('/insights?view=month&at=2025-03-10')
  await expect(page.getByTestId('insights-empty')).toBeVisible()
  expect(await seriousViolations(page)).toEqual([])
})

test('explains insufficient history in the ACWR card', async ({ page }) => {
  await openInsights(page, '/insights', 'insufficientHistory')

  await expect(page.getByTestId('acwr-card')).toContainText('Available after 35 days of logged training; 13 so far.')
  expect(await seriousViolations(page)).toEqual([])
})
