import { expect, type Page } from '@playwright/test'
import { faults, PLAN_PROBLEMS, typeId, type RunWriteRequest } from '../src/test/fakeApi/index.ts'
import {
  cell,
  mockApi,
  openDiary,
  restoreGuest,
  seriousViolations,
  seriousViolationsIn,
  settled,
  stubMapNetwork,
  test,
} from './support.ts'

// M9 AC7/AC8 (E2E parts): goals page, plan generation, suggestions in the diary, accept and dismiss. Fake API, desktop.
// "Today" is 2026-11-03 (Tuesday). The fake plan puts Tue/Thu/Sat/Sun sessions from Wed 11-04.

const HISTORY: RunWriteRequest[] = [
  {
    date: '2026-10-10',
    status: 'Completed',
    runTypeId: typeId('roadRace'),
    title: 'Autumn 10K',
    distanceMeters: 10_000,
    durationSeconds: 3000,
    race: { name: 'Autumn 10K', distanceCategory: 'TenK', resultTimeSeconds: 3000 },
  },
  { date: '2026-10-15', status: 'Completed', runTypeId: typeId('easy'), distanceMeters: 8000, durationSeconds: 2700 },
  { date: '2026-10-20', status: 'Completed', runTypeId: typeId('easy'), distanceMeters: 8000, durationSeconds: 2700 },
  { date: '2026-10-25', status: 'Completed', runTypeId: typeId('long'), distanceMeters: 16_000, durationSeconds: 5700 },
  { date: '2026-11-07', status: 'Planned', runTypeId: typeId('easy'), title: 'My own easy run', distanceMeters: 6000 },
]

async function start(page: Page, options: { planGeneration?: boolean } = {}) {
  const fake = await mockApi(page, { goalsEnabled: true, ...options })
  const token = await restoreGuest(page, fake, { seed: HISTORY })
  return { fake, token }
}

const suggestionBubbles = (page: Page) => page.locator('[role="gridcell"] button[data-kind="suggestion"]')

async function createGoal(page: Page) {
  await page.goto('/goals')
  await expect(page.getByRole('heading', { level: 1, name: 'Goals' })).toBeVisible()
  await page.getByRole('link', { name: 'New goal' }).first().click()
  await expect(page.getByRole('heading', { level: 1, name: 'New goal' })).toBeVisible()
  await page.getByRole('radio', { name: '10K' }).check()
  const hint = page.getByTestId('estimate-hint')
  await expect(hint).toContainText('about 0:50:00 looks realistic')
  await hint.getByRole('button', { name: 'Use as target' }).click()
  await expect(page.getByLabel('Target time (h:mm:ss)')).toHaveValue('0:50:00')
  await page.getByRole('button', { name: 'Save goal' }).click()
  await expect(page.getByRole('heading', { level: 1, name: '10K' })).toBeVisible()
}

