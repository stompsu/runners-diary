import { expect, test, type Page } from '@playwright/test'
import { faults, typeId } from '../src/test/fakeApi/index.ts'
import { cell, mockApi, openDiary, recordWrites, restoreGuest, TODAY } from './support.ts'

// Mocked journeys against the production build (M3 §6, ADR 0019): the in-memory fake API answers every /api call.

type FakeApiT = Awaited<ReturnType<typeof mockApi>>
/** Somebody else edits the run (title) through the API, bumping its version. */
async function changeOnServer(fake: FakeApiT, token: string, id: string, title: string) {
  const run = fake.runsOf(token).find((r) => r.id === id)!
  const response = await fake.handleRaw({
    method: 'PUT',
    url: `http://localhost/api/runs/${id}`,
    headers: { authorization: `Bearer ${token}`, 'content-type': 'application/json' },
    body: JSON.stringify({
      date: run.date,
      status: run.status,
      runTypeId: run.runTypeId,
      title,
      distanceMeters: run.distanceMeters,
      durationSeconds: run.durationSeconds,
      version: run.version,
    }),
  })
  expect(response.status).toBe(200)
}

const weekRow = (page: Page, week: number) =>
  page.getByRole('rowheader', { name: new RegExp(`Week ${week}\\b`), includeHidden: true })

test('try as guest -> populated calendar -> add -> complete -> rate -> delete, driven from the header and month list', async ({
  page,
}) => {
  const fake = await mockApi(page)

  // Anonymous visitor -> sign-in -> real (fake) guest session -> the current month with the seeded runs.
  await page.goto('/')
  await expect(page).toHaveURL(/\/signin/)
  await page.getByRole('button', { name: 'Try as guest' }).click()
  await expect(page).toHaveURL(/\/diary\/2026\/11$/)
  await expect(page.getByRole('heading', { level: 1, name: 'Diary – November 2026' })).toBeVisible()
  await expect(page.getByText('You are in demo mode. Data is deleted after 24 hours.')).toBeVisible()
  await expect(cell(page, '2026-11-14').locator('[data-family="race"] svg')).toBeVisible()
  await expect(cell(page, TODAY)).toHaveAttribute('aria-current', 'date')
  // Week 45 (Nov 2-8): completed 8 + 10 + 21 + 5 km.
  await expect(weekRow(page, 45)).toContainText('44 km')

  // Add a planned 12 km run with the header button (date prefilled with today).
  await page.getByRole('link', { name: 'Add run', exact: true }).click()
  const form = page.getByRole('dialog', { name: 'Add run' })
  await expect(form.getByLabel('Date')).toHaveValue(TODAY)
  await form.getByLabel('Distance (km)').fill('12')
  await form.getByRole('button', { name: 'Save' }).click()
  await expect(page.getByRole('dialog')).toHaveCount(0)
  await expect(cell(page, TODAY).locator('[data-status="Planned"]').first()).toBeAttached()
  await expect(weekRow(page, 45)).toContainText('Planned 12 km')

  // Month list: mark as completed, rate, delete.
  await page.getByRole('link', { name: 'Month list' }).click()
  const list = page.getByRole('dialog', { name: 'Runs in November 2026' })
  const item = list.locator('li[data-status="Planned"]', { hasText: '12 km' })
  await item.getByRole('link', { name: 'Mark as completed' }).click()
  const complete = page.getByRole('dialog', { name: 'Complete run' })
  await complete.getByLabel('Duration (h:mm:ss)').fill('1:00:00')
  await expect(complete.getByTestId('pace')).toHaveText('5:00 /km')
  await complete.getByRole('button', { name: 'Save' }).click()
  await expect(list).toBeVisible()
  const done = list.locator('li[data-status="Completed"]', { hasText: '12 km' })
  await expect(done).toBeVisible()
  await expect(weekRow(page, 45)).toContainText('56 km')

  await done.getByRole('link', { name: 'Rate' }).click()
  const rate = page.getByRole('dialog', { name: 'Rate this run' })
  await rate.locator('label', { hasText: '5 stars' }).click()
  await rate.getByRole('button', { name: 'Save' }).click()
  await expect(list).toBeVisible()
  await expect(done.getByRole('link', { name: 'Rate' })).toHaveCount(0)

  await done.getByRole('button', { name: 'Delete' }).click()
  await page.getByRole('alertdialog', { name: 'Delete this run?' }).getByRole('button', { name: 'Delete' }).click()
  await expect(done).toHaveCount(0)
  await expect(weekRow(page, 45)).toContainText('44 km')

  expect(fake.requests.filter((r) => r === 'POST /api/guest-sessions')).toHaveLength(1)
})

