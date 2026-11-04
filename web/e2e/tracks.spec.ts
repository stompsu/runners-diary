import { expect, type Page } from '@playwright/test'
import { sampleGpx, typeId, type FakeApi, type RunWriteRequest } from '../src/test/fakeApi/index.ts'
import {
  mockApi,
  openDiary,
  recordWrites,
  restoreGuest,
  seriousViolations,
  seriousViolationsIn,
  stubMapNetwork,
  test,
} from './support.ts'

// Mocked journeys for GPX tracks (M4 steps 5-6, AC8/AC9): the in-memory fake API answers /api, real MapLibre GL runs in
// headless Chromium (WebGL2 via SwiftShader, see playwright.config.ts) with the OpenFreeMap style replaced by a local one.

const RECORDED = sampleGpx({ times: ['2026-11-14T07:30:00Z', '2026-11-14T08:19:30Z'] })
const gpx = (name: string, text = RECORDED) => ({ name, mimeType: 'application/gpx+xml', buffer: Buffer.from(text) })
const PLANNED_RUN: RunWriteRequest = {
  date: '2026-11-14',
  status: 'Planned',
  runTypeId: typeId('easy'),
  title: 'Long one',
}

async function start(page: Page, options: { seed?: RunWriteRequest[]; demoTracks?: boolean } = {}) {
  const fake = await mockApi(page, { demoTracks: options.demoTracks })
  const foreign = await stubMapNetwork(page)
  const token = await restoreGuest(page, fake, options.seed ? { seed: options.seed } : undefined)
  return { fake, token, foreign }
}

const raceOf = (fake: FakeApi, token: string) => fake.runsOf(token).find((run) => run.race)!
const map = (page: Page) => page.getByRole('region', { name: 'Map of the route' })

async function openDetails(page: Page, runId: string, title: string) {
  await openDiary(page, `/diary/2026/11/runs/${runId}`)
  const details = page.getByRole('dialog', { name: title })
  await expect(details).toBeVisible()
  await expect(details.getByRole('heading', { name: 'GPX files' })).toBeVisible()
  return details
}

test.describe('track view of a demo race', () => {
  test('opens by the real route: map with the line and markers, linked profile, text alternative, download', async ({
    page,
  }) => {
    const { fake, token, foreign } = await start(page, { demoTracks: true })
    const race = raceOf(fake, token)
    const details = await openDetails(page, race.id, 'Silesia Half')

    await details.getByRole('link', { name: 'View recorded track' }).click()
    await expect(page).toHaveURL(new RegExp(`/diary/2026/11/runs/${race.id}/tracks/[0-9a-f-]+$`))
    const viewer = page.getByRole('dialog', { name: 'Recorded track · gliwice-10k.gpx' })
    await expect(map(page)).toHaveAttribute('data-map-state', 'ready')
    await expect(map(page)).toHaveAttribute('data-track-vertices', '50')
    await expect(viewer.getByRole('img', { name: 'Start' })).toBeVisible()
    await expect(viewer.getByRole('img', { name: 'Finish' })).toBeVisible()
    // The map keeps its height inside the dialog (it must not collapse).
    const frame = (await map(page).boundingBox())!
    expect(frame.height).toBeGreaterThanOrEqual(240)
    // ... and the drawing surface fills it (maplibre-gl.css must not collapse the container).
    const canvas = (await map(page).locator('.maplibregl-canvas').boundingBox())!
    expect(Math.abs(canvas.height - frame.height)).toBeLessThan(4)
    expect(Math.abs(canvas.width - frame.width)).toBeLessThan(4)

    // Keyboard on the profile moves a marker on the map.
    const slider = viewer.getByRole('slider', { name: /^Elevation profile/ })
    await slider.focus()
    await expect(page.getByTestId('map-highlight')).toBeVisible()
    await slider.press('End')
    await expect(slider).toHaveValue('600')

    await expect(viewer.getByRole('table', { name: 'Kilometre splits' })).toBeVisible()
    await expect(viewer.getByText('10 km').first()).toBeVisible()
    expect(await seriousViolations(page)).toEqual([])

    const download = page.waitForEvent('download')
    await viewer.getByRole('button', { name: 'Download' }).click()
    expect((await download).suggestedFilename()).toBe('gliwice-10k.gpx')

    // Only the stubbed style host was contacted.
    expect([...new Set(foreign.map((url) => new URL(url).host))]).toEqual(['tiles.openfreemap.org'])

    await viewer.getByRole('button', { name: 'Close' }).click()
    await expect(page.getByRole('dialog', { name: 'Silesia Half' })).toBeVisible()
    await expect(page.getByRole('link', { name: 'View recorded track' })).toBeFocused()
  })

  test('a deep link opens the viewer directly; a track that does not exist says so', async ({ page }) => {
    const { fake, token } = await start(page, { demoTracks: true })
    const race = raceOf(fake, token)
    const [track] = fake.tracksOf(token, race.id)
    await page.goto(`/diary/2026/11/runs/${race.id}/tracks/${track!.id}`)
    await expect(map(page)).toHaveAttribute('data-map-state', 'ready')
    await page.getByRole('dialog').getByRole('button', { name: 'Close' }).click()
    await expect(page.getByRole('dialog', { name: 'Silesia Half' })).toBeVisible()

    await page.goto(`/diary/2026/11/runs/${race.id}/tracks/00000000-0000-4000-8000-00000000ffff`)
    await expect(page.getByText('This track no longer exists.')).toBeVisible()
  })

  test('a legacy file that was never processed shows a neutral state, not a broken map', async ({ page }) => {
    const { fake, token } = await start(page, { seed: [PLANNED_RUN] })
    const run = fake.runsOf(token)[0]!
    const pending = fake.addTrack(token, run.id, { kind: 'RecordedTrack', status: 'Pending', fileName: 'old.gpx' })
    const details = await openDetails(page, run.id, 'Long one')
    await expect(details.getByTestId('track-status')).toContainText('has not been processed')
    await expect(details.getByRole('link', { name: /^View/ })).toHaveCount(0)
    await page.goto(`/diary/2026/11/runs/${run.id}/tracks/${pending.id}`)
    await expect(page.getByText('has not been processed')).toBeVisible()
    await expect(map(page)).toHaveCount(0)
  })
})

