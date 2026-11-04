import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import { expect, type Page } from '@playwright/test'
// Same test with the CSP guard (fails on Content-Security-Policy violations when the web image serves the page)
import { test } from '../e2e/support.ts'

// Full-stack GPX journeys (M4 step 7): nothing is mocked except the map STYLE/tiles host (CI must not depend on an
// external tile server, ADR 0022). Multipart upload, 201 + Location, 400 invalid-gpx and the localized messages run
// against the real API, SQL and Azurite.

const ROOT = join(import.meta.dirname, '..', '..')
const DEMO_10K = join(ROOT, 'seed', 'demo', 'gpx', 'gliwice-10k.gpx')
const XXE_FILE = join(ROOT, 'tests', 'fixtures', 'gpx', 'xxe-file.gpx')

// The app's own origin is whatever the stack is served on (localhost:5173 via the AppHost, 127.0.0.1:4173 in CI).
const APP_HOST = new URL(process.env.FULLSTACK_BASE_URL ?? 'http://localhost:5173').host

async function stubTiles(page: Page): Promise<string[]> {
  const foreign: string[] = []
  await page.route('https://tiles.openfreemap.org/**', (route) =>
    route.fulfill({
      path: join(import.meta.dirname, '..', 'e2e', 'fixtures', 'map-style.json'),
      contentType: 'application/json',
    }),
  )
  page.on('request', (request) => {
    const { host, protocol } = new URL(request.url())
    if (protocol.startsWith('http') && host !== APP_HOST) foreign.push(request.url())
  })
  return foreign
}

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

/** Creates a Planned run (title only) through the UI and opens its details dialog from the month list. */
async function createRunAndOpenDetails(page: Page, title: string) {
  await page.getByRole('link', { name: 'Add run', exact: true }).click()
  const form = page.getByRole('dialog', { name: 'Add run' })
  await form.getByLabel('Title').fill(title)
  await form.getByRole('button', { name: 'Save', exact: true }).click()
  await expect(page.getByRole('dialog')).toHaveCount(0)
  await page.getByRole('link', { name: 'Month list' }).click()
  const list = page.getByRole('dialog', { name: /^Runs in / })
  await list.getByRole('listitem').filter({ hasText: title }).getByRole('link', { name: 'View details' }).click()
  const details = page.getByRole('dialog', { name: new RegExp(title) })
  await expect(details.getByRole('heading', { name: 'GPX files' })).toBeVisible()
  return details
}

async function pickFile(
  page: Page,
  details: ReturnType<Page['getByRole']>,
  file: string | { name: string; mimeType: string; buffer: Buffer },
) {
  await details.getByRole('button', { name: 'Add GPX file' }).click()
  const upload = page.getByRole('dialog', { name: 'Add a GPX file' })
  await upload.getByRole('radio', { name: 'Recorded track' }).check()
  await upload.getByLabel('GPX file').setInputFiles(file)
  return upload
}

