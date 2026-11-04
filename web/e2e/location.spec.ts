import { expect, test, type Page } from '@playwright/test'
import { typeId, type RunWriteRequest } from '../src/test/fakeApi/index.ts'
import { mockApi, openDiary, restoreGuest, seriousViolations, stubMapNetwork } from './support.ts'

// Location picker and place search (M4 step 6, AC10): real MapLibre with the stubbed style, mocked API.

const WITH_PLACE: RunWriteRequest = {
  date: '2026-11-14',
  status: 'Completed',
  runTypeId: typeId('easy'),
  title: 'Long one',
  location: { latitude: 50.294862123, longitude: 18.676642987, name: 'Gliwice' },
}

async function start(page: Page, options: { geocoding?: boolean; seed?: RunWriteRequest[] } = {}) {
  const fake = await mockApi(page, { geocodingEnabled: options.geocoding })
  await stubMapNetwork(page)
  const token = await restoreGuest(page, fake, options.seed ? { seed: options.seed } : undefined)
  return { fake, token }
}

async function openNew(page: Page) {
  await openDiary(page, '/diary/2026/11/runs/new?date=2026-11-20')
  const form = page.getByRole('dialog', { name: 'Add run' })
  await expect(form.getByLabel('Title')).toBeVisible()
  return form
}

async function openEdit(page: Page, runId: string) {
  await openDiary(page, `/diary/2026/11/runs/${runId}/edit`)
  const form = page.getByRole('dialog', { name: 'Edit run' })
  await expect(form.getByLabel('Title')).toBeVisible()
  return form
}

const pickerMap = (page: Page) => page.getByRole('region', { name: 'Map for choosing a location' })

test('click on the map places the pin and fills the coordinates; the exact numbers are saved with the run', async ({
  page,
}) => {
  const { fake, token } = await start(page)
  const form = await openNew(page)
  await form.getByRole('button', { name: 'Pick on map' }).click()
  const picker = page.getByRole('dialog', { name: 'Choose a location' })
  await expect(pickerMap(page)).toHaveAttribute('data-map-state', 'ready')
  expect(await seriousViolations(page)).toEqual([])
  const frame = (await pickerMap(page).boundingBox())!
  expect(frame.height).toBeGreaterThanOrEqual(240)
  // The drawing surface fills the frame (a container collapsed by a CSS clash would leave a 300 px canvas clipped away).
  const canvas = (await pickerMap(page).locator('.maplibregl-canvas').boundingBox())!
  expect(Math.abs(canvas.height - frame.height)).toBeLessThan(4)
  await expect(picker.getByRole('button', { name: 'Use this location' })).toBeDisabled()

  await pickerMap(page).locator('.maplibregl-canvas').click()
  await expect(page.getByTestId('map-marker')).toBeVisible()
  const latitude = await picker.getByLabel('Latitude').inputValue()
  const longitude = await picker.getByLabel('Longitude').inputValue()
  expect(Number(latitude)).toBeGreaterThan(50.2)
  expect(Number(latitude)).toBeLessThan(50.4)
  expect(Number(longitude)).toBeGreaterThan(18.5)
  expect(Number(longitude)).toBeLessThan(18.8)

  await picker.getByRole('button', { name: 'Use this location' }).click()
  await expect(picker).toHaveCount(0)
  await expect(form.getByTestId('location-coordinates')).toBeVisible()
  await form.getByLabel('Place name').fill('Meeting point')
  await form.getByRole('button', { name: 'Save', exact: true }).click()
  await expect(page.getByRole('grid', { name: /^Calendar for /, includeHidden: true })).toBeVisible()

  await expect
    .poll(() => fake.runsOf(token).find((run) => run.date === '2026-11-20')?.location)
    .toEqual({
      latitude: Number(latitude),
      longitude: Number(longitude),
      name: 'Meeting point',
    })
})