test.describe('upload -> review -> edit -> save', () => {
  test('a recorded track on a planned run fills the form as edits and nothing is saved until Save', async ({
    page,
  }) => {
    const { fake, token } = await start(page, { seed: [PLANNED_RUN] })
    const writes = recordWrites(page)
    const run = fake.runsOf(token)[0]!
    const details = await openDetails(page, run.id, 'Long one')

    await details.getByRole('button', { name: 'Add GPX file' }).click()
    const upload = page.getByRole('dialog', { name: 'Add a GPX file' })
    await upload.getByRole('radio', { name: 'Recorded track' }).check()
    await upload.getByLabel('GPX file').setInputFiles(gpx('morning.gpx'))
    expect(await seriousViolations(page)).toEqual([])
    await upload.getByRole('button', { name: 'Upload' }).click()

    const review = page.getByRole('dialog', { name: 'Use values from the GPX file' })
    await expect(review).toBeVisible()
    for (const name of ['Mark as completed', 'Date and start time', 'Distance', 'Duration', 'Location']) {
      await expect(review.getByRole('checkbox', { name: new RegExp(`^${name}`) })).toBeChecked()
    }
    await expect(review.getByRole('radio', { name: 'Elapsed time 0:49:30' })).toBeChecked()
    expect(await seriousViolationsIn(page, '[data-panel="track-prefill"]')).toEqual([])

    await review.getByRole('button', { name: 'Apply to the run form' }).click()
    const form = page.getByRole('dialog', { name: 'Edit run' })
    await expect(form.getByTestId('prefill-notice')).toContainText('Values from morning.gpx')
    await expect(form.getByLabel('Distance (km)')).toHaveValue('10.005')
    await expect(form.getByLabel('Duration (h:mm:ss)')).toHaveValue('0:49:30')
    await expect(form.getByLabel('Start time (optional)')).toHaveValue('08:30')
    await expect(form.getByRole('radio', { name: 'Completed' })).toBeChecked()
    // Review only: no write to the run yet.
    expect(writes.filter((w) => w.method === 'PUT')).toHaveLength(0)
    expect(fake.runsOf(token)[0]).toMatchObject({ status: 'Planned', distanceMeters: null })

    await form.getByRole('button', { name: 'Save', exact: true }).click()
    await expect(page.getByRole('dialog', { name: 'Long one' })).toBeVisible()
    await expect(page.getByRole('dialog', { name: 'Long one' }).getByText('10.01 km')).toBeVisible()
    expect(fake.runsOf(token)[0]).toMatchObject({
      status: 'Completed',
      distanceMeters: 10005,
      durationSeconds: 2970,
      startTime: '08:30',
      location: { latitude: 50.294862, longitude: 18.676642 },
    })
    expect(writes.filter((w) => w.method === 'PUT')).toHaveLength(1)
  })

  test('a differing value stays unchecked and a date mismatch is warned about', async ({ page }) => {
    const { fake, token } = await start(page, {
      seed: [{ ...PLANNED_RUN, date: '2026-11-03', status: 'Completed', distanceMeters: 9500 }],
    })
    const run = fake.runsOf(token)[0]!
    const details = await openDetails(page, run.id, 'Long one')
    await details.getByRole('button', { name: 'Add GPX file' }).click()
    const upload = page.getByRole('dialog', { name: 'Add a GPX file' })
    await upload.getByLabel('GPX file').setInputFiles(gpx('morning.gpx'))
    await upload.getByRole('button', { name: 'Upload' }).click()

    const review = page.getByRole('dialog', { name: 'Use values from the GPX file' })
    await expect(review.getByRole('checkbox', { name: /^Distance/ })).not.toBeChecked()
    await expect(review.getByRole('checkbox', { name: /^Date and start time/ })).not.toBeChecked()
    await expect(review.getByText(/was recorded on Saturday, November 14, 2026/)).toBeVisible()
  })

  test('replace asks first, then swaps the file', async ({ page }) => {
    const { fake, token } = await start(page, { seed: [{ ...PLANNED_RUN, status: 'Completed' }] })
    const run = fake.runsOf(token)[0]!
    fake.addTrack(token, run.id, { kind: 'RecordedTrack', fileName: 'old.gpx' })
    const details = await openDetails(page, run.id, 'Long one')
    await expect(details.getByText('old.gpx')).toBeVisible()

    await details.getByRole('button', { name: 'Replace recorded track' }).click()
    const upload = page.getByRole('dialog', { name: 'Add a GPX file' })
    await upload.getByLabel('GPX file').setInputFiles(gpx('new.gpx'))
    await upload.getByRole('button', { name: 'Replace file' }).click()
    const confirm = page.getByRole('alertdialog', { name: 'Replace the existing file?' })
    await expect(confirm).toContainText('"old.gpx"')
    expect(fake.tracksOf(token, run.id).map((t) => t.originalFileName)).toEqual(['old.gpx'])
    await confirm.getByRole('button', { name: 'Replace file' }).click()

    // The review of the new file opens; close it, the list shows the new file only.
    const review = page.getByRole('dialog', { name: 'Use values from the GPX file' })
    await expect(review).toBeVisible()
    await review.getByRole('button', { name: 'Cancel' }).click()
    await expect(details.getByText('new.gpx')).toBeVisible()
    await expect(details.getByText('old.gpx')).toHaveCount(0)
    expect(fake.tracksOf(token, run.id).map((t) => t.originalFileName)).toEqual(['new.gpx'])
  })

  test('delete asks first, removes only the file', async ({ page }) => {
    const { fake, token } = await start(page, { demoTracks: true })
    const race = raceOf(fake, token)
    const details = await openDetails(page, race.id, 'Silesia Half')

    await details.getByRole('button', { name: 'Delete recorded track' }).click()
    const confirm = page.getByRole('alertdialog', { name: 'Delete this GPX file?' })
    await expect(confirm).toContainText('"gliwice-10k.gpx"')
    await confirm.getByRole('button', { name: 'Cancel' }).click()
    expect(fake.tracksOf(token, race.id)).toHaveLength(1)

    await details.getByRole('button', { name: 'Delete recorded track' }).click()
    await page.getByRole('alertdialog').getByRole('button', { name: 'Delete' }).click()
    await expect(details.getByText('gliwice-10k.gpx')).toHaveCount(0)
    await expect(details.getByText(/No GPX file yet/)).toBeVisible()
    expect(fake.tracksOf(token, race.id)).toHaveLength(0)
    expect(fake.runsOf(token).some((run) => run.id === race.id)).toBe(true)
  })

  test('problems are explained in words and the dialog stays open', async ({ page }) => {
    const { fake, token } = await start(page, { seed: [{ ...PLANNED_RUN, status: 'Completed' }] })
    const run = fake.runsOf(token)[0]!
    const details = await openDetails(page, run.id, 'Long one')
    await details.getByRole('button', { name: 'Add GPX file' }).click()
    const upload = page.getByRole('dialog', { name: 'Add a GPX file' })

    await upload
      .getByLabel('GPX file')
      .setInputFiles({ name: 'notes.txt', mimeType: 'text/plain', buffer: Buffer.from('x') })
    await expect(upload.getByRole('alert')).toContainText('Choose a file with the .gpx extension.')
    await expect(upload.getByRole('button', { name: 'Upload' })).toBeDisabled()

    const dtd = '<?xml version="1.0"?><!DOCTYPE gpx [<!ENTITY x "y">]><gpx></gpx>'
    await upload.getByLabel('GPX file').setInputFiles(gpx('evil.gpx', dtd))
    await upload.getByRole('button', { name: 'Upload' }).click()
    await expect(upload.getByRole('alert')).toContainText('document type declaration')
    expect(fake.tracksOf(token, run.id)).toHaveLength(0)

    await upload.getByLabel('GPX file').setInputFiles(gpx('good.gpx'))
    await upload.getByRole('button', { name: 'Upload' }).click()
    await expect(page.getByRole('dialog', { name: 'Use values from the GPX file' })).toBeVisible()
  })
})
