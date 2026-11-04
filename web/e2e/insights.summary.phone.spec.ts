import { expect, test } from '@playwright/test'
import type { SummaryPostOutcome, SummaryScenario } from '../src/test/fakeApi/index.ts'
import { horizontalOverflow, openSummary, seriousViolations } from './support.ts'

// Phone (360x640, Los Angeles zone): the summary card is one column, does not scroll sideways, its buttons are large
// enough to tap and it passes axe in the main states. Desktop coverage: insights.summary.spec.ts.

for (const [name, scenario, post] of [
  ['demo example', 'demo', undefined],
  ['generated summary', 'none', 'ok'],
  ['stale summary', 'stale', undefined],
  ['quota exhausted', 'quotaExhausted', undefined],
  ['generation error', 'none', 'timeout'],
] as [string, SummaryScenario, SummaryPostOutcome | undefined][]) {
  test(`${name}: fits the phone, big tap targets, no serious a11y violations`, async ({ page }) => {
    const { card } = await openSummary(page, scenario, { post })
    if (post) {
      await card.getByRole('button', { name: 'Generate summary' }).click()
      await expect(card.getByRole(post === 'ok' ? 'heading' : 'alert').first()).toBeVisible()
    }
    await card.scrollIntoViewIfNeeded()

    expect(await horizontalOverflow(page)).toBeLessThanOrEqual(0)
    for (const button of await card.getByRole('button').all()) {
      expect((await button.boundingBox())?.height).toBeGreaterThanOrEqual(43.5)
    }
    expect(await seriousViolations(page)).toEqual([])
  })
}

test('Cancel is a large tap target while the model works', async ({ page }) => {
  const { card } = await openSummary(page, 'none', { post: 'slow', slowMs: 30_000 })
  await card.getByRole('button', { name: 'Generate summary' }).click()

  const cancel = card.getByRole('button', { name: 'Cancel' })
  await expect(cancel).toBeVisible()
  expect((await cancel.boundingBox())?.height).toBeGreaterThanOrEqual(43.5)
})
