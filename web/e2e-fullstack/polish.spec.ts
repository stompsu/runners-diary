import { expect } from '@playwright/test'
// Same test with the CSP guard (fails on Content-Security-Policy violations when the web image serves the page)
import { test } from '../e2e/support.ts'

// M10 B5: a Polish guest through the real stack (web -> /api proxy -> API -> SQL). The browser locale is pl-PL, so the
// SPA detects Polish and asks for `POST /api/guest-sessions?lang=pl`; the API then seeds the demo data with the Polish
// texts of seed/demo/i18n/pl.json. Nothing is mocked. The AI summary and plan are requested in Polish too (Part C3).

test.use({ locale: 'pl-PL', timezoneId: 'Europe/Warsaw' })

const DEMO_GOAL_PL = 'Demo: trail w Beskidach 25K' // "Demo Beskids Trail 25K"
const DEMO_RACE = 'Gliwice 10K City Run' // week -20, Sunday; its rating comment is a translated demo text
const RACE_COMMENT_PL = 'Nowy rekord życiowy. Początek trochę za szybki, ale udało się utrzymać tempo.'

interface Run {
  id: string
  date: string
  title: string | null
}

test('Polish guest -> Polish UI and demo texts (run comment, goal, plan) -> switch to English and back', async ({
  page,
}) => {
  // The bearer token the SPA uses, to read the truth from the API next to the UI.
  let authorization: string | undefined
  page.on('request', (r) => {
    const header = r.headers()['authorization']
    if (r.url().includes('/api/') && header) authorization = header
  })

  await page.goto('/')
  await expect(page).toHaveURL(/\/signin/)
  await expect(page.locator('html')).toHaveAttribute('lang', 'pl')
  await expect(page.getByRole('heading', { level: 1, name: 'Zaloguj się do Runners Diary' })).toBeVisible()
  const created = page.waitForResponse(
    (r) => new URL(r.url()).pathname === '/api/guest-sessions' && r.request().method() === 'POST',
  )
  await page.getByRole('button', { name: 'Wypróbuj jako gość' }).click()
  const response = await created
  expect(response.status()).toBe(201)
  expect(new URL(response.url()).searchParams.get('lang')).toBe('pl')

  // The diary is Polish; the demo banner too.
  await expect(page).toHaveURL(/\/diary\/\d{4}\/\d{2}$/)
  await expect(page.getByRole('heading', { level: 1, name: /^Dziennik – / })).toBeVisible()
  await expect(page.getByRole('grid', { name: /^Kalendarz: / })).toBeVisible()
  await expect(page.getByText('Jesteś w trybie demo. Dane są usuwane po 24 godzinach.')).toBeVisible()
  await expect(page.locator('[role="gridcell"] [data-run-id]').first()).toBeAttached({ timeout: 20_000 })

  // The profile language was stored by the guest creation: /api/me reports it.
  expect(authorization, 'the SPA sent a bearer token').toBeTruthy()
  const me = await page.request.get('/api/me', { headers: { authorization: authorization! } })
  expect(me.status()).toBe(200)
  expect(((await me.json()) as { language: string | null }).language).toBe('pl')

  // The seeded race run (20 weeks back) has its rating comment in Polish.
  // GET /api/runs allows at most 92 days: weeks -22 to -9 contain the race (week -20).
  const isoDay = (offset: number) => new Date(Date.now() + offset * 86_400_000).toISOString().slice(0, 10)
  const runs = await page.request.get(`/api/runs?from=${isoDay(-22 * 7)}&to=${isoDay(-22 * 7 + 91)}`, {
    headers: { authorization: authorization! },
  })
  expect(runs.status()).toBe(200)
  const race = ((await runs.json()) as { items: Run[] }).items.find((run) => run.title === DEMO_RACE)
  expect(race, `the demo race "${DEMO_RACE}" is seeded`).toBeDefined()
  const [year, month] = race!.date.split('-') as [string, string]
  await page.goto(`/diary/${year}/${month}/day/${race!.date}`)
  const day = page.getByRole('dialog')
  await expect(day).toBeVisible()
  await day.getByRole('listitem').filter({ hasText: DEMO_RACE }).getByRole('link', { name: 'Pokaż szczegóły' }).click()
  const details = page.getByRole('dialog', { name: new RegExp(DEMO_RACE) })
  await expect(details.getByText(RACE_COMMENT_PL)).toBeVisible()
  await expect(details.getByText('New personal best')).toHaveCount(0)
  await page.keyboard.press('Escape')

  // Goals: the demo goal and its example plan are Polish (feature on in CI: Goals__Enabled).
  await page.goto('/goals')
  await expect(page.getByRole('heading', { level: 1, name: 'Cele' })).toBeVisible()
  const card = page.locator('li[data-goal-id]').filter({ hasText: DEMO_GOAL_PL })
  await expect(card).toBeVisible()
  await card.getByRole('link', { name: 'Otwórz' }).click()
  await expect(page.getByRole('heading', { level: 1, name: DEMO_GOAL_PL })).toBeVisible()
  await expect(page.getByText('Po półmaratonie plan przechodzi na trail', { exact: false })).toBeVisible()

  // The plan of the demo goal is in the guest's language, so the Polish UI shows no language hint.
  await expect(page.getByTestId('plan-language-hint')).toHaveCount(0)

  // Insights (C3): the SPA asks for the summary in Polish and the API answers with the Polish example summary of the
  // guest's default view (a model summary from the fake provider would also be Polish, but is not generated here).
  const summaryResponse = page.waitForResponse(
    (r) => new URL(r.url()).pathname === '/api/insights/summary' && r.request().method() === 'GET',
  )
  await page.goto('/insights')
  const summary = await summaryResponse
  expect(new URL(summary.url()).searchParams.get('lang')).toBe('pl')
  expect(summary.status()).toBe(200)
  const summaryBody = (await summary.json()) as { period: { lang: string }; summary: { source: string } | null }
  expect(summaryBody.period.lang).toBe('pl')
  expect(summaryBody.summary?.source).toBe('demo')
  await expect(page.getByTestId('summary-badge')).toHaveText('Przykładowe podsumowanie dla danych demo')
  await expect(page.getByTestId('summary-card')).not.toContainText('You covered')
  await page.goto('/goals')
  await page.locator('li[data-goal-id]').filter({ hasText: DEMO_GOAL_PL }).getByRole('link', { name: 'Otwórz' }).click()
  await expect(page.getByRole('heading', { level: 1, name: DEMO_GOAL_PL })).toBeVisible()

  // Switching to English re-renders without a reload and is saved to the profile; the data stays as seeded (Polish).
  const saved = page.waitForResponse(
    (r) => new URL(r.url()).pathname === '/api/me/language' && r.request().method() === 'PUT',
  )
  await page.getByRole('combobox', { name: 'Język' }).selectOption('en')
  expect((await saved).status()).toBe(204)
  await expect(page.getByRole('heading', { level: 1, name: DEMO_GOAL_PL })).toBeVisible()
  await expect(page.getByRole('link', { name: 'All goals' })).toBeVisible()
  await expect(page.locator('html')).toHaveAttribute('lang', 'en')

  // A reload keeps English (rd.lang and the profile agree), and the profile says so.
  await page.reload()
  await expect(page.locator('html')).toHaveAttribute('lang', 'en')
  const after = await page.request.get('/api/me', { headers: { authorization: authorization! } })
  expect(((await after.json()) as { language: string | null }).language).toBe('en')
})
