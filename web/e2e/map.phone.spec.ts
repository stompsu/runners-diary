import { expect } from '@playwright/test'
import { seriousViolations, stubMapNetwork, test } from './support.ts'

// 360x640 touch viewport (project phone-la): the track viewer on its test-only page. Plan AC15 for the viewer part.
test('the track viewer fits a phone: map at least 240 px tall, profile visible, splits collapsed, no sideways scroll', async ({
  page,
}) => {
  await stubMapNetwork(page)
  await page.goto('http://localhost:4174/e2e/harness/map.html')
  const map = page.getByRole('region', { name: 'Map of the route' })
  await expect(map).toHaveAttribute('data-map-state', 'ready')

  expect((await map.boundingBox())!.height).toBeGreaterThanOrEqual(240)
  await expect(page.getByRole('slider', { name: /^Elevation profile/ })).toBeVisible()
  await expect(page.getByText('Show splits table').locator('xpath=ancestor::details')).not.toHaveAttribute('open', '')
  const overflow = await page.evaluate(
    () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
  )
  expect(overflow).toBeLessThanOrEqual(0)
  expect(await seriousViolations(page)).toEqual([])
})
