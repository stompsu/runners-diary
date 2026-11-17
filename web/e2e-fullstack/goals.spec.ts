import { AxeBuilder } from '@axe-core/playwright'
import { expect, type Page } from '@playwright/test'
// Same test with the CSP guard (fails on Content-Security-Policy violations when the web image serves the page)
import { test } from '../e2e/support.ts'

// M9 full-stack goals journey (plan §9; nothing is mocked): web -> /api proxy -> API -> SQL, with the rule-based fake
// model (CI: Goals__Enabled=true, Ai__Provider=Fake). A fresh guest has the demo goal with its "Example plan"; the
// journey accepts and dismisses suggestions, then creates a goal and generates (fake) with the replace confirmation.

interface Suggestion {
  id: string
  goalId: string
  date: string
  status: string
}
interface Goal {
  id: string
  title: string
  plan: { source: string; pendingCount: number; dismissedCount: number; acceptedCount: number } | null
}
interface Run {
  id: string
  date: string
  status: string
  goalId: string | null
}

const DEMO_GOAL = 'Demo Beskids Trail 25K'

function iso(date: Date): string {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`
}

function addDays(date: Date, days: number): Date {
  const copy = new Date(date)
  copy.setDate(copy.getDate() + days)
  return copy
}

async function tryAsGuest(page: Page) {
  await page.goto('/')
  await expect(page).toHaveURL(/\/signin/)
  const created = page.waitForResponse(
    (r) => new URL(r.url()).pathname === '/api/guest-sessions' && r.request().method() === 'POST',
  )
  await page.getByRole('button', { name: 'Try as guest' }).click()
  expect((await created).status()).toBe(201)
  await expect(page).toHaveURL(/\/diary\/\d{4}\/\d{2}$/)
  await expect(page.getByRole('grid', { name: /^Calendar for / })).toBeVisible()
}

async function seriousViolations(page: Page): Promise<string[]> {
  await page.waitForFunction(() => document.getAnimations().every((a) => a.playState !== 'running'))
  const { violations } = await new AxeBuilder({ page }).analyze()
  return violations.filter((v) => v.impact === 'serious' || v.impact === 'critical').map((v) => `${v.id}: ${v.help}`)
}

const gridCell = (page: Page, date: string) => page.locator(`[role="gridcell"][data-date="${date}"]`)

test('guest -> demo goal and example plan -> accept and dismiss in the diary -> new goal -> generate (fake) with the replace confirmation', async ({
  page,
}) => {
  await tryAsGuest(page)

  // The bearer token the SPA uses, to read the truth from the API next to the UI.
  let authorization: string | undefined
  page.on('request', (r) => {
    const header = r.headers()['authorization']
    if (r.url().includes('/api/') && header) authorization = header
  })
  const api = async <T>(path: string): Promise<T> => {
    expect(authorization, 'the SPA sent a bearer token').toBeTruthy()
    const response = await page.request.get(path, { headers: { authorization: authorization! } })
    expect(response.status(), path).toBe(200)
    return (await response.json()) as T
  }

  // The client config switches the feature on (CI starts the API with Goals__Enabled and the fake model).
  const config = (await (await page.request.get('/api/client-config')).json()) as {
    goals?: { enabled?: boolean }
    ai?: { planGeneration?: boolean }
  }
  expect(config.goals?.enabled).toBe(true)
  expect(config.ai?.planGeneration).toBe(true)

  // 1. Goals page: the demo goal with its Example plan, no model call involved.
  await page.getByRole('link', { name: 'Goals', exact: true }).first().click()
  await expect(page).toHaveURL(/\/goals$/)
  await expect(page.getByRole('heading', { level: 1, name: 'Goals' })).toBeVisible()
  const card = page.locator('li[data-goal-id]').filter({ hasText: DEMO_GOAL })
  await expect(card).toBeVisible()
  await expect(card).toContainText('Short-term')
  await expect(card).toContainText(/Plan: \d+ suggestions, generated/)
  expect(await seriousViolations(page)).toEqual([])
  await card.getByRole('link', { name: 'Open' }).click()
  await expect(page.getByRole('heading', { level: 1, name: DEMO_GOAL })).toBeVisible()
  await expect(page.getByText('Example plan for the demo data')).toBeVisible()
  const weeks = page.getByRole('region', { name: /^Week \d+, from/ })
  await expect(weeks.first()).toBeVisible()
  expect(await weeks.count()).toBeGreaterThanOrEqual(4)
  await expect(page.locator('[data-kind="suggestion"][data-suggestion-id]').first()).toBeVisible()
  expect(await seriousViolations(page)).toEqual([])

  const goals = (await api<{ items: Goal[] }>('/api/goals')).items
  const demo = goals.find((g) => g.title === DEMO_GOAL)!
  expect(demo.plan?.source).toBe('Demo')
  const today = new Date()
  const range = `from=${iso(addDays(today, -1))}&to=${iso(addDays(today, 90))}`
  const listed = (await api<{ items: Suggestion[] }>(`/api/training-suggestions?${range}`)).items
  expect(listed.length).toBe(demo.plan!.pendingCount)
  expect(listed.every((s) => s.goalId === demo.id)).toBe(true)
  // Weeks +6..+10: nothing in the next 5 weeks, which the demo's planned runs already fill.
  expect(listed[0]!.date > iso(addDays(today, 35))).toBe(true)
  expect(listed.at(-1)!.date <= iso(addDays(today, 91))).toBe(true)

  // 2. The diary shows the suggestions, distinct from planned runs; pick two days in the first suggested month.
  const acceptDate = listed[0]!.date
  const month = acceptDate.slice(0, 7)
  const sameMonth = listed.filter((s) => s.date.startsWith(month))
  const dismissDate = sameMonth[2]!.date
  expect(dismissDate).not.toBe(acceptDate)
  await page.goto(`/diary/${month.slice(0, 4)}/${month.slice(5, 7)}`)
  const bubbles = page.locator('[role="gridcell"] button[data-kind="suggestion"]')
  await expect(bubbles.first()).toBeVisible({ timeout: 20_000 })
  expect(await bubbles.count()).toBeGreaterThanOrEqual(3)
  const bubble = gridCell(page, acceptDate).locator('button[data-kind="suggestion"]')
  await expect(bubble).toBeVisible()
  expect(await bubble.evaluate((el) => getComputedStyle(el).backgroundImage)).toContain('gradient')
  await expect(gridCell(page, acceptDate)).toHaveAccessibleName(/\d suggested trainings?: /)
  await expect(page.getByRole('list', { name: 'Legend' }).locator('[data-legend="suggestion"]')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Show suggestions' })).toHaveAttribute('aria-pressed', 'true')
  expect(await seriousViolations(page)).toEqual([])

  // 3. Accept one: a planned run appears (with the "from plan" marker) and the suggestion is gone.
  await bubble.click()
  const day = page.getByRole('dialog')
  await expect(day.getByRole('region', { name: 'Suggested trainings' })).toBeVisible()
  const accepted = page.waitForResponse(
    (r) => /\/api\/training-suggestions\/[^/]+\/accept$/.test(r.url()) && r.request().method() === 'POST',
  )
  await day.getByRole('button', { name: 'Accept', exact: true }).click()
  expect((await accepted).status()).toBe(201)
  await expect(gridCell(page, acceptDate).locator('button[data-run-id][data-from-plan="true"]')).toBeVisible()
  await expect(gridCell(page, acceptDate).locator('button[data-kind="suggestion"]')).toHaveCount(0)
  await page.keyboard.press('Escape')
  const runs = (await api<{ items: Run[] }>(`/api/runs?from=${acceptDate}&to=${acceptDate}`)).items
  const run = runs.find((r) => r.goalId === demo.id)
  expect(run).toMatchObject({ status: 'Planned', date: acceptDate })

  // 4. Dismiss another one: its bubble goes, and it is kept as Dismissed on the server.
  await gridCell(page, dismissDate).locator('button[data-kind="suggestion"]').click()
  const dismissed = page.waitForResponse(
    (r) => /\/api\/training-suggestions\/[^/]+\/dismiss$/.test(r.url()) && r.request().method() === 'POST',
  )
  await page.getByRole('dialog').getByRole('button', { name: 'Dismiss', exact: true }).click()
  expect((await dismissed).status()).toBe(200)
  await expect(gridCell(page, dismissDate).locator('button[data-kind="suggestion"]')).toHaveCount(0)
  await page.keyboard.press('Escape')
  const all = (await api<{ items: Suggestion[] }>(`/api/training-suggestions?${range}&includeDismissed=true`)).items
  expect(all.find((s) => s.date === dismissDate)?.status).toBe('Dismissed')
  expect(all.find((s) => s.date === acceptDate)).toBeUndefined()
  const afterDemo = (await api<{ items: Goal[] }>('/api/goals')).items.find((g) => g.id === demo.id)!
  expect(afterDemo.plan).toMatchObject({ acceptedCount: 1, dismissedCount: 1 })

  // 5. A new goal from the 10K preset (Performance, 70 days ahead by default) with the realistic-target hint.
  await page.goto('/goals/new')
  await expect(page.getByRole('heading', { level: 1, name: 'New goal' })).toBeVisible()
  await page.getByLabel('Title').fill('Full-stack 10K')
  const targetTime = page.getByLabel('Target time (h:mm:ss)')
  const hint = page.getByTestId('estimate-hint')
  await expect(hint.or(page.getByText('There is no recent race or hard effort'))).toBeVisible({ timeout: 20_000 })
  if (await hint.isVisible()) {
    await expect(hint).toContainText('looks realistic')
    await hint.getByRole('button', { name: 'Use as target' }).click()
    await expect(targetTime).not.toHaveValue('')
  } else {
    await targetTime.fill('0:55:00')
  }
  const created = page.waitForResponse((r) => r.url().endsWith('/api/goals') && r.request().method() === 'POST')
  await page.getByRole('button', { name: 'Save goal' }).click()
  expect((await created).status()).toBe(201)
  await expect(page.getByRole('heading', { level: 1, name: 'Full-stack 10K' })).toBeVisible()
  await expect(page.getByText('No suggestions yet')).toBeVisible()

  // 6. Generate with the fake model: the demo goal still has pending suggestions, so the SPA asks first.
  await page.getByRole('button', { name: 'Generate suggestions' }).click()
  const dialog = page.getByRole('alertdialog')
  await expect(dialog).toContainText(`Generating replaces the suggestions of "${DEMO_GOAL}"`)
  await expect(dialog).toContainText('Runs you already accepted stay in your diary.')
  await dialog.getByRole('button', { name: 'Keep them' }).click()
  await expect(dialog).toHaveCount(0)
  expect((await api<{ items: Suggestion[] }>(`/api/training-suggestions?${range}`)).items.length).toBe(
    demo.plan!.pendingCount - 2,
  )

  await page.getByRole('button', { name: 'Generate suggestions' }).click()
  const generated = page.waitForResponse(
    (r) => /\/api\/goals\/[^/]+\/plan$/.test(r.url()) && r.request().method() === 'POST',
    { timeout: 60_000 },
  )
  await page.getByRole('alertdialog').getByRole('button', { name: 'Replace and generate' }).click()
  const response = await generated
  expect(response.status()).toBe(200)
  expect(response.headers()['cache-control']).toContain('no-store')
  const plan = (await response.json()) as { goal: Goal; suggestions: Suggestion[]; replacedGoalIds: string[] }
  expect(plan.replacedGoalIds).toEqual([demo.id])
  expect(plan.suggestions.length).toBeGreaterThan(5)
  await expect(page.getByRole('button', { name: 'Regenerate' })).toBeVisible()
  await expect(page.getByText('AI-generated')).toBeVisible()
  await expect(page.getByRole('region', { name: /^Week \d+, from/ }).first()).toBeVisible()
  expect(await seriousViolations(page)).toEqual([])

  // 7. The set now belongs to the new goal; the demo plan is replaced; the accepted run stays in the diary.
  const finalSuggestions = (
    await api<{ items: Suggestion[] }>(`/api/training-suggestions?${range}&includeDismissed=true`)
  ).items
  expect(finalSuggestions.length).toBeGreaterThan(0)
  expect(finalSuggestions.every((s) => s.goalId === plan.goal.id)).toBe(true)
  const finalGoals = (await api<{ items: Goal[] }>('/api/goals')).items
  expect(finalGoals.find((g) => g.id === demo.id)?.plan?.pendingCount).toBe(0)
  expect(finalGoals.find((g) => g.id === plan.goal.id)?.plan).toMatchObject({
    source: 'Model',
    pendingCount: plan.suggestions.length,
  })
  const keptRun = (await api<{ items: Run[] }>(`/api/runs?from=${acceptDate}&to=${acceptDate}`)).items.find(
    (r) => r.id === run!.id,
  )
  expect(keptRun).toMatchObject({ status: 'Planned', goalId: demo.id })

  // The diary shows the new goal's suggestions, and the demo's accepted run keeps its marker.
  const first = finalSuggestions[0]!.date
  await page.goto(`/diary/${first.slice(0, 4)}/${first.slice(5, 7)}`)
  await expect(gridCell(page, first).locator('button[data-kind="suggestion"]')).toBeVisible({ timeout: 20_000 })
  if (acceptDate.startsWith(first.slice(0, 7))) {
    await expect(gridCell(page, acceptDate).locator('button[data-run-id][data-from-plan="true"]')).toBeVisible()
  }
})
