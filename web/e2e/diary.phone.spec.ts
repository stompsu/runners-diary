import { expect, test } from '@playwright/test'
import { cell, horizontalOverflow, mockApi, openDiary, recordWrites, restoreGuest, TODAY } from './support.ts'

// Runs only in the `phone-la` project (playwright.config.ts): 360x640, touch, browser zone America/Los_Angeles while
// the (fake) account's zone is Europe/Warsaw. AC12 (dates never shift) and AC14 (usable at 360 px).

test('the browser really runs in Los Angeles at 360x640', async ({ page }) => {
  await mockApi(page)
  await page.goto('/signin')
  expect(await page.evaluate(() => Intl.DateTimeFormat().resolvedOptions().timeZone)).toBe('America/Los_Angeles')
  expect(page.viewportSize()).toEqual({ width: 360, height: 640 })
})

test('runs stay on their dates and nothing scrolls sideways (calendar, list, day sheet, form)', async ({ page }) => {
  const fake = await mockApi(page)
  await restoreGuest(page, fake)
  await openDiary(page, '/diary/2026/11')

  await expect(page.getByRole('heading', { level: 1, name: 'Diary – November 2026' })).toBeVisible()
  await expect(cell(page, TODAY)).toHaveAttribute('aria-current', 'date')
  // Seeded dates (day 2, 14, 21...) hold their runs; the neighbours are empty.
  await expect(cell(page, '2026-11-14').locator('[data-run-id]').first()).toBeAttached()
  await expect(cell(page, '2026-11-13').locator('[data-run-id]')).toHaveCount(0)
  await expect(cell(page, '2026-11-15').locator('[data-run-id]')).toHaveCount(0)
  await expect(cell(page, '2026-11-21').locator('[data-run-id]').first()).toBeAttached()
  expect(await horizontalOverflow(page)).toBeLessThanOrEqual(0)

  // Touch targets of the header controls (44 px minimum on phones).
  for (const name of ['Previous month', 'Next month', 'Today', 'Add run', 'Month list']) {
    const box = await page
      .getByRole(name === 'Add run' || name === 'Month list' ? 'link' : name === 'Today' ? 'button' : 'link', {
        name,
        exact: true,
      })
      .boundingBox()
    expect(box?.height, name).toBeGreaterThanOrEqual(43.5)
  }

  await page.getByRole('link', { name: 'Month list' }).click()
  const list = page.getByRole('dialog', { name: 'Runs in November 2026' })
  await expect(list).toHaveAttribute('data-variant', 'sheet')
  await expect(list.getByRole('listitem').first()).toBeVisible()
  expect(await horizontalOverflow(page)).toBeLessThanOrEqual(0)
  expect(await list.evaluate((el) => el.scrollWidth - el.clientWidth)).toBeLessThanOrEqual(0)
  await page.keyboard.press('Escape')

  await page.goto('/diary/2026/11/day/2026-11-14')
  const day = page.getByRole('dialog', { name: 'Saturday, November 14, 2026' })
  await expect(day).toHaveAttribute('data-variant', 'sheet')
  await day.getByRole('button', { name: 'Delete' }).click()
  const confirm = page.getByRole('alertdialog', { name: 'Delete this run?' })
  const box = (await confirm.boundingBox())!
  expect(box.x).toBeGreaterThanOrEqual(0)
  expect(box.x + box.width).toBeLessThanOrEqual(360.5)
  expect(await horizontalOverflow(page)).toBeLessThanOrEqual(0)
})

test('a 23:30 run is stored on the chosen date in the profile zone, not the browser zone', async ({ page }) => {
  const fake = await mockApi(page)
  const token = await restoreGuest(page, fake)
  await openDiary(page, '/diary/2026/11/runs/new?date=2026-11-10')
  const runsOn = (date: string) => cell(page, date).locator('[data-run-id]')
  const before = { nine: await runsOn('2026-11-09').count(), eleven: await runsOn('2026-11-11').count() }
  expect(await runsOn('2026-11-10').count()).toBe(0)
  const writes = recordWrites(page)

  const form = page.getByRole('dialog', { name: 'Add run' })
  await expect(form.getByLabel('Date')).toHaveValue('2026-11-10')
  await expect(form.getByTestId('zone-current')).toHaveText('Europe/Warsaw')
  await form.getByLabel('Start time (optional)').fill('23:30')
  await form.getByLabel('Distance (km)').fill('6')
  await form.getByRole('button', { name: 'Save', exact: true }).click()
  await expect(page.getByRole('dialog')).toHaveCount(0)

  const post = writes.find((w) => w.method === 'POST')!
  expect(post.body?.date).toBe('2026-11-10')
  expect(post.body?.startTime).toBe('23:30')
  expect(post.body?.timeZoneId ?? 'Europe/Warsaw').toBe('Europe/Warsaw')
  const stored = fake.runsOf(token).find((r) => r.date === '2026-11-10')!
  expect(stored.startTime).toMatch(/^23:30/)
  expect(stored.timeZoneId).toBe('Europe/Warsaw')
  await expect(runsOn('2026-11-10').first()).toBeAttached()
  // The neighbouring days (seeded cross-training and rest day) gained nothing.
  expect(await runsOn('2026-11-09').count()).toBe(before.nine)
  expect(await runsOn('2026-11-11').count()).toBe(before.eleven)
})

test('"today" comes from the server profile, not from the browser clock (browser: 23:30 on 3 Nov in Los Angeles)', async ({
  page,
}) => {
  // 2026-11-04T07:30Z = 23:30 on 3 Nov in Los Angeles = 08:30 on 4 Nov in Warsaw (the account's zone).
  await page.clock.setFixedTime(new Date('2026-11-04T07:30:00Z'))
  const fake = await mockApi(page, { today: '2026-11-04' })
  await restoreGuest(page, fake)

  await page.goto('/')
  await expect(page).toHaveURL(/\/diary\/2026\/11$/)
  await expect(cell(page, '2026-11-04')).toHaveAttribute('aria-current', 'date')
  await expect(cell(page, '2026-11-03')).not.toHaveAttribute('aria-current', 'date')

  await page.getByRole('link', { name: 'Add run', exact: true }).click()
  await expect(page.getByRole('dialog', { name: 'Add run' }).getByLabel('Date')).toHaveValue('2026-11-04')
})

test('"/" opens the month of the server "today" across a month boundary (browser still on 30 Nov)', async ({
  page,
}) => {
  // 2026-12-01T07:30Z = 23:30 on 30 Nov in Los Angeles; the account (Warsaw) is already on 1 Dec.
  await page.clock.setFixedTime(new Date('2026-12-01T07:30:00Z'))
  const fake = await mockApi(page, { today: '2026-12-01' })
  await restoreGuest(page, fake)

  await page.goto('/')

  await expect(page).toHaveURL(/\/diary\/2026\/12$/)
  await expect(page.getByRole('heading', { level: 1, name: 'Diary – December 2026' })).toBeVisible()
  await expect(cell(page, '2026-12-01')).toHaveAttribute('aria-current', 'date')
})

test('the sign-in page fits 360 px', async ({ page }) => {
  await mockApi(page)
  await page.goto('/signin')
  await expect(page.getByRole('button', { name: 'Try as guest' })).toBeVisible()
  expect(await horizontalOverflow(page)).toBeLessThanOrEqual(0)
})