test('create a goal, generate a plan, see distinct suggestions in the diary, accept one and dismiss one', async ({
  page,
}) => {
  const { fake, token } = await start(page)
  await page.goto('/diary/2026/11')
  await page.getByRole('link', { name: 'Goals' }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'Goals' })).toBeVisible()

  await createGoal(page)
  await expect(page.getByText('No suggestions yet')).toBeVisible()
  await page.getByRole('button', { name: 'Generate suggestions' }).click()
  await expect(page.getByRole('button', { name: 'Regenerate' })).toBeVisible()
  await expect(page.getByRole('region', { name: /^Week \d+, from/ }).first()).toBeVisible()
  await expect(page.getByText('AI-generated')).toBeVisible()
  expect(fake.suggestionsOf(token).length).toBeGreaterThan(5)
  expect(await seriousViolations(page)).toEqual([])

  // In the diary: hatched bubbles, distinct from the dashed planned run, legend and toggle.
  await openDiary(page, '/diary/2026/11')
  await expect(suggestionBubbles(page).first()).toBeVisible()
  const bubble = cell(page, '2026-11-05').locator('button[data-kind="suggestion"]')
  await expect(bubble).toContainText('Tempo')
  expect(await bubble.evaluate((el) => getComputedStyle(el).backgroundImage)).toContain('gradient')
  expect(await bubble.evaluate((el) => getComputedStyle(el).borderTopStyle)).toBe('solid')
  const planned = cell(page, '2026-11-07').locator('button[data-run-id]')
  expect(await planned.evaluate((el) => getComputedStyle(el).borderTopStyle)).toBe('dashed')
  expect(await planned.evaluate((el) => getComputedStyle(el).backgroundImage)).not.toContain('gradient')
  await expect(page.getByRole('list', { name: 'Legend' }).locator('[data-legend="suggestion"]')).toBeVisible()
  await expect(cell(page, '2026-11-05')).toHaveAccessibleName(/1 suggested training: Tempo run, /)
  await expect(page.getByRole('button', { name: 'Show suggestions' })).toHaveAttribute('aria-pressed', 'true')
  expect(await seriousViolations(page)).toEqual([])

  // Accept one: it becomes a planned run with the "from plan" marker.
  await bubble.click()
  const day = page.getByRole('dialog')
  await expect(day.getByRole('region', { name: 'Suggested trainings' })).toBeVisible()
  await day.getByRole('button', { name: 'Accept' }).click()
  await expect(cell(page, '2026-11-05').locator('button[data-run-id][data-from-plan="true"]')).toBeVisible()
  await expect(cell(page, '2026-11-05').locator('button[data-kind="suggestion"]')).toHaveCount(0)
  expect(fake.runsOf(token).find((run) => run.date === '2026-11-05')).toMatchObject({ status: 'Planned' })
  await page.keyboard.press('Escape')

  // Dismiss another one: the bubble goes.
  await cell(page, '2026-11-08').locator('button[data-kind="suggestion"]').click()
  await page.getByRole('dialog').getByRole('button', { name: 'Dismiss' }).click()
  await expect(cell(page, '2026-11-08').locator('button[data-kind="suggestion"]')).toHaveCount(0)
  expect(fake.suggestionsOf(token).find((s) => s.date === '2026-11-08')?.status).toBe('Dismissed')
})

test('keyboard path: cell, Enter, Accept; Edit & accept with a changed distance', async ({ page }) => {
  const { fake, token } = await start(page)
  const goal = fake.addGoal(token, { title: 'Autumn 10K', targetDate: '2026-12-20' })
  await fake.handle(
    new Request(`http://localhost/api/goals/${goal.id}/plan`, {
      method: 'POST',
      headers: { Authorization: `Bearer ${token}` },
    }),
  )
  await openDiary(page, '/diary/2026/11')
  await expect(suggestionBubbles(page).first()).toBeVisible()

  await cell(page, '2026-11-05').focus()
  await page.keyboard.press('Enter')
  const day = page.getByRole('dialog')
  await expect(day.getByRole('region', { name: 'Suggested trainings' })).toBeVisible()
  await settled(page)
  expect(await seriousViolationsIn(page, '[role="dialog"]')).toEqual([])
  await day.getByRole('link', { name: 'Edit & accept' }).focus()
  await page.keyboard.press('Enter')

  const form = page.getByRole('dialog', { name: 'Edit & accept suggestion' })
  await expect(form.getByTestId('pace-hint')).toBeVisible()
  await form.getByLabel('Distance (km)').fill('7')
  await form.getByRole('button', { name: 'Save' }).click()
  await expect(page.getByRole('dialog', { name: /Tempo run/ })).toBeVisible()
  expect(fake.runsOf(token).find((run) => run.date === '2026-11-05')).toMatchObject({
    distanceMeters: 7000,
    status: 'Planned',
    goalId: goal.id,
  })
})

test('an ambitious target is flagged on the plan and Use as target moves it to what the plan trains toward', async ({
  page,
}) => {
  const { fake, token } = await start(page, { planGeneration: true })
  const goal = fake.addGoal(token, {
    title: 'Autumn 10K',
    type: 'Performance',
    targetDurationSeconds: 2400,
    targetDate: '2026-12-20',
  })
  await fake.handle(
    new Request(`http://localhost/api/goals/${goal.id}/plan`, {
      method: 'POST',
      headers: { Authorization: `Bearer ${token}` },
    }),
  )
  await page.goto(`/goals/${goal.id}`)
  const notice = page.getByRole('region', { name: 'This target seems very ambitious' })
  await expect(notice).toContainText('this plan trains toward ~0:50:00. 0:40:00 by')
  expect(await seriousViolationsIn(page, '[data-testid="plan-ambitious"]')).toEqual([])
  await notice.getByRole('button', { name: 'Use ~0:50:00 as target' }).click()
  await expect(page.getByTestId('plan-outdated')).toBeVisible()
  await expect(notice).toHaveCount(0)
  expect(fake.goalsOf(token)[0]!.targetDurationSeconds).toBe(3000)
})

