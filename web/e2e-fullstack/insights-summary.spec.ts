import { AxeBuilder } from '@axe-core/playwright'
import { expect, test, type Page } from '@playwright/test'

// Full-stack AI summary (M6 step 5, plan §6): nothing is mocked and, as in CI, no AI provider is configured
// (Ai:Provider=None). A seeded guest's default view shows the hand-written "Example summary" resolved against the guest's
// own numbers; generating is unavailable and the API says so consistently (client-config, GET availability, POST 503).

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

async function seriousViolations(page: Page): Promise<string[]> {
  await page.waitForFunction(() => document.getAnimations().every((a) => a.playState !== 'running'))
  const { violations } = await new AxeBuilder({ page }).analyze()
  return violations.filter((v) => v.impact === 'serious' || v.impact === 'critical').map((v) => `${v.id}: ${v.help}`)
}

function num(text: string | null): number {
  const match = /-?\d[\d,]*(?:\.\d+)?/.exec((text ?? '').replace(/ | /g, ''))
  if (!match) throw new Error(`no number in "${text}"`)
  return Number(match[0].replaceAll(',', ''))
}

test('guest -> default /insights shows the real Example summary, formatted, with the AI and medical disclosures', async ({
  page,
}) => {
  await tryAsGuest(page)

  // The summary request of the default view, answered by the real API.
  const summaryResponse = page.waitForResponse(
    (r) => r.url().includes('/api/insights/summary') && r.request().method() === 'GET',
  )
  await page.getByRole('link', { name: 'Insights', exact: true }).first().click()
  await expect(page.getByRole('button', { name: 'Last 12 weeks' })).toHaveAttribute('aria-pressed', 'true')
  const response = await summaryResponse
  expect(response.status()).toBe(200)
  expect(response.headers()['cache-control']).toContain('no-store')
  const body = (await response.json()) as {
    state: string
    summary: { source: string; model: string | null; values: Record<string, unknown> } | null
    generation: { availability: string }
  }
  expect(body.state).toBe('fresh')
  expect(body.summary?.source).toBe('demo')
  expect(body.summary?.model).toBeNull()
  expect(body.generation.availability).toBe('notConfigured')

  const card = page.getByTestId('summary-card')
  await expect(card).toBeVisible()
  await expect(card.getByRole('heading', { level: 2, name: 'AI summary' })).toBeVisible()
  await expect(card.getByTestId('summary-badge')).toHaveText('Example summary for the demo data')
  const headline = card.getByRole('heading', { level: 3 })
  await expect(headline).toBeVisible()

  // Real values, resolved and formatted: no raw placeholders, no broken numbers, no "unknown key" dashes.
  const text = (await card.textContent()) ?? ''
  expect(text).not.toMatch(/[{}]/)
  expect(text).not.toMatch(/undefined|NaN|null|Infinity|\[object/i)
  expect(text).not.toContain('—')
  expect((await headline.textContent())!.trim().length).toBeGreaterThan(10)
  expect(await card.getByRole('listitem').count()).toBeGreaterThan(0)

  // The distance the narrative states is the KPI distance of the same report (plan §6).
  const kpiDistance = num(await page.locator('[data-kpi="distance"] dd').first().textContent())
  const distances = [...text.replace(/ | /g, ' ').matchAll(/(\d[\d,]*(?:\.\d+)?) km/g)].map((m) =>
    Number(m[1]!.replaceAll(',', '')),
  )
  expect(distances).toContain(kpiDistance)

  // Disclosures are fixed UI strings, not model output.
  const footer = card.getByTestId('summary-footer')
  await expect(footer).toContainText('An example written for the demo data')
  await expect(footer).toContainText(
    'Written by an AI model from your computed training numbers. It can make mistakes.',
  )
  await expect(footer).toContainText('This is not medical advice.')

  // AI is not configured: no Generate button, and the card says why.
  await expect(card.getByRole('button', { name: /Generate|Regenerate/ })).toHaveCount(0)
  await expect(card).toContainText("Generating your own summary isn't available here.")
  expect(await seriousViolations(page)).toEqual([])
})

test('another period has no stored summary: the card says AI is not enabled, and the API refuses POST with 503 feature-unavailable', async ({
  page,
}) => {
  await tryAsGuest(page)
  let authorization: string | undefined
  page.on('request', (r) => {
    const header = r.headers()['authorization']
    if (r.url().includes('/api/') && header) authorization = header
  })

  await page.getByRole('link', { name: 'Insights', exact: true }).first().click()
  await expect(page.getByTestId('kpi-grid')).toBeVisible()
  const summaryAgain = page.waitForResponse(
    (r) => r.url().includes('/api/insights/summary') && r.request().method() === 'GET' && r.url().includes('compare='),
  )
  await page.getByRole('button', { name: 'This month' }).click()
  await expect(page.getByRole('button', { name: 'This month' })).toHaveAttribute('aria-pressed', 'true')
  const response = await summaryAgain
  expect(response.status()).toBe(200)

  const card = page.getByTestId('summary-card')
  // This month is not the demo preset: either nothing (not configured) or an automatic note for a thin month.
  await expect(card.getByTestId('summary-badge').or(card.getByTestId('summary-not-configured'))).toBeVisible()
  const text = (await card.textContent()) ?? ''
  expect(text).not.toMatch(/[{}]|undefined|NaN/)
  await expect(card.getByTestId('summary-badge')).not.toHaveText('Example summary for the demo data')
  await expect(card.getByRole('button', { name: /Generate|Regenerate/ })).toHaveCount(0)
  expect(await seriousViolations(page)).toEqual([])

  // The server agrees with the UI: the client config advertises no generation, and a direct POST is refused as a
  // problem+json, without any quota being spent.
  const config = await page.request.get('/api/client-config')
  expect(((await config.json()) as { ai?: { summaryGeneration?: boolean } }).ai?.summaryGeneration).toBe(false)
  expect(authorization).toBeTruthy()
  const post = await page.request.post('/api/insights/summary', {
    headers: { authorization: authorization! },
    data: { from: '2026-10-01', to: '2026-10-31', regenerate: true },
  })
  expect(post.status()).toBe(503)
  expect(post.headers()['content-type']).toContain('application/problem+json')
  expect(((await post.json()) as { type: string }).type).toBe('urn:runnersdiary:problem:feature-unavailable')
  const anonymous = await page.request.post('/api/insights/summary', { data: { from: '2026-10-01', to: '2026-10-31' } })
  expect(anonymous.status()).toBe(401)
})
