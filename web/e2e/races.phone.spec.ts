import { expect, type Page } from '@playwright/test'
import { DEMO_RACE_IDS } from '../src/test/fakeApi/index.ts'
import {
  cell,
  horizontalOverflow,
  mockApi,
  openDiary,
  restoreGuest,
  seriousViolations,
  seriousViolationsIn,
  settled,
  stubMapNetwork,
  test,
} from './support.ts'

// Runs only in the `phone-la` project: 360x640, touch, browser zone America/Los_Angeles, account zone Europe/Warsaw.
// M8 AC6/AC7/AC9 on a phone: dots instead of bubbles, bottom-sheet details, one-column settings, no sideways scroll.

async function start(page: Page) {
  const fake = await mockApi(page)
  await stubMapNetwork(page)
  await restoreGuest(page, fake)
  return fake
}

test('proposals are dots (no bubbles) and the grid does not scroll sideways', async ({ page }) => {
  await start(page)
  await openDiary(page, '/diary/2026/11')

  const night = cell(page, '2026-11-12')
  await expect(night.locator('svg[data-kind="proposal"]')).toBeVisible()
  // The desktop bubble is `hidden sm:flex`: it is in the DOM but not displayed on a phone.
  await expect(night.locator('button[data-kind="proposal"]')).toBeHidden()
  await expect(night).toHaveAccessibleName(/1 race proposal: Demo Gliwice Night 10K, 10 km/)
  expect(await horizontalOverflow(page)).toBeLessThanOrEqual(0)
  expect(await seriousViolations(page)).toEqual([])
})

test('details open as a bottom sheet; the actions are touch sized; nothing scrolls sideways', async ({ page }) => {
  await start(page)
  await openDiary(page, `/diary/2026/11/races/${DEMO_RACE_IDS.silesianTrail}`)
  const sheet = page.getByRole('dialog', { name: 'Demo Silesian Trail Weekend' })
  await expect(sheet).toHaveAttribute('data-variant', 'sheet')
  await expect(sheet.getByRole('link', { name: /Open signup page/ })).toBeVisible()

  const box = (await sheet.boundingBox())!
  expect(box.x).toBeGreaterThanOrEqual(0)
  expect(box.x + box.width).toBeLessThanOrEqual(360.5)
  expect(box.y + box.height).toBeGreaterThan(600) // anchored to the bottom edge of the 640 px viewport
  expect(await horizontalOverflow(page)).toBeLessThanOrEqual(0)
  expect(await sheet.evaluate((el) => el.scrollWidth - el.clientWidth)).toBeLessThanOrEqual(0)

  const targets = [
    sheet.getByRole('link', { name: /Open signup page/ }),
    sheet.getByRole('button', { name: 'I signed up' }),
    sheet.getByRole('button', { name: 'Interested', exact: true }),
    sheet.getByRole('button', { name: 'Not interested' }),
  ]
  for (const target of targets) expect((await target.boundingBox())!.height).toBeGreaterThanOrEqual(43.5)

  // The signup form with the distance radios stays inside the sheet.
  await sheet.getByRole('button', { name: 'I signed up' }).click()
  const form = sheet.getByRole('form', { name: 'I signed up' })
  await expect(form.getByRole('radio', { name: '21.1 km' })).toBeAttached()
  await form.getByText('21.1 km').click()
  await form.getByRole('button', { name: 'Add to my diary' }).scrollIntoViewIfNeeded()
  expect(await horizontalOverflow(page)).toBeLessThanOrEqual(0)
  expect(await seriousViolationsIn(page, '[role="dialog"]')).toEqual([])

  await form.getByRole('button', { name: 'Add to my diary' }).click()
  await expect(page).toHaveURL(/\/diary\/2026\/11\/runs\/[0-9a-f-]+$/)
  await page.keyboard.press('Escape')
  await expect(cell(page, '2026-11-21').locator('[data-run-id]').first()).toBeAttached()
  await expect(cell(page, '2026-11-21').locator('[data-kind="proposal"]')).toHaveCount(0)
})

test('the proposals list is a sheet without sideways scroll', async ({ page }) => {
  await start(page)
  await openDiary(page, '/diary/2026/11/races')
  const list = page.getByRole('dialog', { name: 'Race proposals – November 2026' })
  await expect(list).toHaveAttribute('data-variant', 'sheet')
  await expect(list.getByRole('listitem').first()).toBeVisible()
  expect(await horizontalOverflow(page)).toBeLessThanOrEqual(0)
  expect(await list.evaluate((el) => el.scrollWidth - el.clientWidth)).toBeLessThanOrEqual(0)
  expect(await seriousViolationsIn(page, '[role="dialog"]')).toEqual([])
})

test('settings: one column, usable, saves, no sideways scroll, axe', async ({ page }) => {
  const fake = await start(page)
  await page.goto('/settings/races')
  const form = page.getByRole('form', { name: 'Race interests' })
  await expect(form).toBeVisible()
  await expect(page.locator('[data-map-state]').first()).toHaveAttribute('data-map-state', 'ready')
  await settled(page)
  expect(await horizontalOverflow(page)).toBeLessThanOrEqual(0)
  expect(await seriousViolations(page)).toEqual([])

  // One column: every region checkbox sits at the same x.
  const xs = await form
    .getByRole('checkbox')
    .evaluateAll((boxes) => boxes.filter((b) => b.closest('label')).map((b) => Math.round(b.getBoundingClientRect().x)))
  const regionXs = xs.slice(-16)
  expect(new Set(regionXs).size).toBe(1)

  // Touch targets of the checkbox rows (the whole label is the target).
  const row = form.getByRole('checkbox', { name: 'Silesian (Śląskie)' }).locator('xpath=ancestor::label')
  expect((await row.boundingBox())!.height).toBeGreaterThanOrEqual(43.5)

  // Use it: change the horizon and save.
  await form
    .getByLabel('Look ahead')
    .selectOption('60')
    .catch(async () => {
      await form.getByLabel('Look ahead').click()
      await page.getByRole('option', { name: '60 days' }).click()
    })
  await form.getByRole('button', { name: 'Save interests' }).click()
  await expect(page.getByText('Race interests saved')).toBeVisible()
  expect(await horizontalOverflow(page)).toBeLessThanOrEqual(0)
  void fake
})