test('guest: upload a recorded demo GPX -> prefill review -> apply -> save -> track view -> delete run', async ({
  page,
}) => {
  const title = `E2E GPX ${Date.now()}`
  const foreign = await stubTiles(page)
  await tryAsGuest(page)
  const details = await createRunAndOpenDetails(page, title)

  const upload = await pickFile(page, details, DEMO_10K)
  const uploadResponse = page.waitForResponse(
    (r) => /\/api\/runs\/[^/]+\/tracks$/.test(r.url()) && r.request().method() === 'POST',
  )
  await upload.getByRole('button', { name: 'Upload' }).click()
  const response = await uploadResponse
  // Real multipart through fetchClient: boundary set by the browser, 201 + Location.
  expect(response.request().headers()['content-type']).toMatch(/^multipart\/form-data; boundary=/)
  expect(response.status()).toBe(201)
  expect(response.headers()['location']).toMatch(/\/api\/runs\/[^/]+\/tracks\/[^/]+$/)

  const review = page.getByRole('dialog', { name: 'Use values from the GPX file' })
  await expect(review).toBeVisible()
  await expect(review.getByRole('checkbox', { name: /^Distance/ })).toBeChecked()
  await expect(review.getByRole('checkbox', { name: /^Mark as completed/ })).toBeChecked()
  await review.getByRole('button', { name: 'Apply to the run form' }).click()

  const form = page.getByRole('dialog', { name: 'Edit run' })
  await expect(form.getByTestId('prefill-notice')).toContainText('gliwice-10k.gpx')
  await expect(form.getByLabel('Distance (km)')).toHaveValue(/^10\./)
  await expect(form.getByRole('radio', { name: 'Completed' })).toBeChecked()
  await form.getByRole('button', { name: 'Save', exact: true }).click()

  const saved = page.getByRole('dialog', { name: new RegExp(title) })
  await expect(saved.getByText(/^10\.\d+ km/).first()).toBeVisible()

  // Track view against the real GeoJSON endpoint, real MapLibre, stubbed style.
  await saved.getByRole('link', { name: 'View recorded track' }).click()
  await expect(page).toHaveURL(/\/runs\/[0-9a-f-]+\/tracks\/[0-9a-f-]+$/)
  const map = page.getByRole('region', { name: 'Map of the route' })
  await expect(map).toHaveAttribute('data-map-state', 'ready')
  expect(Number(await map.getAttribute('data-track-vertices'))).toBeGreaterThan(10)
  await expect(page.getByRole('slider', { name: /^Elevation profile/ })).toBeVisible()
  await expect(page.getByRole('table', { name: 'Kilometre splits' })).toBeAttached()
  const trackUrl = page.url()

  // The real GPX download (attachment) works through the blob path.
  const download = page.waitForEvent('download')
  await page.getByRole('dialog').getByRole('button', { name: 'Download' }).click()
  expect((await download).suggestedFilename()).toBe('gliwice-10k.gpx')
  expect([...new Set(foreign.map((url) => new URL(url).host))].filter((h) => h !== 'tiles.openfreemap.org')).toEqual([])

  // Delete the run (blobs are removed server-side first); the track URL is gone afterwards.
  await page.getByRole('dialog').getByRole('button', { name: 'Close' }).click()
  await expect(saved).toBeVisible()
  await saved.getByRole('button', { name: 'Delete', exact: true }).click()
  const confirmDelete = page.getByRole('alertdialog', { name: 'Delete this run?' })
  await confirmDelete.getByRole('button', { name: 'Delete' }).click()
  await expect(confirmDelete).toBeHidden() // closes only after the API answered 204
  await expect(page.getByText(title)).toHaveCount(0)
  await page.goto(trackUrl)
  await expect(page.getByRole('dialog', { name: 'Run not found' })).toBeVisible()
  // The API agrees: track, GeoJSON and GPX of the deleted run are 404.
  const token = await page.evaluate(
    () => (JSON.parse(localStorage.getItem('rd.guestSession')!) as { accessToken: string }).accessToken,
  )
  const base = new URL(trackUrl).pathname.replace(/^.*\/runs\//, '/api/runs/')
  for (const suffix of ['', '/geojson', '/gpx']) {
    const res = await page.request.get(base + suffix, { headers: { Authorization: `Bearer ${token}` } })
    expect(res.status(), suffix).toBe(404)
  }
})

test('guest: a file over the 5 MB guest limit gets the localized "too large" message', async ({ page }) => {
  await tryAsGuest(page)
  const details = await createRunAndOpenDetails(page, `E2E big ${Date.now()}`)
  const big = Buffer.concat([readFileSync(DEMO_10K), Buffer.alloc(6 * 1024 * 1024, 0x20)])
  const upload = await pickFile(page, details, { name: 'big.gpx', mimeType: 'application/gpx+xml', buffer: big })
  // The client pre-check answers on selection (Upload stays disabled); the server's 413 is covered by Api.Tests and curl.
  await expect(upload.getByRole('button', { name: 'Upload' })).toBeDisabled()
  await expect(upload.getByText('The file is too large. The limit is 5 MB.')).toBeVisible()
})

test('guest: an XXE payload is rejected with the localized invalid-gpx message and leaves no track', async ({
  page,
}) => {
  await tryAsGuest(page)
  const details = await createRunAndOpenDetails(page, `E2E xxe ${Date.now()}`)
  const upload = await pickFile(page, details, XXE_FILE)
  const rejected = page.waitForResponse(
    (r) => /\/api\/runs\/[^/]+\/tracks$/.test(r.url()) && r.request().method() === 'POST',
  )
  await upload.getByRole('button', { name: 'Upload' }).click()
  const response = await rejected
  expect(response.status()).toBe(400)
  const body = (await response.json()) as { code?: string; type?: string }
  expect(body.type).toBe('urn:runnersdiary:problem:invalid-gpx')
  expect(body.code).toBe('dtdProhibited')
  await expect(
    upload.getByText('The file contains a document type declaration, which is not allowed for security reasons.'),
  ).toBeVisible()
  // No raw server text and no secrets/file content on the page.
  await expect(page.locator('body')).not.toContainText('root:')
  await upload.getByRole('button', { name: 'Cancel' }).click()
  await expect(details.getByText('No GPX file yet.')).toBeVisible()
})