test.describe('editing and concurrency (409)', () => {
  async function openEdit(page: Page) {
    const fake = await mockApi(page)
    const token = await restoreGuest(page, fake)
    await openDiary(page, '/diary/2026/11/day/2026-11-02')
    const id = fake.runsOf(token).find((r) => r.title === 'Easy morning')!.id
    await page.getByRole('dialog', { name: 'Monday, November 2, 2026' }).getByRole('link', { name: 'Edit' }).click()
    const form = page.getByRole('dialog', { name: 'Edit run' })
    await expect(form.getByLabel('Title')).toHaveValue('Easy morning')
    // The form now holds this version; somebody else saves a newer one.
    await changeOnServer(fake, token, id, 'Server wins')
    return { fake, token, id, form }
  }

  test('Reload latest discards my edit and loads the server version', async ({ page }) => {
    const { fake, token, form } = await openEdit(page)
    const title = form.getByLabel('Title')

    await title.fill('Mine')
    await form.getByRole('button', { name: 'Save' }).click()
    await expect(form.getByText('This run was changed elsewhere since you opened it.')).toBeVisible()
    await expect(title).toHaveValue('Mine')
    await form.getByRole('button', { name: 'Reload latest' }).click()

    await expect(title).toHaveValue('Server wins')
    await expect(page.getByText('Loaded the latest version of this run.').first()).toBeVisible()
    await expect(form.getByRole('button', { name: 'Reload latest' })).toHaveCount(0)
    expect(fake.runsOf(token).some((r) => r.title === 'Mine')).toBe(false)
  })

  test('Save mine anyway overwrites with my values and keeps the untouched ones', async ({ page }) => {
    const { fake, token, id, form } = await openEdit(page)
    const latestVersion = fake.runsOf(token).find((r) => r.id === id)!.version
    const writes = recordWrites(page)

    await form.getByLabel('Title').fill('Mine')
    await form.getByRole('button', { name: 'Save' }).click()
    await form.getByRole('button', { name: 'Save mine anyway' }).click()

    await expect(page.getByRole('dialog', { name: 'Edit run' })).toHaveCount(0)
    const stored = fake.runsOf(token).find((r) => r.id === id)!
    expect(stored.title).toBe('Mine')
    expect(stored.distanceMeters).toBe(8000)
    // First attempt with the stale version (409), then the retry with the latest version.
    const puts = writes.filter((w) => w.method === 'PUT')
    expect(puts).toHaveLength(2)
    expect(puts[0]!.body?.version).not.toBe(latestVersion)
    expect(puts[1]!.body?.version).toBe(latestVersion)
  })

  test('a PUT preserves the run location the form does not show', async ({ page }) => {
    const fake = await mockApi(page)
    const token = await restoreGuest(page, fake, {
      seed: [
        {
          date: '2026-11-05',
          status: 'Completed',
          runTypeId: typeId('easy'),
          title: 'Park loop',
          distanceMeters: 5000,
          durationSeconds: 1500,
          location: { latitude: 50.2945, longitude: 18.6714, name: 'Gliwice' },
        },
      ],
    })
    await openDiary(page, '/diary/2026/11/day/2026-11-05')
    const writes = recordWrites(page)

    await page.getByRole('link', { name: 'Edit' }).click()
    const form = page.getByRole('dialog', { name: 'Edit run' })
    await form.getByLabel('Title').fill('Park loop 2')
    await form.getByRole('button', { name: 'Save' }).click()
    await expect(page.getByRole('dialog', { name: 'Edit run' })).toHaveCount(0)

    expect(writes.at(-1)?.body?.location).toEqual({ latitude: 50.2945, longitude: 18.6714, name: 'Gliwice' })
    expect(fake.runsOf(token)[0]!.location).toEqual({ latitude: 50.2945, longitude: 18.6714, name: 'Gliwice' })
  })
})