test('without a mouse: type the coordinates, Enter confirms, the focus returns to the button', async ({ page }) => {
  const { fake, token } = await start(page)
  const form = await openNew(page)
  await form.getByRole('button', { name: 'Pick on map' }).focus()
  await page.keyboard.press('Enter')
  const picker = page.getByRole('dialog', { name: 'Choose a location' })
  await expect(pickerMap(page)).toHaveAttribute('data-map-state', 'ready')

  await picker.getByLabel('Latitude').fill('50.2945')
  await picker.getByLabel('Longitude').fill('181')
  await expect(picker.getByLabel('Longitude')).toHaveAttribute('aria-invalid', 'true')
  await expect(picker.getByRole('button', { name: 'Use this location' })).toBeDisabled()
  await picker.getByLabel('Longitude').fill('18,6714')
  await expect(picker.getByTestId('map-marker')).toBeVisible()
  await expect(picker.getByRole('status')).toContainText('Selected position: 50.2945, 18.6714')
  await picker.getByLabel('Longitude').press('Enter')

  await expect(picker).toHaveCount(0)
  await expect(form.getByRole('button', { name: 'Change on map' })).toBeFocused()
  await expect(form.getByTestId('location-coordinates')).toContainText('50.29450, 18.67140')
  await form.getByRole('button', { name: 'Save', exact: true }).click()
  await expect
    .poll(() => fake.runsOf(token).find((run) => run.date === '2026-11-20')?.location)
    .toMatchObject({ latitude: 50.2945, longitude: 18.6714, name: null })
})

test('an existing place shows in the form and the details, can be cleared, and Cancel changes nothing', async ({
  page,
}) => {
  const { fake, token } = await start(page, { seed: [WITH_PLACE] })
  const run = fake.runsOf(token)[0]!

  await openDiary(page, `/diary/2026/11/runs/${run.id}`)
  const details = page.getByRole('dialog', { name: 'Long one' })
  await expect(details.getByRole('heading', { name: 'Location' })).toBeVisible()
  await expect(details.getByText('Gliwice', { exact: false }).first()).toBeVisible()
  await expect(details.getByRole('region', { name: "Map of the run's location" })).toHaveAttribute(
    'data-map-state',
    'ready',
  )
  expect(
    (await details.getByRole('region', { name: "Map of the run's location" }).boundingBox())!.height,
  ).toBeGreaterThan(100)

  const form = await openEdit(page, run.id)
  await expect(form.getByLabel('Place name')).toHaveValue('Gliwice')
  await form.getByRole('button', { name: 'Change on map' }).click()
  const picker = page.getByRole('dialog', { name: 'Choose a location' })
  await expect(picker.getByLabel('Latitude')).toHaveValue('50.294862')
  await picker.getByRole('button', { name: 'Cancel' }).click()
  await expect(form.getByRole('button', { name: 'Change on map' })).toBeFocused()

  await form.getByRole('button', { name: 'Clear location' }).click()
  await form.getByRole('button', { name: 'Save', exact: true }).click()
  await expect.poll(() => fake.runsOf(token)[0]!.location).toBeNull()
})

test('saving another field keeps the exact coordinates of the stored place', async ({ page }) => {
  const { fake, token } = await start(page, { seed: [WITH_PLACE] })
  const run = fake.runsOf(token)[0]!
  const form = await openEdit(page, run.id)
  await form.getByLabel('Title').fill('Renamed')
  await form.getByRole('button', { name: 'Save', exact: true }).click()
  await expect.poll(() => fake.runsOf(token)[0]!.title).toBe('Renamed')
  expect(fake.runsOf(token)[0]!.location).toEqual({
    latitude: 50.294862123,
    longitude: 18.676642987,
    name: 'Gliwice',
  })
})

test.describe('place search', () => {
  test('is not there while geocoding is disabled', async ({ page }) => {
    await start(page)
    const form = await openNew(page)
    await expect(form.getByLabel('Search for a place')).toHaveCount(0)
  })

  test('finds a place, fills the location from it, and Enter does not submit the run', async ({ page }) => {
    const { fake, token } = await start(page, { geocoding: true })
    const form = await openNew(page)
    await form.getByLabel('Search for a place').fill('gliwice')
    await form.getByLabel('Search for a place').press('Enter')
    const results = form.getByRole('list', { name: 'Search results' })
    await expect(results.getByRole('button')).toHaveCount(2)
    await expect(form).toBeVisible()
    expect(fake.runsOf(token).filter((run) => run.date === '2026-11-20')).toHaveLength(0)

    await results.getByRole('button', { name: /Gliwice Rynek/ }).click()
    await expect(form.getByLabel('Place name')).toHaveValue('Gliwice Rynek')
    await expect(form.getByLabel('Place name')).toBeFocused()
    await expect(form.getByTestId('location-coordinates')).toContainText('50.29450, 18.67140')
    expect(await seriousViolations(page)).toEqual([])

    await form.getByRole('button', { name: 'Save', exact: true }).click()
    await expect
      .poll(() => fake.runsOf(token).find((run) => run.date === '2026-11-20')?.location)
      .toEqual({ latitude: 50.2945, longitude: 18.6714, name: 'Gliwice Rynek' })
  })
})
