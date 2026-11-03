import { AxeBuilder } from '@axe-core/playwright'
import { expect, type Page } from '@playwright/test'
import { createFakeApi, type FakeApi, type FakeApiOptions, type RunWriteRequest } from '../src/test/fakeApi/index.ts'

/** The fixed "today" of every mocked journey (also `me.today`). */
export const TODAY = '2026-11-03'

/** Installs the in-memory fake API (ADR 0019) as the answer to every `/api/**` call of the page. */
export async function mockApi(page: Page, options: FakeApiOptions = {}): Promise<FakeApi> {
  const fake = createFakeApi({ today: TODAY, ...options })
  await page.route('**/api/**', (route) => fake.fulfillRoute(route))
  return fake
}

/** Starts the page with a restored guest session (ADR 0014 §4) backed by a fresh fake account. */
export async function restoreGuest(
  page: Page,
  fake: FakeApi,
  options: { seed?: RunWriteRequest[]; expiresInMs?: number } = {},
): Promise<string> {
  const token = fake.issueToken('Guest', options.seed ? { seed: options.seed } : undefined)
  await page.addInitScript(
    ({ accessToken, ttl }) =>
      localStorage.setItem(
        'rd.guestSession',
        JSON.stringify({ accessToken, expiresAt: Date.now() + ttl, userId: 'e2e' }),
      ),
    { accessToken: token, ttl: options.expiresInMs ?? 24 * 60 * 60 * 1000 },
  )
  return token
}

/** Opens a diary URL and waits for the grid. `includeHidden`: a deep-linked panel is modal, the grid is aria-hidden. */
export async function openDiary(page: Page, path: string): Promise<void> {
  await page.goto(path)
  await expect(page.getByRole('grid', { name: /^Calendar for /, includeHidden: true })).toBeVisible()
}

export const cell = (page: Page, date: string) => page.locator(`[role="gridcell"][data-date="${date}"]`)

export interface RecordedWrite {
  method: string
  path: string
  body: Record<string, unknown> | null
}

/** Records the mutating `/api/runs` requests the browser sends (bodies included). */
export function recordWrites(page: Page): RecordedWrite[] {
  const writes: RecordedWrite[] = []
  page.on('request', (request) => {
    const url = new URL(request.url())
    if (!url.pathname.startsWith('/api/runs') || request.method() === 'GET') return
    writes.push({ method: request.method(), path: url.pathname, body: request.postDataJSON() as RecordedWrite['body'] })
  })
  return writes
}

export async function seriousViolations(page: Page): Promise<string[]> {
  const { violations } = await new AxeBuilder({ page }).analyze()
  return violations.filter((v) => v.impact === 'serious' || v.impact === 'critical').map((v) => `${v.id}: ${v.help}`)
}

export async function horizontalOverflow(page: Page): Promise<number> {
  return page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)
}
