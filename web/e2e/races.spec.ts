import { expect, type Page } from '@playwright/test'
import { DEMO_RACE_IDS } from '../src/test/fakeApi/index.ts'
import {
  cell,
  mockApi,
  openDiary,
  restoreGuest,
  seriousViolations,
  seriousViolationsIn,
  settled,
  stubMapNetwork,
  test,
} from './support.ts'

// M8 AC6-AC9 (E2E parts): race proposals in the diary, details, signup, state, list panel, settings. Fake API, desktop.
// "Today" is 2026-11-03: the demo catalogue has Gliwice Night 10K on Nov 12, Silesian Trail Weekend (10 / 21.1 / 42.2 km)
// on Nov 21, Katowice Half on Nov 29, Rybnik 5K (date changed, Interested) on Dec 11, Tychy 10K (removed, Interested)
// on Dec 3 and Warsaw 10K (outside the demo profile) on Nov 15.

const NIGHT = '2026-11-12'
const TRAIL = '2026-11-21'

async function start(page: Page, options: { guestRaceProfile?: boolean } = {}) {
  const fake = await mockApi(page, options)
  await stubMapNetwork(page)
  const token = await restoreGuest(page, fake)
  return { fake, token }
}

const proposalBubbles = (page: Page) => page.locator('[role="gridcell"] button[data-kind="proposal"]')
const bubbleOf = (page: Page, raceId: string) => page.locator(`[role="gridcell"] button[data-race-id="${raceId}"]`)
const detailsDialog = (page: Page, name: string) => page.getByRole('dialog', { name })

test('proposals are shown and visibly distinct (dotted, flag, legend), the Day cell names them', async ({ page }) => {
  await start(page)
  await openDiary(page, '/diary/2026/11')

  await expect(bubbleOf(page, DEMO_RACE_IDS.night10k)).toBeVisible()
  await expect(bubbleOf(page, DEMO_RACE_IDS.silesianTrail)).toBeVisible()
  await expect(bubbleOf(page, DEMO_RACE_IDS.katowiceHalf)).toBeVisible()
  // Not in the profile (Warsaw), a duplicate listing: no bubble. The removed Tychy race is in December.
  await expect(bubbleOf(page, DEMO_RACE_IDS.warsaw10k)).toHaveCount(0)
  await expect(bubbleOf(page, DEMO_RACE_IDS.night10kDuplicate)).toHaveCount(0)

  const bubble = bubbleOf(page, DEMO_RACE_IDS.night10k)
  await expect(bubble).toHaveClass(/border-dotted/)
  await expect(bubble).toContainText('Demo Gliwice Night 10K')
  await expect(bubble).toContainText('10 km')
  expect(await bubble.evaluate((el) => getComputedStyle(el).borderTopStyle)).toBe('dotted')

  // Distinct from a run bubble: runs are dashed (planned) or solid (completed), never dotted, and carry data-run-id.
  const runBubble = page.locator('[role="gridcell"] [data-run-id]').first()
  await expect(runBubble).toBeAttached()
  expect(await runBubble.evaluate((el) => getComputedStyle(el).borderTopStyle)).not.toBe('dotted')

  // The legend has an entry for proposals.
  const legend = page.getByRole('list', { name: 'Legend' })
  await expect(legend.locator('[data-legend="proposal"]')).toHaveText('Race proposal')

  // The day cell's accessible name lists its proposals (the bubbles themselves are presentational).
  await expect(cell(page, NIGHT)).toHaveAccessibleName(/1 race proposal: Demo Gliwice Night 10K, 10 km/)
})

