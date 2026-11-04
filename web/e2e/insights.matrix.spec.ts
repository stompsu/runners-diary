import { expect, type Page } from '@playwright/test'
import { DEMO_REPORT_TODAY, type InsightsScenario } from '../src/test/fakeApi/index.ts'
import { mockApi, restoreGuest, seriousViolations, settled, test } from './support.ts'

// M5 step 6 (qa-tester): the parts of plan §6 the smoke spec leaves out - comparison modes, paging, custom range, URL
// round-trips, a fallback for broken params, and axe in every state / selection. The report is a canned fixture
// (plan assumption 6); the real numbers are covered by Domain/Api tests and the full-stack spec.

async function open(page: Page, path = '/insights', scenario: InsightsScenario = 'demo') {
  const fake = await mockApi(page, { today: DEMO_REPORT_TODAY, insights: scenario })
  await restoreGuest(page, fake)
  await page.goto(path)
  await expect(page.getByRole('heading', { level: 1, name: 'Insights' })).toBeVisible()
}

test('"Same period last year" is written to the URL, labelled with both ranges, and restored by Back', async ({
  page,
}) => {
  await open(page)

  await page.getByLabel('Compare with').selectOption({ label: 'Same period last year' })
  await expect(page).toHaveURL(/compare=previousYear/)
  await expect(page.getByTestId('comparison-label')).toContainText('Same period last year')
  await expect(page.getByTestId('kpi-grid')).toBeVisible()

  await page.goBack()
  await expect(page).not.toHaveURL(/compare=/)
  await expect(page.getByTestId('comparison-label')).toContainText('Previous period')
})

test('"No comparison" removes the comparison label and every delta', async ({ page }) => {
  await open(page)

  await page.getByLabel('Compare with').selectOption({ label: 'No comparison' })
  await expect(page).toHaveURL(/compare=none/)
  await expect(page.getByTestId('comparison-label')).toHaveCount(0)
  await expect(page.getByTestId('kpi-grid').locator('[data-direction]')).toHaveCount(0)
})

test('earlier and later page through periods and the URL keeps the anchor', async ({ page }) => {
  await open(page, '/insights?view=month&at=2026-11-04')

  await page.getByRole('button', { name: 'Show earlier period' }).click()
  await expect(page).toHaveURL(/view=month&at=2026-10/)
  await expect(page.getByTestId('period-label')).toContainText('Oct 1')

  await page.getByRole('button', { name: 'Show later period' }).click()
  await expect(page.getByTestId('period-label')).toContainText('Nov 1')

  await page.getByRole('button', { name: 'Year to date' }).click()
  await expect(page.getByRole('button', { name: 'Show earlier period' })).toHaveCount(0)
})

test('a custom range is validated in the form and applied to the URL', async ({ page }) => {
  await open(page)

  await page.getByRole('button', { name: 'Custom', exact: true }).click()
  await page.getByRole('textbox', { name: 'From', exact: true }).fill('2026-01-01')
  await page.getByRole('textbox', { name: 'To', exact: true }).fill('2027-06-01')
  await page.getByRole('button', { name: 'Show period' }).click()
  await expect(page.getByText('A period can be at most 366 days.')).toBeVisible()

  await page.getByRole('textbox', { name: 'To', exact: true }).fill('2026-03-31')
  await page.getByRole('button', { name: 'Show period' }).click()
  await expect(page).toHaveURL(/view=custom&from=2026-01-01&to=2026-03-31/)
})

test('a shared URL restores the exact view, and broken parameters fall back to the default', async ({ page }) => {
  await open(page, '/insights?view=week&at=2026-12-30&compare=none')
  await expect(page.getByRole('button', { name: 'This week' })).toHaveAttribute('aria-pressed', 'true')
  await expect(page.getByTestId('period-label')).toContainText('Dec 28')

  await page.goto('/insights?view=custom&from=2026-01-01&to=2027-06-01')
  await expect(page).toHaveURL(/\/insights$/)
  await expect(page.getByRole('button', { name: 'Last 12 weeks' })).toHaveAttribute('aria-pressed', 'true')
})

test('has no serious accessibility violations with the comparison off, a custom form open and a phone-sized month', async ({
  page,
}) => {
  await open(page, '/insights?view=month&at=2026-10-10&compare=previousYear')
  await expect(
    page.getByRole('figure', { name: 'Volume by month' }).or(page.getByRole('figure', { name: 'Volume by week' })),
  ).toBeVisible()
  expect(await seriousViolations(page)).toEqual([])

  await page.getByRole('button', { name: 'Custom', exact: true }).click()
  await expect(page.getByRole('textbox', { name: 'From', exact: true })).toBeVisible()
  await settled(page) // the form fades in; a half-transparent label fails the contrast check
  expect(await seriousViolations(page)).toEqual([])
})

test('the chart data tables are reachable from the keyboard', async ({ page }) => {
  await open(page)

  const figure = page.getByRole('figure', { name: 'Time by run type' })
  const summary = figure.getByText('Show data')
  await summary.focus()
  await page.keyboard.press('Enter')
  await expect(figure.getByRole('table')).toBeVisible()
  // The visual chart is hidden from assistive tech and holds nothing that takes keyboard focus.
  await expect(figure.locator('div[aria-hidden="true"]').first()).toBeAttached()
  await expect(
    figure.locator('[aria-hidden="true"] :is(a, button, input, select, [tabindex]:not([tabindex="-1"]))'),
  ).toHaveCount(0)
})
