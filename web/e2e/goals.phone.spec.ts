import { expect, type Page } from '@playwright/test'
import { typeId, type RunWriteRequest } from '../src/test/fakeApi/index.ts'
import {
  cell,
  horizontalOverflow,
  mockApi,
  openDiary,
  restoreGuest,
  seriousViolations,
  seriousViolationsIn,
  settled,
  test,
} from './support.ts'

// Runs only in the `phone-la` project: 360x640, touch, browser zone America/Los_Angeles, account zone Europe/Warsaw.
// M9 AC7/AC8 on a phone: sparkles dots and goal markers, a usable goal form, a bottom-sheet Day panel with big buttons.

const HISTORY: RunWriteRequest[] = [
  { date: '2026-10-15', status: 'Completed', runTypeId: typeId('easy'), distanceMeters: 8000, durationSeconds: 2700 },
  { date: '2026-10-20', status: 'Completed', runTypeId: typeId('easy'), distanceMeters: 8000, durationSeconds: 2700 },
  { date: '2026-10-25', status: 'Completed', runTypeId: typeId('long'), distanceMeters: 16_000, durationSeconds: 5700 },
]

async function start(page: Page) {
  const fake = await mockApi(page, { goalsEnabled: true })
  const token = await restoreGuest(page, fake, { seed: HISTORY })
  const goal = fake.addGoal(token, { title: 'Autumn 10K', targetDate: '2026-11-18' })
  await fake.handle(
    new Request(`http://localhost/api/goals/${goal.id}/plan`, {
      method: 'POST',
      headers: { Authorization: `Bearer ${token}` },
    }),
  )
  return { fake, token, goal }
}

test('suggestions are sparkles dots, the goal has a marker, nothing scrolls sideways', async ({ page }) => {
  await start(page)
  await openDiary(page, '/diary/2026/11')

  const thursday = cell(page, '2026-11-05')
  await expect(thursday.locator('svg[data-kind="suggestion"]')).toBeVisible()
  await expect(thursday.locator('button[data-kind="suggestion"]')).toBeHidden()
  await expect(cell(page, '2026-11-18').locator('[data-kind="goal"]')).toBeVisible()
  await expect(thursday).toHaveAccessibleName(/1 suggested training: Tempo run/)
  expect(await horizontalOverflow(page)).toBeLessThanOrEqual(0)
  expect(await seriousViolations(page)).toEqual([])
})

test('the Day panel is a sheet with touch-sized Accept / Edit & accept / Dismiss', async ({ page }) => {
  const { fake, token } = await start(page)
  await openDiary(page, '/diary/2026/11/day/2026-11-05')
  const sheet = page.getByRole('dialog')
  await expect(sheet).toHaveAttribute('data-variant', 'sheet')
  for (const name of ['Accept', 'Dismiss']) {
    const box = await sheet.getByRole('button', { name }).boundingBox()
    expect(box!.height).toBeGreaterThanOrEqual(44)
  }
  const edit = await sheet.getByRole('link', { name: 'Edit & accept' }).boundingBox()
  expect(edit!.height).toBeGreaterThanOrEqual(44)
  await settled(page)
  expect(await seriousViolationsIn(page, '[role="dialog"]')).toEqual([])
  await sheet.getByRole('button', { name: 'Accept' }).click()
  await expect.poll(() => fake.runsOf(token).some((run) => run.date === '2026-11-05')).toBe(true)
})

test('the goal form and the goal page are usable on a phone', async ({ page }) => {
  const { goal } = await start(page)
  await page.goto('/goals/new')
  await expect(page.getByRole('heading', { level: 1, name: 'New goal' })).toBeVisible()
  expect(await horizontalOverflow(page)).toBeLessThanOrEqual(0)
  expect(await seriousViolations(page)).toEqual([])
  for (const control of [page.getByRole('button', { name: 'Save goal' }), page.getByLabel('Title')]) {
    expect((await control.boundingBox())!.height).toBeGreaterThanOrEqual(44)
  }
  await page.goto(`/goals/${goal.id}`)
  await expect(page.getByRole('heading', { level: 1, name: 'Autumn 10K' })).toBeVisible()
  await expect(page.getByRole('region', { name: /^Week \d+, from/ }).first()).toBeVisible()
  expect(await horizontalOverflow(page)).toBeLessThanOrEqual(0)
  expect(await seriousViolations(page)).toEqual([])
})
