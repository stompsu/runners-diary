import { expect, type Page } from '@playwright/test'
import { DEMO_RACE_IDS, DEMO_REPORT_TODAY, typeId, type RunWriteRequest } from '../src/test/fakeApi/index.ts'
import { horizontalOverflow, mockApi, restoreGuest, stubMapNetwork, test } from './support.ts'

// M10 B5: the main screens in Polish, in the `desktop-pl` and `phone-pl` projects only (playwright.config.ts; the
// browser locale is pl-PL, so the language is detected, no rd.lang is set). The phone project is 360 px wide: Polish
// strings are longer, so every screen is also checked for sideways scrolling. The production build has no missing-key
// handler (English is the silent fallback), so a gap shows up as an English word or a raw key; both are checked.

// A namespace prefix ("races:x") or a dotted camelCase path ("interests.where.title"): host names are all lower case.
const RAW_KEY =
  /\b(common|auth|errors|runTypes|calendar|runs|maps|tracks|insights|races|goals):[A-Za-z]|\b[a-z]+(\.[a-z]*[A-Z][A-Za-z]*|\.[a-z]+){1,}\.[a-zA-Z]*[A-Z][A-Za-z]*\b|\b[a-z]+[A-Z][A-Za-z]*\.[a-z][A-Za-z]*\.[a-zA-Z]+\b/
// English UI words that must not appear on a Polish screen (the demo data is English in the fake API on purpose).
const ENGLISH_UI = ['Add run', 'Save', 'Cancel', 'Today', 'Previous', 'Next month', 'Loading', 'Try as guest']

async function expectPolishScreen(page: Page): Promise<void> {
  await expect(page.locator('html')).toHaveAttribute('lang', 'pl')
  const text = await page.locator('body').innerText()
  expect(text).not.toMatch(RAW_KEY)
  for (const word of ENGLISH_UI) expect(text, `English UI text "${word}"`).not.toContain(word)
  expect(await horizontalOverflow(page), 'horizontal overflow').toBeLessThanOrEqual(0)
}

// Enough history for the fake plan generator (the same shape goals.spec.ts uses).
const HISTORY: RunWriteRequest[] = [
  { date: '2026-10-10', status: 'Completed', runTypeId: typeId('easy'), distanceMeters: 10_000, durationSeconds: 3300 },
  { date: '2026-10-15', status: 'Completed', runTypeId: typeId('easy'), distanceMeters: 8000, durationSeconds: 2700 },
  { date: '2026-10-20', status: 'Completed', runTypeId: typeId('easy'), distanceMeters: 8000, durationSeconds: 2700 },
  { date: '2026-10-25', status: 'Completed', runTypeId: typeId('long'), distanceMeters: 16_000, durationSeconds: 5700 },
]

async function start(page: Page) {
  const fake = await mockApi(page, { goalsEnabled: true })
  await stubMapNetwork(page)
  await restoreGuest(page, fake)
  return fake
}