test('other write errors show localized messages (409 run limit, 413) and keep the form open', async ({ page }) => {
  const fake = await mockApi(page)
  await restoreGuest(page, fake)
  await openDiary(page, '/diary/2026/11/runs/new?date=2026-11-20')
  const form = page.getByRole('dialog', { name: 'Add run' })
  await form.getByLabel('Distance (km)').fill('5')

  fake.addFault({
    match: { method: 'POST', path: '/api/runs' },
    action: { type: 'status', status: 409, problemType: 'urn:runnersdiary:problem:run-limit' },
    times: 1,
  })
  await form.getByRole('button', { name: 'Save' }).click()
  await expect(form.getByText('You have reached the maximum number of runs.')).toBeVisible()

  fake.addFault(faults.status({ method: 'POST', path: '/api/runs' }, 413, 1))
  await form.getByRole('button', { name: 'Save' }).click()
  await expect(form.getByText('The request is too large.')).toBeVisible()
  await expect(form).toBeVisible()
})

test('a bare server 400 shows the form-level summary and keeps the form open', async ({ page }) => {
  const fake = await mockApi(page)
  await restoreGuest(page, fake)
  await openDiary(page, '/diary/2026/11/runs/new?date=2026-11-20')
  const form = page.getByRole('dialog', { name: 'Add run' })
  // Field-level mapping of 400s is covered by the Vitest serverErrorMapping tests; here the error list is empty.
  fake.addFault({ match: { method: 'POST', path: '/api/runs' }, action: { type: 'status', status: 400 }, times: 1 })
  await form.getByLabel('Distance (km)').fill('5')
  await form.getByRole('button', { name: 'Save' }).click()
  await expect(form.getByRole('alert')).toContainText('Some values are not valid')
})

test('month navigation through the URL, Back/Forward and the year boundary', async ({ page }) => {
  const fake = await mockApi(page)
  await restoreGuest(page, fake)
  await openDiary(page, '/diary/2026/11')

  await page.getByRole('link', { name: 'Next month' }).click()
  await expect(page).toHaveURL(/\/diary\/2026\/12$/)
  await expect(page.getByRole('heading', { level: 1, name: 'Diary – December 2026' })).toBeVisible()
  // 2026 has 53 ISO weeks: Mon 28 Dec belongs to week 53 and the week continues into January.
  await expect(page.getByRole('rowheader', { name: /Week 53/ })).toBeVisible()
  await expect(cell(page, '2026-12-28')).toBeVisible()
  await expect(cell(page, '2027-01-03')).toBeVisible()

  await page.getByRole('link', { name: 'Next month' }).click()
  await expect(page).toHaveURL(/\/diary\/2027\/01$/)
  await expect(page.getByRole('heading', { level: 1, name: 'Diary – January 2027' })).toBeVisible()
  await expect(page.getByRole('rowheader', { name: /Week 53/ })).toBeVisible()
  await expect(cell(page, '2026-12-28')).toBeVisible()
  await expect(page.getByRole('rowheader', { name: /Week 1\b/ })).toBeVisible()

  await page.goBack()
  await expect(page).toHaveURL(/\/diary\/2026\/12$/)
  await expect(page.getByRole('heading', { level: 1, name: 'Diary – December 2026' })).toBeVisible()
  await page.goBack()
  await expect(page).toHaveURL(/\/diary\/2026\/11$/)
  await page.goForward()
  await expect(page).toHaveURL(/\/diary\/2026\/12$/)

  // Picker: pick a month in another year.
  await page.getByRole('button', { name: /choose month and year/ }).click()
  await page.getByRole('button', { name: 'Next year' }).click()
  await page.getByRole('button', { name: 'March 2027' }).click()
  await expect(page).toHaveURL(/\/diary\/2027\/03$/)

  // Today returns to the current month and focuses today.
  await page.getByRole('button', { name: 'Today' }).click()
  await expect(page).toHaveURL(/\/diary\/2026\/11$/)
  await expect(cell(page, TODAY)).toBeFocused()

  // Deep links: bookmarkable, out-of-range months are Not found, /diary redirects to the current month.
  await page.goto('/diary/2027/01')
  await expect(page.getByRole('heading', { level: 1, name: 'Diary – January 2027' })).toBeVisible()
  await page.goto('/diary/1949/12')
  await expect(page.getByRole('heading', { level: 1, name: 'Page not found' })).toBeVisible()
  await page.goto('/diary/2101/01')
  await expect(page.getByRole('heading', { level: 1, name: 'Page not found' })).toBeVisible()
  await page.goto('/diary')
  await expect(page).toHaveURL(/\/diary\/2026\/11$/)
})

