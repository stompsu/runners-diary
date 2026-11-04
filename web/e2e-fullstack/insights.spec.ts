import { AxeBuilder } from '@axe-core/playwright'
import { expect, test, type Page } from '@playwright/test'

// Full-stack Insights journey (M5 step 6, plan §6): nothing is mocked. Guest -> /insights shows the real demo report ->
// a completed run added in the diary changes the next report (query invalidation, AC14) -> the run is deleted again.
// The guest account is isolated and expires on its own.

async function tryAsGuest(page: Page) {
  await page.goto('/')
  await expect(page).toHaveURL(/\/signin/)
  const created = page.waitForResponse(
    (r) => r.url().endsWith('/api/guest-sessions') && r.request().method() === 'POST',
  )
  await page.getByRole('button', { name: 'Try as guest' }).click()
  expect((await created).status()).toBe(201)
  await expect(page).toHaveURL(/\/diary\/\d{4}\/\d{2}$/)
  await expect(page.getByRole('grid', { name: /^Calendar for / })).toBeVisible()
}

/** First number in a text, thousands separators removed ("1,234 AU" -> 1234, "Up +47.7 km (+14.2%)" -> 47.7). */
function num(text: string | null): number {
  const match = /-?\d[\d,]*(?:\.\d+)?/.exec((text ?? '').replace(/ | /g, ''))
  if (!match) throw new Error(`no number in "${text}"`)
  return Number(match[0].replaceAll(',', ''))
}

async function kpi(page: Page, key: string): Promise<{ value: number; delta: number }> {
  const card = page.locator(`[data-kpi="${key}"]`)
  const value = num(await card.locator('dd').first().textContent())
  const direction = await card.locator('[data-direction]').getAttribute('data-direction')
  const deltaText = await card.locator('[data-direction]').textContent()
  const magnitude = direction === 'same' ? 0 : num(deltaText)
  return { value, delta: direction === 'down' ? -magnitude : magnitude }
}

async function seriousViolations(page: Page): Promise<string[]> {
  await page.waitForFunction(() => document.getAnimations().every((a) => a.playState !== 'running'))
  const { violations } = await new AxeBuilder({ page }).analyze()
  return violations.filter((v) => v.impact === 'serious' || v.impact === 'critical').map((v) => `${v.id}: ${v.help}`)
}