test('the toggle hides the proposals (and the legend entry), the choice survives a reload', async ({ page }) => {
  const { fake, token } = await start(page)
  await openDiary(page, '/diary/2026/11')
  await expect(proposalBubbles(page).first()).toBeVisible()

  const toggle = page.getByTestId('proposals-toggle')
  await expect(toggle).toHaveAttribute('aria-pressed', 'true')
  await toggle.click()
  await expect(toggle).toHaveAttribute('aria-pressed', 'false')
  await expect(proposalBubbles(page)).toHaveCount(0)
  await expect(page.locator('[data-legend="proposal"]')).toHaveCount(0)
  await expect.poll(() => fake.raceInterestsOf(token)?.showInCalendar).toBe(false)

  await page.reload()
  await expect(page.getByRole('grid', { name: /^Calendar for / })).toBeVisible()
  await expect(page.getByTestId('proposals-toggle')).toHaveAttribute('aria-pressed', 'false')
  await expect(proposalBubbles(page)).toHaveCount(0)
  await expect(page.locator('[data-legend="proposal"]')).toHaveCount(0)

  await page.getByTestId('proposals-toggle').click()
  await expect(proposalBubbles(page).first()).toBeVisible()
  await expect(page.locator('[data-legend="proposal"]')).toBeVisible()
})

