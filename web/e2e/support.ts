import { join } from 'node:path'
import { AxeBuilder } from '@axe-core/playwright'
import { expect, test as base, type Page } from '@playwright/test'
import {
  createFakeApi,
  DEMO_REPORT_TODAY,
  type FakeApi,
  type FakeApiOptions,
  type RunWriteRequest,
  type SummaryPostOutcome,
  type SummaryScenario,
} from '../src/test/fakeApi/index.ts'

interface CspWindow {
  rdCspViolations?: string[]
}

/**
 * `test` with a CSP guard (ADR 0029, plan M7 AC3): every test fails if the page reports a Content-Security-Policy
 * violation, either as a `securitypolicyviolation` event or as the browser's "Refused to ..." console error.
 * Against `vite preview` (no CSP header) nothing can fire, so the guard is a no-op there; against the nginx image
 * (PLAYWRIGHT_BASE_URL) it proves that the shipped policy does not break the app.
 */
export const test = base.extend<{ cspGuard: void }>({
  cspGuard: [
    async ({ page }, use) => {
      await page.addInitScript(() => {
        const store: string[] = []
        ;(window as CspWindow).rdCspViolations = store
        document.addEventListener('securitypolicyviolation', (event) => {
          store.push(
            `${event.violatedDirective} blocked ${event.blockedURI || 'inline'} (${event.sourceFile}:${event.lineNumber})`,
          )
        })
      })
      const reported: string[] = []
      page.on('console', (message) => {
        if (
          message.type() === 'error' &&
          /Content Security Policy|Refused to (load|connect|apply|execute|create)/i.test(message.text())
        ) {
          reported.push(message.text())
        }
      })
      await use()
      // The page may already be closed or mid-navigation; the console listener above still has what it saw.
      const inPage = await page.evaluate(() => (window as CspWindow).rdCspViolations ?? []).catch(() => [])
      expect([...reported, ...inPage], 'Content-Security-Policy violations').toEqual([])
    },
    { auto: true },
  ],
})

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
    // A multipart track upload has no JSON body.
    let body: RecordedWrite['body'] = null
    try {
      body = request.postDataJSON() as RecordedWrite['body']
    } catch {
      body = null
    }
    writes.push({ method: request.method(), path: url.pathname, body })
  })
  return writes
}

export async function seriousViolations(page: Page): Promise<string[]> {
  const { violations } = await new AxeBuilder({ page }).analyze()
  return violations.filter((v) => v.impact === 'serious' || v.impact === 'critical').map((v) => `${v.id}: ${v.help}`)
}

/** Waits for running CSS animations (a dialog fading in is semi-transparent and fails the contrast check). */
export async function settled(page: Page): Promise<void> {
  await page.waitForFunction(() => document.getAnimations().every((animation) => animation.playState !== 'running'))
}

/** Like `seriousViolations`, limited to one subtree (e.g. a dialog, to keep a fading toast out of the check). */
export async function seriousViolationsIn(page: Page, selector: string): Promise<string[]> {
  await settled(page)
  const { violations } = await new AxeBuilder({ page }).include(selector).analyze()
  return violations.filter((v) => v.impact === 'serious' || v.impact === 'critical').map((v) => `${v.id}: ${v.help}`)
}

export async function horizontalOverflow(page: Page): Promise<number> {
  return page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)
}

/**
 * Serves the local, tile-less test style in place of the OpenFreeMap style (ADR 0022: tests never use the network) and
 * fails the test on any other request to a foreign host. Returns the list of foreign hosts seen (for assertions).
 */
export async function stubMapNetwork(page: Page, options: { styleStatus?: number } = {}): Promise<string[]> {
  const foreign: string[] = []
  await page.route('https://tiles.openfreemap.org/**', (route) =>
    options.styleStatus && options.styleStatus !== 200
      ? route.fulfill({ status: options.styleStatus, body: 'unavailable' })
      : route.fulfill({
          path: join(import.meta.dirname, 'fixtures', 'map-style.json'),
          contentType: 'application/json',
        }),
  )
  page.on('request', (request) => {
    const { hostname, protocol } = new URL(request.url())
    if (protocol.startsWith('http') && hostname !== 'localhost') foreign.push(request.url())
  })
  return foreign
}

/** Opens `/insights` on the demo report with the summary endpoints in `scenario`; returns the summary card. */
export async function openSummary(
  page: Page,
  scenario: SummaryScenario,
  options: { post?: SummaryPostOutcome; slowMs?: number } = {},
) {
  const fake = await mockApi(page, { today: DEMO_REPORT_TODAY, summary: { scenario, ...options } })
  await restoreGuest(page, fake)
  await page.goto('/insights')
  await expect(page.getByRole('heading', { level: 1, name: 'Insights' })).toBeVisible()
  const card = page.getByTestId('summary-card')
  await expect(card).toBeVisible()
  return { fake, card }
}
