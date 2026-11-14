import { expect, type Page } from '@playwright/test'
// Same test with the CSP guard (fails on Content-Security-Policy violations when the web image serves the page)
import { test } from '../e2e/support.ts'

// Full-stack race proposals journey (M8 plan §9, AC8): nothing is mocked. Guest -> the demo catalogue's proposals in
// Silesia -> details -> "I signed up" (21.1 km) -> the planned race run in the calendar, the month list and /insights ->
// delete the run -> the proposal shows again. Needs `Races__Enabled=true` and the demo catalogue (AppHost sets both).
// The guest account is isolated and expires on its own; the run is deleted again at the end.

const RACE = 'Demo Gliwice Spring Half Marathon' // seed/races/demo-races.json "demo-02": week +3, Sunday, 21.1 / 10 / 5 km

/** The race date of "demo-02": Sunday of the ISO week three weeks from now, resolved in the catalogue's zone. */
function raceDate(now = new Date()): string {
  const warsaw = new Intl.DateTimeFormat('en-CA', { timeZone: 'Europe/Warsaw' }).format(now) // YYYY-MM-DD
  const [y, m, d] = warsaw.split('-').map(Number) as [number, number, number]
  const day = new Date(Date.UTC(y, m - 1, d))
  const isoWeekday = (day.getUTCDay() + 6) % 7 // Monday = 0
  day.setUTCDate(day.getUTCDate() - isoWeekday + 21 + 6)
  return day.toISOString().slice(0, 10)
}

const cell = (page: Page, date: string) => page.locator(`[role="gridcell"][data-date="${date}"]`)

test('guest -> demo proposals -> I signed up (21.1 km) -> calendar, month list, insights -> delete -> proposal again', async ({
  page,
}) => {
  const date = raceDate()
  const [year, month] = date.split('-') as [string, string]
  const proposal = page.locator(`[role="gridcell"][data-date="${date}"] button[data-kind="proposal"]`)

  // Real POST /api/guest-sessions; the demo profile is Silesia.
  await page.goto('/')
  await expect(page).toHaveURL(/\/signin/)
  const created = page.waitForResponse(
    (r) => r.url().endsWith('/api/guest-sessions') && r.request().method() === 'POST',
  )
  await page.getByRole('button', { name: 'Try as guest' }).click()
  expect((await created).status()).toBe(201)
  await expect(page).toHaveURL(/\/diary\/\d{4}\/\d{2}$/)
  await expect(page.getByRole('grid', { name: /^Calendar for / })).toBeVisible()

  // Proposals are there (the demo catalogue seeds >= 10 future Silesian races) and the legend explains them.
  await page.goto(`/diary/${year}/${month}`)
  await expect(page.getByRole('grid', { name: /^Calendar for / })).toBeVisible()
  await expect(page.locator('[role="gridcell"] button[data-kind="proposal"]').first()).toBeVisible({ timeout: 20_000 })
  await expect(page.locator('[data-legend="proposal"]')).toBeVisible()
  await expect(proposal).toContainText(RACE)

  // Details: facts, a safe signup link, the three distances.
  await proposal.click()
  const details = page.getByRole('dialog', { name: RACE })
  await expect(details).toBeVisible()
  await expect(details.getByText('Gliwice', { exact: false }).first()).toBeVisible()
  await expect(details.getByText('5 / 10 / 21.1 km')).toBeVisible()
  const signup = details.getByRole('link', { name: /Open signup page/ })
  await expect(signup).toHaveAttribute('target', '_blank')
  expect(await signup.getAttribute('rel')).toMatch(/noopener.*noreferrer|noreferrer.*noopener/)
  expect(await signup.getAttribute('href')).toMatch(/^https:\/\//)

  // I signed up, 21.1 km: the run dialog opens.
  await details.getByRole('button', { name: 'I signed up' }).click()
  const form = details.getByRole('form', { name: 'I signed up' })
  await form.getByRole('radio', { name: '21.1 km' }).check()
  const signedUp = page.waitForResponse(
    (r) => /\/api\/races\/[^/]+\/signup$/.test(r.url()) && r.request().method() === 'POST',
  )
  await form.getByRole('button', { name: 'Add to my diary' }).click()
  expect((await signedUp).status()).toBe(201)
  await expect(page).toHaveURL(/\/diary\/\d{4}\/\d{2}\/runs\/[0-9a-f-]+$/)
  const run = page.locator('[data-panel]').filter({ has: page.getByText('Planned', { exact: true }) })
  await expect(run.first()).toBeVisible()
  await expect(run.first().getByText('21.1 km', { exact: true }).first()).toBeVisible()
  await page.keyboard.press('Escape')

  // The planned race run replaces the proposal bubble in the grid ...
  await expect(cell(page, date).locator('[data-run-id][data-status="Planned"]').first()).toBeAttached()
  await expect(proposal).toHaveCount(0)

  // ... and is in the month list.
  await page.getByRole('link', { name: 'Month list' }).click()
  const list = page.getByRole('dialog', { name: /^Runs in / })
  const item = list.locator('li[data-status="Planned"]').filter({ hasText: '21.1 km' })
  await expect(item).toHaveCount(1)
  await page.keyboard.press('Escape')
  await expect(list).toHaveCount(0)

  // /insights lists it as a planned race (a period that contains the race day; client-side navigation keeps the cache).
  const today = new Intl.DateTimeFormat('en-CA', { timeZone: 'Europe/Warsaw' }).format(new Date())
  await page.goto(`/insights?view=custom&from=${today}&to=${date}`)
  await expect(page.getByRole('heading', { level: 1, name: 'Insights' })).toBeVisible()
  const races = page.getByRole('region', { name: 'Race results table' })
  const row = races.getByRole('row').filter({ hasText: RACE })
  await expect(row).toBeVisible({ timeout: 20_000 })
  await expect(row).toContainText('Planned')

  // Delete the run from its day panel: the proposal is back.
  await page.goto(`/diary/${year}/${month}/day/${date}`)
  const day = page.getByRole('dialog', { name: /^\w+, \w+ \d+, \d{4}$/ })
  await expect(day).toBeVisible()
  // The demo diary already holds another planned run that day (10 km): pick ours by its 21.1 km.
  const mine = day.getByRole('listitem').filter({ hasText: '21.1 km' })
  await expect(mine).toHaveCount(1)
  await mine.getByRole('button', { name: 'Delete' }).click()
  await page.getByRole('alertdialog').getByRole('button', { name: 'Delete', exact: true }).click()
  await expect(mine).toHaveCount(0)
  await page.keyboard.press('Escape')
  await expect(proposal).toBeVisible({ timeout: 20_000 })
  await expect(cell(page, date).locator('button[data-run-id]')).toHaveCount(1) // the seeded run stays, ours is gone
})