test('a bubble opens the details; the signup link is a safe external link', async ({ page }) => {
  await start(page)
  await openDiary(page, '/diary/2026/11')

  await bubbleOf(page, DEMO_RACE_IDS.night10k).click()
  const dialog = detailsDialog(page, 'Demo Gliwice Night 10K')
  await expect(dialog).toBeVisible()
  await expect(page).toHaveURL(new RegExp(`/diary/2026/11/races/${DEMO_RACE_IDS.night10k}$`))
  await expect(dialog.getByText('Gliwice, Silesian')).toBeVisible()
  await expect(dialog.getByText('10 km').first()).toBeVisible()
  await expect(dialog.getByText('Demo Gliwice Runners')).toBeVisible()
  await expect(dialog.getByRole('link', { name: /running\.life|Source/ }).first()).toBeVisible()

  const signup = dialog.getByRole('link', { name: /Open signup page/ })
  await expect(signup).toHaveAttribute('target', '_blank')
  expect(await signup.getAttribute('rel')).toMatch(/noopener/)
  expect(await signup.getAttribute('rel')).toMatch(/noreferrer/)
  await expect(signup).toHaveAttribute('referrerpolicy', 'no-referrer')
  expect(await signup.getAttribute('href')).toMatch(/^https:\/\//)

  // Every outbound link of ours in the dialog has the same protection. (The MapLibre attribution link inside the map is
  // generated by the library with target=_blank and no rel: reported as a finding, not asserted here.)
  const external = dialog.locator('a[target="_blank"]:not(.maplibregl-ctrl a, [class*="maplibregl"] a)')
  expect(await external.count()).toBeGreaterThanOrEqual(3)
  for (const link of await external.all()) {
    expect(await link.getAttribute('rel')).toMatch(/noopener/)
    expect(await link.getAttribute('rel')).toMatch(/noreferrer/)
    expect(await link.getAttribute('href')).toMatch(/^https:\/\//)
  }

  await page.keyboard.press('Escape')
  await expect(dialog).toHaveCount(0)
  await expect(page).toHaveURL(/\/diary\/2026\/11$/)
})

test('I signed up on a race with three distances: the planned race run replaces the proposal and opens', async ({
  page,
}) => {
  const { fake, token } = await start(page)
  await openDiary(page, '/diary/2026/11')

  await bubbleOf(page, DEMO_RACE_IDS.silesianTrail).click()
  const dialog = detailsDialog(page, 'Demo Silesian Trail Weekend')
  await dialog.getByRole('button', { name: 'I signed up' }).click()
  const form = dialog.getByRole('form', { name: 'I signed up' })
  // Distance is required when there is more than one.
  await form.getByRole('button', { name: 'Add to my diary' }).click()
  await expect(form.getByText('Choose a distance.')).toBeVisible()
  await form.getByRole('radio', { name: '21.1 km' }).check()
  await form.getByRole('button', { name: 'Add to my diary' }).click()

  // The run dialog opens (the race dialog is replaced).
  await expect(page).toHaveURL(/\/diary\/2026\/11\/runs\/[0-9a-f-]+$/)
  const run = page.getByRole('dialog', { name: 'Trail race' })
  await expect(run.getByRole('region', { name: 'Race: Demo Silesian Trail Weekend' })).toBeVisible()
  await expect(run.getByText('21.1 km', { exact: true })).toBeVisible()
  await expect(run).toContainText('Planned')
  expect(fake.raceStateOf(token, DEMO_RACE_IDS.silesianTrail)).toBe('SignedUp')
  await page.keyboard.press('Escape')

  // A planned run bubble on the race day; the proposal bubble is gone.
  await expect(cell(page, TRAIL).locator('[data-run-id][data-status="Planned"]').first()).toBeAttached()
  await expect(bubbleOf(page, DEMO_RACE_IDS.silesianTrail)).toHaveCount(0)
  await expect(cell(page, TRAIL)).toHaveAccessibleName(/Demo Silesian Trail Weekend|Race, Planned|Planned/)

  // The month list shows it too.
  await page.getByRole('link', { name: 'Month list' }).click()
  const list = page.getByRole('dialog', { name: 'Runs in November 2026' })
  await expect(list.locator('li[data-status="Planned"]').filter({ hasText: '21.1 km' })).toHaveCount(1)
})

test('Not interested hides the race, Undo brings it back', async ({ page }) => {
  const { fake, token } = await start(page)
  await openDiary(page, '/diary/2026/11')

  await bubbleOf(page, DEMO_RACE_IDS.katowiceHalf).click()
  const dialog = detailsDialog(page, 'Demo Katowice Half Marathon')
  await dialog.getByRole('button', { name: 'Not interested' }).click()
  await expect.poll(() => fake.raceStateOf(token, DEMO_RACE_IDS.katowiceHalf)).toBe('Dismissed')
  await expect(dialog.getByRole('button', { name: 'Undo' })).toBeVisible()
  await page.keyboard.press('Escape')
  await expect(bubbleOf(page, DEMO_RACE_IDS.katowiceHalf)).toHaveCount(0)
  await expect(bubbleOf(page, DEMO_RACE_IDS.night10k)).toBeVisible()

  // Undo from the "Show dismissed" list.
  await page.getByRole('link', { name: 'Race proposals' }).click()
  const list = page.getByRole('dialog', { name: 'Race proposals – November 2026' })
  await expect(list.getByText('Demo Katowice Half Marathon')).toHaveCount(0)
  await list.getByLabel('Show dismissed').click()
  const row = list.getByRole('listitem').filter({ hasText: 'Demo Katowice Half Marathon' })
  await row.getByRole('button', { name: 'Undo' }).click()
  await expect.poll(() => fake.raceStateOf(token, DEMO_RACE_IDS.katowiceHalf)).toBe('None')
  await page.keyboard.press('Escape')
  await expect(bubbleOf(page, DEMO_RACE_IDS.katowiceHalf)).toBeVisible()
})

test('the list panel marks unseen races as New and stops once they were seen', async ({ page }) => {
  const { fake, token } = await start(page)
  await openDiary(page, '/diary/2026/11')

  await page.getByRole('link', { name: 'Race proposals' }).click()
  const list = page.getByRole('dialog', { name: 'Race proposals – November 2026' })
  const night = list.getByRole('listitem').filter({ hasText: 'Demo Gliwice Night 10K' })
  await expect(night).toBeVisible()
  await expect(night.getByText('New', { exact: true })).toBeVisible()
  // The badges stay while the panel is open, even though opening it marks everything as seen.
  await expect.poll(() => fake.raceSeenOf(token)).not.toBeNull()
  await expect(night.getByText('New', { exact: true })).toBeVisible()

  await page.keyboard.press('Escape')
  await page.getByRole('link', { name: 'Race proposals' }).click()
  const again = page.getByRole('dialog', { name: 'Race proposals – November 2026' })
  await expect(again.getByRole('listitem').filter({ hasText: 'Demo Gliwice Night 10K' })).toBeVisible()
  await expect(again.getByText('New', { exact: true })).toHaveCount(0)
})

test('settings: save the interests and the diary filters accordingly (Silesia out, Masovia in)', async ({ page }) => {
  const { fake, token } = await start(page)
  await page.goto('/settings/races')
  const form = page.getByRole('form', { name: 'Race interests' })
  await expect(form).toBeVisible()
  const silesian = form.getByRole('checkbox', { name: 'Silesian (Śląskie)' })
  await expect(silesian).toBeChecked()

  await silesian.uncheck()
  await form.getByRole('checkbox', { name: 'Masovian (Mazowieckie)' }).check()
  // The demo profile also has Gliwice with 60 km: drop the place so only the regions count.
  await form.getByRole('button', { name: 'Clear location' }).click()
  await form.getByRole('button', { name: 'Save interests' }).click()
  await expect(page.getByText('Race interests saved')).toBeVisible()

  await expect.poll(() => fake.raceInterestsOf(token)?.regionCodes).toEqual(['PL-14'])
  expect(fake.raceInterestsOf(token)?.center).toBeNull()

  await openDiary(page, '/diary/2026/11')
  await expect(bubbleOf(page, DEMO_RACE_IDS.warsaw10k)).toBeVisible()
  await expect(bubbleOf(page, DEMO_RACE_IDS.night10k)).toHaveCount(0)
  await expect(bubbleOf(page, DEMO_RACE_IDS.silesianTrail)).toHaveCount(0)
  await expect(bubbleOf(page, DEMO_RACE_IDS.katowiceHalf)).toHaveCount(0)
})

test('keyboard: cell -> Enter -> Day panel -> proposal Details link -> details', async ({ page }) => {
  await start(page)
  await openDiary(page, '/diary/2026/11')

  await cell(page, NIGHT).focus()
  await expect(cell(page, NIGHT)).toBeFocused()
  await page.keyboard.press('Enter')
  const day = page.getByRole('dialog', { name: 'Thursday, November 12, 2026' })
  await expect(day).toBeVisible()
  const section = day.getByRole('region', { name: 'Race proposals' })
  await expect(section.getByText('Demo Gliwice Night 10K')).toBeVisible()

  const link = section.getByRole('link', { name: /Details/ })
  await link.focus()
  await page.keyboard.press('Enter')
  await expect(detailsDialog(page, 'Demo Gliwice Night 10K')).toBeVisible()
  await expect(page).toHaveURL(new RegExp(`/races/${DEMO_RACE_IDS.night10k}$`))

  // Escape goes back to the Day panel (the details were opened from it) with the focus on the Details link ...
  await page.keyboard.press('Escape')
  await expect(day).toBeVisible()
  await expect(section.getByRole('link', { name: /Details/ })).toBeFocused()
  // ... and a second Escape returns to the month with the focus on the day's cell.
  await page.keyboard.press('Escape')
  await expect(day).toHaveCount(0)
  await expect(cell(page, NIGHT)).toBeFocused()
})

test('a changed date of a signed-up race shows the banner; "Move my run" moves it explicitly', async ({ page }) => {
  const { fake, token } = await start(page)
  await openDiary(page, '/diary/2026/12')

  // Rybnik 5K (Dec 11) starts as Interested with a date change: the interested banner is shown.
  await bubbleOf(page, DEMO_RACE_IDS.rybnik5k).click()
  const dialog = detailsDialog(page, 'Demo Rybnik 5K')
  await expect(dialog.locator('[data-banner="date-changed"]')).toContainText('The listing now says')
  await dialog.getByRole('button', { name: 'I signed up' }).click()
  await dialog.getByRole('button', { name: 'Add to my diary' }).click()
  await expect(page).toHaveURL(/\/diary\/2026\/12\/runs\/[0-9a-f-]+$/)
  await page.keyboard.press('Escape')
  await expect(cell(page, '2026-12-11').locator('[data-run-id]').first()).toBeAttached()

  // The organiser moves the race a week later; the run stays where it is until the user moves it.
  fake.patchRace(DEMO_RACE_IDS.rybnik5k, { date: '2026-12-18', previousDate: '2026-12-11' })
  await page.reload()
  await expect(page.getByRole('grid', { name: /^Calendar for / })).toBeVisible()
  await page.goto(`/diary/2026/12/races/${DEMO_RACE_IDS.rybnik5k}`)
  const again = detailsDialog(page, 'Demo Rybnik 5K')
  const banner = again.locator('[data-banner="date-changed"]')
  await expect(banner).toContainText('your planned run is on')
  await expect(cell(page, '2026-12-11').locator('[data-run-id]').first()).toBeAttached()
  expect(fake.raceStateOf(token, DEMO_RACE_IDS.rybnik5k)).toBe('SignedUp')

  await banner.getByRole('button', { name: /Move my run to/ }).click()
  await expect(page.getByText(/Your run moved to/)).toBeVisible()
  await expect(banner).toHaveCount(0)
  await page.keyboard.press('Escape')
  await expect(cell(page, '2026-12-18').locator('[data-run-id]').first()).toBeAttached()
  await expect(cell(page, '2026-12-11').locator('[data-run-id]')).toHaveCount(0)
})

test('a removed race (Interested) shows the removed banner and cannot be signed up through the listing', async ({
  page,
}) => {
  await start(page)
  await page.goto(`/diary/2026/12/races/${DEMO_RACE_IDS.tychy10k}`)
  const dialog = detailsDialog(page, 'Demo Tychy 10K')
  await expect(dialog.locator('[data-banner="removed"]')).toBeVisible()
  await expect(dialog.getByRole('button', { name: /Open signup page/ })).toBeDisabled()
  await expect(dialog.getByRole('button', { name: 'I signed up' })).toBeDisabled()
})

test.describe('accessibility (axe: serious and critical fail)', () => {
  test('the grid with proposals', async ({ page }) => {
    await start(page)
    await openDiary(page, '/diary/2026/11')
    await expect(proposalBubbles(page).first()).toBeVisible()
    await settled(page)
    expect(await seriousViolations(page)).toEqual([])
  })

  test('the details dialog', async ({ page }) => {
    await start(page)
    await openDiary(page, `/diary/2026/11/races/${DEMO_RACE_IDS.silesianTrail}`)
    const dialog = detailsDialog(page, 'Demo Silesian Trail Weekend')
    await expect(dialog.getByRole('link', { name: /Open signup page/ })).toBeVisible()
    expect(await seriousViolationsIn(page, '[role="dialog"]')).toEqual([])
    // With the signup form open.
    await dialog.getByRole('button', { name: 'I signed up' }).click()
    await expect(dialog.getByRole('form', { name: 'I signed up' })).toBeVisible()
    expect(await seriousViolationsIn(page, '[role="dialog"]')).toEqual([])
  })

  test('the proposals list panel', async ({ page }) => {
    await start(page)
    await openDiary(page, '/diary/2026/11/races')
    await expect(page.getByRole('dialog', { name: 'Race proposals – November 2026' })).toBeVisible()
    await expect(page.getByText('Demo Gliwice Night 10K').first()).toBeVisible()
    expect(await seriousViolationsIn(page, '[role="dialog"]')).toEqual([])
  })

  test('the settings page', async ({ page }) => {
    await start(page)
    await page.goto('/settings/races')
    await expect(page.getByRole('form', { name: 'Race interests' })).toBeVisible()
    const map = page.locator('[data-map-state]')
    await expect(map.first()).toHaveAttribute('data-map-state', 'ready')
    await settled(page)
    expect(await seriousViolations(page)).toEqual([])
  })
})