test('a second goal asks before replacing the first plan', async ({ page }) => {
  const { fake, token } = await start(page)
  const first = fake.addGoal(token, { title: 'Spring half', targetDate: '2027-03-20' })
  const second = fake.addGoal(token, { title: 'Autumn 10K', targetDate: '2026-12-20' })
  await fake.handle(
    new Request(`http://localhost/api/goals/${first.id}/plan`, {
      method: 'POST',
      headers: { Authorization: `Bearer ${token}` },
    }),
  )
  await page.goto(`/goals/${second.id}`)
  await page.getByRole('button', { name: 'Generate suggestions' }).click()
  const dialog = page.getByRole('alertdialog')
  await expect(dialog).toContainText('Generating replaces the suggestions of "Spring half"')
  await dialog.getByRole('button', { name: 'Replace and generate' }).click()
  await expect(page.getByRole('button', { name: 'Regenerate' })).toBeVisible()
  expect(fake.suggestionsOf(token).every((s) => s.goalId === second.id)).toBe(true)
})

test('an AI error is shown as a friendly message and nothing changes', async ({ page }) => {
  const { fake, token } = await start(page)
  const goal = fake.addGoal(token, { title: 'Autumn 10K', targetDate: '2026-12-20' })
  fake.addFault(
    faults.problem({ method: 'POST', path: /\/plan$/ }, 502, PLAN_PROBLEMS.failed, {
      members: { reason: 'invalidOutput' },
      times: 1,
    }),
  )
  await page.goto(`/goals/${goal.id}`)
  await page.getByRole('button', { name: 'Generate suggestions' }).click()
  await expect(page.getByRole('alert')).toHaveText('The AI could not produce a safe plan. Try again later.')
  expect(fake.suggestionsOf(token)).toHaveLength(0)
  await expect(page.getByRole('button', { name: 'Generate suggestions' })).toBeEnabled()
  expect(await seriousViolations(page)).toEqual([])
})

test('a goal gets a location from the map, shows it, and the location can be cleared again', async ({ page }) => {
  const { fake, token } = await start(page)
  await stubMapNetwork(page)
  await page.goto('/goals/new')
  await expect(page.getByRole('heading', { level: 1, name: 'New goal' })).toBeVisible()
  await page.getByLabel('Target time (h:mm:ss)').fill('0:55:00')
  await page.getByRole('button', { name: 'Pick on map' }).click()
  const picker = page.getByRole('dialog', { name: 'Choose a location' })
  const map = page.getByRole('region', { name: 'Map for choosing a location' })
  await expect(map).toHaveAttribute('data-map-state', 'ready')
  await map.locator('.maplibregl-canvas').click()
  await picker.getByRole('button', { name: 'Use this location' }).click()
  await expect(picker).toHaveCount(0)
  await page.getByLabel('Place name').fill('Gliwice start line')
  expect(await seriousViolations(page)).toEqual([])
  await page.getByRole('button', { name: 'Save goal' }).click()

  await expect(page.getByRole('heading', { level: 1, name: '10K' })).toBeVisible()
  await expect(page.getByText(/Gliwice start line/)).toBeVisible()
  const stored = fake.goalsOf(token)[0]?.location
  expect(stored?.name).toBe('Gliwice start line')
  expect(stored?.latitude).toBeGreaterThan(50.2)
  expect(stored?.latitude).toBeLessThan(50.4)

  await page.getByRole('link', { name: 'Edit' }).click()
  await page.getByRole('button', { name: 'Clear location' }).click()
  await page.getByRole('button', { name: 'Save goal' }).click()
  await expect(page.getByRole('heading', { level: 1, name: '10K' })).toBeVisible()
  await expect(page.getByText(/Gliwice start line/)).toHaveCount(0)
  expect(fake.goalsOf(token)[0]?.location).toBeNull()
})