test('guest -> real demo report -> add a completed run -> Insights shows +5 km and +150 AU -> delete', async ({
  page,
}) => {
  const title = `E2E insights ${Date.now()}`
  await tryAsGuest(page)

  // Client-side navigation only: a reload would drop the query cache and hide a missing invalidation.
  await page.getByRole('link', { name: 'Insights', exact: true }).first().click()
  await expect(page).toHaveURL(/\/insights$/)
  await expect(page.getByRole('heading', { level: 1, name: 'Insights' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Last 12 weeks' })).toHaveAttribute('aria-pressed', 'true')

  // The real report of the seeded demo story.
  const grid = page.getByTestId('kpi-grid')
  await expect(grid).toContainText(/km/)
  await expect(page.getByTestId('acwr-card')).toContainText(/Optimal|Low|High|Available after/)
  const acwr = await page.getByTestId('acwr-card').textContent()
  expect(acwr).toMatch(/Optimal:|Low:|High:/) // a demo guest has well over 35 days of history
  const volume = page.getByRole('figure', { name: 'Volume by week' })
  await volume.getByText('Show data').click()
  await expect(volume.getByRole('row')).toHaveCount(13) // header + 12 weeks
  await expect(page.getByRole('table', { name: 'Race results' }).getByRole('row')).not.toHaveCount(1)
  expect(await seriousViolations(page)).toEqual([])

  const distance = await kpi(page, 'distance')
  const load = await kpi(page, 'load')
  const runs = await kpi(page, 'runs')
  expect(distance.value).toBeGreaterThan(100)
  expect(load.value).toBeGreaterThan(0)

  // Add a completed run today: 5 km in 30:00 at RPE 5 = 5 x 1800 s = 150 AU.
  await page.getByRole('link', { name: 'Diary', exact: true }).first().click()
  await expect(page.getByRole('grid', { name: /^Calendar for / })).toBeVisible()
  await page.getByRole('link', { name: 'Add run', exact: true }).click()
  const form = page.getByRole('dialog', { name: 'Add run' })
  await form.getByLabel('Title').fill(title)
  await form.getByRole('radio', { name: 'Completed' }).check()
  await form.getByLabel('Distance (km)').fill('5')
  await form.getByLabel('Duration (h:mm:ss)').fill('30:00')
  await form.getByLabel('Effort (RPE)').fill('5')
  await form.getByRole('button', { name: 'Save', exact: true }).click()
  await expect(page.getByRole('dialog')).toHaveCount(0)

  // Back to Insights (same SPA session): the cached report was invalidated and refetched.
  await page.getByRole('link', { name: 'Insights', exact: true }).first().click()
  await expect(page.getByRole('heading', { level: 1, name: 'Insights' })).toBeVisible()
  await expect(async () => {
    const after = await kpi(page, 'distance')
    expect(after.value).toBeCloseTo(distance.value + 5, 0)
  }).toPass({ timeout: 15_000 })
  const distanceAfter = await kpi(page, 'distance')
  const loadAfter = await kpi(page, 'load')
  const runsAfter = await kpi(page, 'runs')
  expect(Math.abs(distanceAfter.value - distance.value - 5)).toBeLessThanOrEqual(0.11)
  expect(Math.abs(distanceAfter.delta - distance.delta - 5)).toBeLessThanOrEqual(0.21)
  expect(Math.abs(loadAfter.value - load.value - 150)).toBeLessThanOrEqual(1)
  expect(Math.abs(loadAfter.delta - load.delta - 150)).toBeLessThanOrEqual(2)
  expect(runsAfter.value).toBe(runs.value + 1)
  expect(runsAfter.delta).toBe(runs.delta + 1)

  // Clean up: delete the run (month list) and see the totals return.
  await page.getByRole('link', { name: 'Diary', exact: true }).first().click()
  await page.getByRole('link', { name: 'Month list' }).click()
  const list = page.getByRole('dialog', { name: /^Runs in / })
  const item = list.getByRole('listitem').filter({ hasText: title })
  await item.getByRole('button', { name: 'Delete' }).click()
  const confirmDelete = page.getByRole('alertdialog', { name: 'Delete this run?' })
  await confirmDelete.getByRole('button', { name: 'Delete' }).click()
  await expect(confirmDelete).toBeHidden()
  await expect(item).toHaveCount(0)
  await page.keyboard.press('Escape')
  await expect(list).toBeHidden()

  await page.getByRole('link', { name: 'Insights', exact: true }).first().click()
  await expect(async () => {
    const restored = await kpi(page, 'distance')
    expect(Math.abs(restored.value - distance.value)).toBeLessThanOrEqual(0.11)
  }).toPass({ timeout: 15_000 })
  expect((await kpi(page, 'runs')).value).toBe(runs.value)
})

test('the real API answers a bad range with 400 and the dashboard shows the error state with Try again', async ({
  page,
}) => {
  await tryAsGuest(page)

  // The page never builds an invalid range itself (its validation mirrors the API's), so the bad query is injected on
  // the wire; the 400 below comes from the real API, not from a mock.
  let seen = 0
  await page.route('**/api/insights/report?*', async (route) => {
    const url = new URL(route.request().url())
    url.searchParams.set('from', '2026-13-01')
    seen += 1
    await route.continue({ url: url.toString() })
  })
  const bad = page.waitForResponse((r) => r.url().includes('/api/insights/report') && r.status() === 400)
  await page.getByRole('link', { name: 'Insights', exact: true }).first().click()
  const response = await bad
  expect(response.headers()['content-type']).toContain('application/problem+json')
  const problem = (await response.json()) as { errors?: Record<string, string[]> }
  expect(Object.keys(problem.errors ?? {})).toContain('from')
  expect(seen).toBeGreaterThan(0)

  const alert = page.getByRole('alert')
  await expect(alert).toContainText("Couldn't load this page")
  expect(await seriousViolations(page)).toEqual([])

  // Retry with the interception removed recovers.
  await page.unroute('**/api/insights/report?*')
  await alert.getByRole('button', { name: 'Try again' }).click()
  await expect(page.getByTestId('kpi-grid')).toBeVisible()
})

test('invalid URL parameters fall back to the default view and never reach the API as a bad request', async ({
  page,
}) => {
  await tryAsGuest(page)
  const statuses: number[] = []
  page.on('response', (r) => {
    if (r.url().includes('/api/insights/report')) statuses.push(r.status())
  })

  for (const query of [
    '?view=custom&from=2026-01-01&to=2027-06-01', // 517 days
    '?view=custom&from=2026-11-10&to=2026-11-01', // to < from
    '?view=custom&from=1900-01-01&to=1900-02-01', // before 1950
    '?view=decade',
    '?view=month&at=2026-13-45&compare=bogus',
  ]) {
    await page.goto(`/insights${query}`) // the guest session is restored from storage
    await expect(page.getByRole('heading', { level: 1, name: 'Insights' })).toBeVisible()
    await expect(page.getByTestId('kpi-grid')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Last 12 weeks' })).toHaveAttribute('aria-pressed', 'true')
  }
  expect(statuses.length).toBeGreaterThan(0)
  expect(statuses.every((s) => s === 200)).toBe(true)
})
