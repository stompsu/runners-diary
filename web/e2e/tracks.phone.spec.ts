import { expect, test, type Page } from '@playwright/test'
import { sampleGpx, typeId, type RunWriteRequest } from '../src/test/fakeApi/index.ts'
import {
  horizontalOverflow,
  mockApi,
  openDiary,
  restoreGuest,
  seriousViolationsIn,
  settled,
  stubMapNetwork,
} from './support.ts'

// Runs only in the `phone-la` project: 360x640, touch, browser zone Los Angeles (plan AC15). Track view, upload,
// review and picker are bottom sheets that fit the screen, with a map of at least 240 px.

const RECORDED = sampleGpx({ times: ['2026-11-14T07:30:00Z', '2026-11-14T08:19:30Z'] })
const COMPLETED: RunWriteRequest = {
  date: '2026-11-14',
  status: 'Completed',
  runTypeId: typeId('easy'),
  title: 'Long one',
}

async function start(page: Page, options: { demoTracks?: boolean; seed?: RunWriteRequest[] } = {}) {
  const fake = await mockApi(page, { demoTracks: options.demoTracks })
  await stubMapNetwork(page)
  const token = await restoreGuest(page, fake, options.seed ? { seed: options.seed } : undefined)
  return { fake, token }
}

const fitsTheScreen = async (page: Page, dialogName: string) => {
  const dialog = page.getByRole('dialog', { name: dialogName })
  await expect(dialog).toHaveAttribute('data-variant', 'sheet')
  await settled(page)
  expect(await horizontalOverflow(page)).toBeLessThanOrEqual(0)
  expect(await dialog.evaluate((el) => el.scrollWidth - el.clientWidth)).toBeLessThanOrEqual(0)
  const box = (await dialog.boundingBox())!
  expect(box.x).toBeGreaterThanOrEqual(0)
  expect(box.x + box.width).toBeLessThanOrEqual(360.5)
  // A sheet never grows beyond 92% of the screen (it scrolls inside instead).
  expect(box.height).toBeLessThanOrEqual(640 * 0.92 + 1)
  return dialog
}

test('the details list the file with large tap targets, and the track view is a sheet with a 240 px map', async ({
  page,
}) => {
  const { fake, token } = await start(page, { demoTracks: true })
  const race = fake.runsOf(token).find((run) => run.race)!
  await openDiary(page, `/diary/2026/11/runs/${race.id}`)
  const details = await fitsTheScreen(page, 'Silesia Half')
  for (const name of [
    'View recorded track',
    'Download recorded track',
    'Replace recorded track',
    'Delete recorded track',
  ]) {
    const box = (await details.getByRole(name.startsWith('View') ? 'link' : 'button', { name }).boundingBox())!
    expect(box.height, name).toBeGreaterThanOrEqual(43.5)
  }
  expect(await seriousViolationsIn(page, '[data-panel="details"]')).toEqual([])

  await details.getByRole('link', { name: 'View recorded track' }).click()
  const viewer = await fitsTheScreen(page, 'Recorded track · gliwice-10k.gpx')
  const map = page.getByRole('region', { name: 'Map of the route' })
  await expect(map).toHaveAttribute('data-map-state', 'ready')
  expect((await map.boundingBox())!.height).toBeGreaterThanOrEqual(240)
  await expect(viewer.getByRole('slider', { name: /^Elevation profile/ })).toBeVisible()
  // The profile is readable (at least 100 px tall) and the splits are collapsed until asked for.
  expect(
    (await viewer.getByRole('slider', { name: /^Elevation profile/ }).boundingBox())!.height,
  ).toBeGreaterThanOrEqual(100)
  await expect(viewer.getByText('Show splits table').locator('xpath=ancestor::details')).not.toHaveAttribute('open', '')
  expect(await seriousViolationsIn(page, '[data-panel="track-view"]')).toEqual([])
})

test('upload and review dialogs are sheets that fit; Apply opens the form with the values', async ({ page }) => {
  const { fake, token } = await start(page, { seed: [COMPLETED] })
  const run = fake.runsOf(token)[0]!
  await openDiary(page, `/diary/2026/11/runs/${run.id}`)
  const details = page.getByRole('dialog', { name: 'Long one' })
  await details.getByRole('button', { name: 'Add GPX file' }).tap()

  const upload = await fitsTheScreen(page, 'Add a GPX file')
  expect(await seriousViolationsIn(page, '[data-panel="track-upload"]')).toEqual([])
  const choose = upload.getByLabel('GPX file')
  await choose.setInputFiles({ name: 'morning.gpx', mimeType: 'application/gpx+xml', buffer: Buffer.from(RECORDED) })
  const uploadButton = upload.getByRole('button', { name: 'Upload' })
  expect((await uploadButton.boundingBox())!.height).toBeGreaterThanOrEqual(43.5)
  await uploadButton.tap()

  const review = await fitsTheScreen(page, 'Use values from the GPX file')
  expect(await seriousViolationsIn(page, '[data-panel="track-prefill"]')).toEqual([])
  await review.getByRole('button', { name: 'Apply to the run form' }).scrollIntoViewIfNeeded()
  await review.getByRole('button', { name: 'Apply to the run form' }).tap()
  const form = page.getByRole('dialog', { name: 'Edit run' })
  await expect(form.getByLabel('Distance (km)')).toHaveValue('10.005')
  expect(await horizontalOverflow(page)).toBeLessThanOrEqual(0)
})

test('the location picker is a sheet with a 240 px map and reachable coordinate fields', async ({ page }) => {
  await start(page)
  await openDiary(page, '/diary/2026/11/runs/new?date=2026-11-20')
  const form = page.getByRole('dialog', { name: 'Add run' })
  await form.getByRole('button', { name: 'Pick on map' }).scrollIntoViewIfNeeded()
  await form.getByRole('button', { name: 'Pick on map' }).tap()

  const picker = await fitsTheScreen(page, 'Choose a location')
  const map = page.getByRole('region', { name: 'Map for choosing a location' })
  await expect(map).toHaveAttribute('data-map-state', 'ready')
  expect((await map.boundingBox())!.height).toBeGreaterThanOrEqual(240)
  await picker.getByLabel('Latitude').scrollIntoViewIfNeeded()
  expect((await picker.getByLabel('Latitude').boundingBox())!.height).toBeGreaterThanOrEqual(43.5)
  expect(await seriousViolationsIn(page, '[data-panel="location-picker"]')).toEqual([])
  expect(await horizontalOverflow(page)).toBeLessThanOrEqual(0)
})
