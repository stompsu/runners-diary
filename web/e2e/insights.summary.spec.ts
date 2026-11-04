import { expect, test } from '@playwright/test'
import type { SummaryPostOutcome, SummaryScenario } from '../src/test/fakeApi/index.ts'
import { openSummary, seriousViolations } from './support.ts'

// Smoke coverage for the AI summary card (M6 step 4): the states of the plan's UI table, the explicit-click flow, Cancel
// and axe. The fake API answers (ADR 0019); QA extends the matrix. Phone: insights.summary.phone.spec.ts.

test('the demo example is labelled, formatted by the unit helpers and sits below the KPI grid', async ({ page }) => {
  const { card } = await openSummary(page, 'demo')

  await expect(card.getByTestId('summary-badge')).toHaveText('Example summary for the demo data')
  await expect(card.getByRole('heading', { level: 3 })).toContainText('personal best at the half marathon')
  await expect(card).toContainText('You covered 382.6 km in 39 runs, +14.2% on the previous period.')
  await expect(card).not.toContainText(/[{}]/)
  const kpi = await page.getByTestId('kpi-grid').boundingBox()
  const box = await card.boundingBox()
  expect(box!.y).toBeGreaterThan(kpi!.y + kpi!.height - 1)
})

test('Generate by keyboard: progress, a polite result message, focus on the new headline', async ({ page }) => {
  const { card } = await openSummary(page, 'none')

  const generate = card.getByRole('button', { name: 'Generate summary' })
  await generate.focus()
  await page.keyboard.press('Enter')

  await expect(card.getByTestId('summary-badge')).toHaveText('AI-generated')
  await expect(card.getByTestId('summary-status')).toHaveText('The summary is ready.')
  await expect(card.getByRole('heading', { level: 3 })).toBeFocused()
  await expect(card.getByTestId('summary-remaining')).toHaveText('2 of 3 left')
  expect(await seriousViolations(page)).toEqual([])
})

test('Cancel aborts a slow generation and puts focus back on the button', async ({ page }) => {
  const { card } = await openSummary(page, 'none', { post: 'slow', slowMs: 30_000 })

  await card.getByRole('button', { name: 'Generate summary' }).click()
  const cancel = card.getByRole('button', { name: 'Cancel' })
  await expect(cancel).toBeFocused()
  await expect(card.getByTestId('summary-status')).toHaveText('Generating the summary…')
  await cancel.click()

  await expect(card.getByRole('button', { name: 'Generate summary' })).toBeFocused()
  await expect(card.getByTestId('summary-status')).toHaveText('Generation cancelled.')
  await expect(card.getByRole('alert')).toHaveCount(0)
})

for (const [name, scenario, post, expected] of [
  ['demo example', 'demo', undefined, 'Example summary for the demo data'],
  ['stale summary', 'stale', undefined, 'Newer data is available.'],
  ['automatic note', 'template', undefined, 'Automatic note'],
  ['quota exhausted', 'quotaExhausted', undefined, 'Demo limit reached'],
  ['not configured', 'notConfigured', undefined, "Generating your own summary isn't available here."],
  ['generation error', 'none', 'busy', 'The AI service is busy.'],
] as [string, SummaryScenario, SummaryPostOutcome | undefined, string][]) {
  test(`${name}: states its situation in words and has no serious a11y violations`, async ({ page }) => {
    const { card } = await openSummary(page, scenario, { post })
    if (post) await card.getByRole('button', { name: 'Generate summary' }).click()

    await expect(card).toContainText(expected)
    expect(await seriousViolations(page)).toEqual([])
  })
}