test('keyboard-only: sign in as guest, open a day, add a run and get the focus back on the day', async ({ page }) => {
  await mockApi(page)
  const tabTo = async (locator: import('@playwright/test').Locator) => {
    for (let i = 0; i < 60; i++) {
      if (await locator.evaluate((el) => el === document.activeElement)) return
      await page.keyboard.press('Tab')
    }
    await expect(locator).toBeFocused()
  }

  await page.goto('/')
  await tabTo(page.getByRole('button', { name: 'Try as guest' }))
  await page.keyboard.press('Enter')
  await expect(page).toHaveURL(/\/diary\/2026\/11$/)
  await expect(page.getByRole('grid', { name: /^Calendar for / })).toBeVisible()

  // The grid is one tab stop (roving tabindex); arrows move within it, Enter opens the day.
  await tabTo(page.locator('[role="gridcell"][tabindex="0"]'))
  await page.locator('[role="gridcell"][tabindex="0"]').focus()
  await cell(page, '2026-11-03').focus()
  await page.keyboard.press('ArrowDown')
  await expect(cell(page, '2026-11-10')).toBeFocused()
  await page.keyboard.press('Enter')
  const day = page.getByRole('dialog', { name: 'Tuesday, November 10, 2026' })
  await expect(day).toBeVisible()

  await tabTo(day.getByRole('link', { name: 'Add run on this day' }))
  await page.keyboard.press('Enter')
  const form = page.getByRole('dialog', { name: 'Add run' })
  await expect(form).toBeVisible()
  await tabTo(form.getByLabel('Distance (km)'))
  await page.keyboard.type('7.5')
  await page.keyboard.press('Enter')
  await expect(day.getByRole('listitem')).toHaveAttribute('data-status', 'Planned')
  await expect(day.getByRole('listitem')).toContainText('7.5 km')

  await page.keyboard.press('Escape')
  await expect(page.getByRole('dialog')).toHaveCount(0)
  await expect(cell(page, '2026-11-10')).toBeFocused()
})

// AC13 for the nested case: the day panel is a sibling route that is re-mounted, so the details dialog hands the
// "View details" link (router state) over to it (features/runs/returnFocus.ts).
test('closing the run details returns the focus to the View details link of the day panel', async ({ page }) => {
  const fake = await mockApi(page)
  await restoreGuest(page, fake)
  await openDiary(page, '/diary/2026/11')
  await cell(page, '2026-11-02').focus()
  await page.keyboard.press('Enter')
  const day = page.getByRole('dialog', { name: 'Monday, November 2, 2026' })
  await day.getByRole('link', { name: 'View details' }).click()
  await expect(page.getByRole('dialog', { name: /Easy/ })).toBeVisible()

  await page.keyboard.press('Escape')

  await expect(page.getByRole('dialog', { name: 'Monday, November 2, 2026' })).toBeVisible()
  await expect(
    page.getByRole('dialog', { name: 'Monday, November 2, 2026' }).getByRole('link', { name: 'View details' }),
  ).toBeFocused()
})