test('the sign-in page is Polish, the switcher offers both languages, the guest lands in a Polish diary', async ({
  page,
}) => {
  await mockApi(page)
  await page.goto('/')
  await expect(page).toHaveURL(/\/signin/)
  await expect(page.getByRole('heading', { level: 1, name: 'Zaloguj się do Runners Diary' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Wypróbuj jako gość' })).toBeVisible()
  await expectPolishScreen(page)

  const switcher = page.getByRole('combobox', { name: 'Język' })
  await expect(switcher).toHaveValue('pl')
  await expect(switcher.getByRole('option')).toHaveText(['English', 'Polski'])

  const session = page.waitForRequest((request) => request.url().includes('/api/guest-sessions'))
  await page.getByRole('button', { name: 'Wypróbuj jako gość' }).click()
  expect(new URL((await session).url()).searchParams.get('lang')).toBe('pl')
  await expect(page.getByRole('heading', { level: 1, name: /^Dziennik – / })).toBeVisible()
  await expect(page.getByText('Jesteś w trybie demo. Dane są usuwane po 24 godzinach.', { exact: false })).toBeVisible()
  await expectPolishScreen(page)
})

test('the language can be switched back to English and to Polish again without reload', async ({ page }) => {
  await start(page)
  await page.goto('/diary/2026/11')
  await expect(page.getByRole('heading', { level: 1, name: /^Dziennik – / })).toBeVisible()
  const switcher = page.getByRole('combobox', { name: 'Język' })
  await switcher.selectOption('en')
  await expect(page.getByRole('heading', { level: 1, name: /^Diary – / })).toBeVisible()
  await expect(page.locator('html')).toHaveAttribute('lang', 'en')
  await page.getByRole('combobox', { name: 'Language' }).selectOption('pl')
  await expect(page.getByRole('heading', { level: 1, name: 'Dziennik – listopad 2026' })).toBeVisible()
})

test('diary: Polish month, week names, week totals, month list and run form fit the screen', async ({ page }) => {
  await start(page)
  await page.goto('/diary/2026/11')
  await expect(page.getByRole('heading', { level: 1, name: 'Dziennik – listopad 2026' })).toBeVisible()
  await expect(page.getByRole('grid', { name: 'Kalendarz: listopad 2026', includeHidden: true })).toBeVisible()
  await expect(page.locator('[role="gridcell"] [data-run-id]').first()).toBeAttached()
  await expectPolishScreen(page)

  await page.getByRole('link', { name: 'Lista miesiąca' }).click()
  const list = page.getByRole('dialog', { name: /^Treningi: / })
  await expect(list.getByRole('listitem').first()).toBeVisible()
  expect(await horizontalOverflow(page)).toBeLessThanOrEqual(0)
  expect(await list.evaluate((el) => el.scrollWidth - el.clientWidth)).toBeLessThanOrEqual(0)
  await page.keyboard.press('Escape')

  await page.getByRole('link', { name: 'Dodaj trening', exact: true }).click()
  const form = page.getByRole('dialog', { name: 'Dodaj trening' })
  await expect(form.getByLabel('Dystans (km)')).toBeVisible()
  await expect(form.getByLabel('Tytuł')).toBeVisible()
  expect(await horizontalOverflow(page)).toBeLessThanOrEqual(0)
  expect(await form.evaluate((el) => el.scrollWidth - el.clientWidth)).toBeLessThanOrEqual(0)
  const text = await form.innerText()
  expect(text).not.toMatch(RAW_KEY)
  for (const word of ENGLISH_UI) expect(text, `English UI text "${word}"`).not.toContain(word)
})

test('insights is Polish and fits the screen', async ({ page }) => {
  const fake = await mockApi(page, { today: DEMO_REPORT_TODAY, insights: 'demo' })
  await restoreGuest(page, fake)
  await page.goto('/insights')
  await expect(page.getByRole('heading', { level: 1, name: 'Analiza' })).toBeVisible()
  await expect(page.getByTestId('kpi-grid')).toBeVisible()
  await expect(page.getByTestId('acwr-card')).toBeVisible()
  await expectPolishScreen(page)
})

test('race interests (settings) are Polish and fit the screen', async ({ page }) => {
  await start(page)
  await page.goto('/settings/races')
  await expect(page.getByRole('heading', { level: 1, name: 'Zainteresowania startami' })).toBeVisible()
  await expectPolishScreen(page)
})

test('goals: the list, a goal with a generated plan, and the new-goal form are Polish and fit the screen', async ({
  page,
}) => {
  const fake = await mockApi(page, { goalsEnabled: true, planGeneration: true })
  const token = await restoreGuest(page, fake, { seed: HISTORY })
  await page.goto('/goals')
  await expect(page.getByRole('heading', { level: 1, name: 'Cele' })).toBeVisible()
  await expectPolishScreen(page)

  const goal = fake.addGoal(token, { title: 'Jesienna dycha', targetDate: '2026-12-20' })
  await fake.handle(
    new Request(`http://localhost/api/goals/${goal.id}/plan`, {
      method: 'POST',
      headers: { Authorization: `Bearer ${token}` },
    }),
  )
  await page.goto(`/goals/${goal.id}`)
  await expect(page.getByRole('heading', { level: 1, name: 'Jesienna dycha' })).toBeVisible()
  await expect(page.getByRole('region', { name: /^Tydzień \d+/ }).first()).toBeVisible()
  await expectPolishScreen(page)

  await page.goto('/goals/new')
  await expect(page.getByRole('heading', { level: 1, name: 'Nowy cel' })).toBeVisible()
  await expectPolishScreen(page)
})

test('diary with race proposals and suggested trainings: bubbles, legend, race details and day panel are Polish', async ({
  page,
}) => {
  const fake = await mockApi(page, { goalsEnabled: true, planGeneration: true })
  await stubMapNetwork(page)
  const token = await restoreGuest(page, fake, { seed: HISTORY })
  const goal = fake.addGoal(token, { title: 'Jesienna dycha', targetDate: '2026-12-20' })
  await fake.handle(
    new Request(`http://localhost/api/goals/${goal.id}/plan`, {
      method: 'POST',
      headers: { Authorization: `Bearer ${token}` },
    }),
  )
  await page.goto('/diary/2026/11')
  // The phone shows the month as a day list without bubbles; the race details open from their URL there.
  const phone = page.viewportSize()!.width < 640
  if (!phone) {
    await expect(page.locator('[role="gridcell"] button[data-kind="proposal"]').first()).toBeVisible()
    await expect(page.locator('[role="gridcell"] button[data-kind="suggestion"]').first()).toBeVisible()
  }
  await expectPolishScreen(page)

  if (phone) await page.goto(`/diary/2026/11/races/${DEMO_RACE_IDS.silesianTrail}`)
  else await page.locator(`[role="gridcell"] button[data-race-id="${DEMO_RACE_IDS.silesianTrail}"]`).click()
  const details = page.getByRole('dialog')
  await expect(details).toBeVisible()
  expect(await horizontalOverflow(page)).toBeLessThanOrEqual(0)
  expect(await details.evaluate((el) => el.scrollWidth - el.clientWidth)).toBeLessThanOrEqual(0)
  const text = await details.innerText()
  expect(text).not.toMatch(RAW_KEY)
  for (const word of ENGLISH_UI) expect(text, `English UI text "${word}"`).not.toContain(word)
})
