import { expect, type Page } from '@playwright/test'
import { seriousViolations, stubMapNetwork, test } from './support.ts'

// The track viewer on a test-only page (served by the Vite dev server, never built): real MapLibre GL JS 6 in headless
// Chromium (WebGL2 via SwiftShader, see playwright.config.ts), with the OpenFreeMap style replaced by a local one.
const HARNESS = 'http://localhost:4174/e2e/harness/map.html'

async function openViewer(page: Page, query = '') {
  await page.goto(`${HARNESS}${query}`)
}

const map = (page: Page) => page.getByRole('region', { name: 'Map of the route' })
const slider = (page: Page) => page.getByRole('slider', { name: /^Elevation profile/ })

test('renders the real map with the track, markers, attribution and no foreign requests', async ({ page }) => {
  const foreign = await stubMapNetwork(page)
  await openViewer(page)

  await expect(map(page)).toHaveAttribute('data-map-state', 'ready')
  await expect(map(page)).toHaveAttribute('data-track-vertices', '50')
  await expect(page.getByRole('img', { name: 'Start' })).toBeVisible()
  await expect(page.getByRole('img', { name: 'Finish' })).toBeVisible()
  await expect(map(page).locator('.maplibregl-canvas')).toBeVisible()
  await expect(map(page).locator('.maplibregl-ctrl-attrib')).toContainText('Test map data')
  await expect(page.getByRole('button', { name: 'Zoom in' })).toBeVisible()
  // Only the (stubbed) style host was contacted: no tiles, no fonts, nothing else.
  expect([...new Set(foreign.map((url) => new URL(url).host))]).toEqual(['tiles.openfreemap.org'])

  // The map is actually drawn: a composited screenshot of the map has line-blue pixels. (Reading the WebGL canvas
  // directly is unreliable: without preserveDrawingBuffer it is blank outside the frame that rendered it.) Software
  // WebGL under load can take a few frames to paint the first one, so poll with a fresh screenshot each time.
  await expect
    .poll(async () => blueLinePixels(page, (await map(page).screenshot()).toString('base64')), {
      message: 'line-blue pixels in the composited map',
      timeout: 15_000,
    })
    .toBeGreaterThan(50)
})

async function blueLinePixels(page: Page, base64: string): Promise<number> {
  return page.evaluate(async (data) => {
    const image = new Image()
    image.src = `data:image/png;base64,${data}`
    await image.decode()
    const copy = document.createElement('canvas')
    copy.width = image.naturalWidth
    copy.height = image.naturalHeight
    const context = copy.getContext('2d')!
    context.drawImage(image, 0, 0)
    const { data: pixels } = context.getImageData(0, 0, copy.width, copy.height)
    let blue = 0
    for (let i = 0; i < pixels.length; i += 4) if (pixels[i + 2]! > 150 && pixels[i]! < 80) blue++
    return blue
  }, base64)
}

test('keyboard on the elevation profile moves a marker on the map', async ({ page }) => {
  await stubMapNetwork(page)
  await openViewer(page)
  await expect(map(page)).toHaveAttribute('data-map-state', 'ready')

  const highlight = page.getByTestId('map-highlight')
  await expect(highlight).toHaveCount(0)

  await slider(page).focus()
  await expect(highlight).toBeVisible()
  const start = await highlight.boundingBox()

  await slider(page).press('End')
  await expect(slider(page)).toHaveValue('600')
  await expect(page.getByText(/^10\.0\d? km · /)).toBeVisible()

  await slider(page).press('PageDown')
  await slider(page).press('ArrowLeft')
  const moved = await highlight.boundingBox()
  expect(moved).not.toBeNull()
  expect(moved).not.toEqual(start)

  await slider(page).press('Escape')
  await expect(highlight).toHaveCount(0)
})

test('pointer on the elevation profile moves the marker and leaving clears it', async ({ page }) => {
  await stubMapNetwork(page)
  await openViewer(page)
  await expect(map(page)).toHaveAttribute('data-map-state', 'ready')

  const box = (await slider(page).boundingBox())!
  await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2)
  await expect(page.getByTestId('map-highlight')).toBeVisible()
  // The middle of the plot area is roughly the middle of the track (4-6 km of 10 km).
  const text = await page.getByText(/ km · \d+ m$/).textContent()
  const km = Number.parseFloat(text ?? '0')
  expect(km).toBeGreaterThan(4)
  expect(km).toBeLessThan(6)

  await page.mouse.move(box.x + box.width / 2, box.y - 200)
  await expect(page.getByTestId('map-highlight')).toHaveCount(0)
})

test('the text alternative is complete: summary, splits table, warnings', async ({ page }) => {
  await stubMapNetwork(page)
  await openViewer(page)

  await expect(page.getByText('10 km')).toBeVisible()
  await expect(page.getByText('49:30', { exact: false }).first()).toBeVisible()
  await expect(page.getByRole('table', { name: 'Kilometre splits' })).toBeVisible()
  await expect(page.getByRole('row')).toHaveCount(12) // header + 10 full splits + the partial one
})

test('an untimed route without elevation has no profile and says why', async ({ page }) => {
  await stubMapNetwork(page)
  await openViewer(page, '?fixture=bare&planned=1')
  await expect(map(page)).toHaveAttribute('data-map-state', 'ready')
  await expect(slider(page)).toHaveCount(0)
  await expect(page.getByText('The file has no elevation data', { exact: false })).toBeVisible()
  await expect(page.getByText('Not available').first()).toBeVisible()
})

test('without WebGL2 the map is replaced by an accessible panel and the text stays', async ({ page }) => {
  await stubMapNetwork(page)
  await page.addInitScript(() => {
    const original = HTMLCanvasElement.prototype.getContext
    HTMLCanvasElement.prototype.getContext = function (this: HTMLCanvasElement, type: string, ...rest: unknown[]) {
      return type === 'webgl2' ? null : (original as (...args: unknown[]) => unknown).call(this, type, ...rest)
    } as typeof original
  })
  await openViewer(page)

  await expect(map(page)).toHaveAttribute('data-map-state', 'unavailable')
  await expect(map(page).getByRole('status')).toContainText('Map unavailable')
  await expect(page.locator('.maplibregl-canvas')).toHaveCount(0)
  await expect(page.getByRole('table', { name: 'Kilometre splits' })).toBeVisible()
  expect(await seriousViolations(page)).toEqual([])
})

test('a style that cannot be loaded shows the same panel instead of a blank map', async ({ page }) => {
  await stubMapNetwork(page, { styleStatus: 503 })
  await openViewer(page)
  await expect(map(page)).toHaveAttribute('data-map-state', 'error')
  await expect(map(page).getByRole('status')).toContainText('could not be loaded')
})

test('has no serious or critical accessibility violations', async ({ page }) => {
  await stubMapNetwork(page)
  await openViewer(page)
  await expect(map(page)).toHaveAttribute('data-map-state', 'ready')
  await slider(page).focus()
  expect(await seriousViolations(page)).toEqual([])
})
