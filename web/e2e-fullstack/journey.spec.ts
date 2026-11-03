import { expect, test } from '@playwright/test'

// One real journey through the whole stack (web -> /api proxy -> API -> SQL): nothing is mocked (M3 §6, AC2/AC9/AC10).
// Every run created here is deleted again; the guest account is isolated and expires on its own.

test('try as guest -> seeded month -> add -> complete -> rate -> delete', async ({ page }) => {
  const title = `E2E ${Date.now()}`

  // Real POST /api/guest-sessions.
  await page.goto('/')
  await expect(page).toHaveURL(/\/signin/)
  const guestSession = page.waitForResponse(
    (response) => response.url().endsWith('/api/guest-sessions') && response.request().method() === 'POST',
  )
  await page.getByRole('button', { name: 'Try as guest' }).click()
  expect((await guestSession).status()).toBe(201)

  // The current month with the seeded demo runs (the grid keeps the cell bubbles as [data-run-id]).
  await expect(page).toHaveURL(/\/diary\/\d{4}\/\d{2}$/)
  await expect(page.getByRole('heading', { level: 1, name: /^Diary – / })).toBeVisible()
  await expect(page.getByText('You are in demo mode. Data is deleted after 24 hours.')).toBeVisible()
  await expect(page.getByRole('grid', { name: /^Calendar for / })).toBeVisible()
  await expect(page.locator('[role="gridcell"] [data-run-id]').first()).toBeAttached({ timeout: 20_000 })

  // Add a planned run (date prefilled with today in the profile zone).
  await page.getByRole('link', { name: 'Add run', exact: true }).click()
  const form = page.getByRole('dialog', { name: 'Add run' })
  await expect(form.getByLabel('Date')).toHaveValue(/^\d{4}-\d{2}-\d{2}$/)
  await form.getByLabel('Title').fill(title)
  await form.getByLabel('Distance (km)').fill('9.5')
  await form.getByRole('button', { name: 'Save' }).click()
  await expect(page.getByRole('dialog')).toHaveCount(0)

  // It is listed in the month panel as Planned.
  await page.getByRole('link', { name: 'Month list' }).click()
  const list = page.getByRole('dialog', { name: /^Runs in / })
  const item = list.getByRole('listitem').filter({ hasText: title })
  await expect(item).toHaveAttribute('data-status', 'Planned')
  await expect(item).toContainText('9.5 km')

  // Mark as completed: the real API needs the actuals; 47:30 over 9.5 km is 5:00 /km.
  await item.getByRole('link', { name: 'Mark as completed' }).click()
  const complete = page.getByRole('dialog', { name: 'Complete run' })
  await complete.getByLabel('Duration (h:mm:ss)').fill('47:30')
  await expect(complete.getByTestId('pace')).toHaveText('5:00 /km')
  await complete.getByRole('button', { name: 'Save' }).click()
  await expect(list).toBeVisible()
  await expect(item).toHaveAttribute('data-status', 'Completed')

  // Rate.
  await item.getByRole('link', { name: 'Rate' }).click()
  const rate = page.getByRole('dialog', { name: 'Rate this run' })
  await rate.locator('label', { hasText: '4 stars' }).click()
  await rate.getByLabel('Comment').fill('Full-stack check')
  await rate.getByRole('button', { name: 'Save' }).click()
  await expect(list).toBeVisible()
  await expect(item.getByRole('link', { name: 'Rate' })).toHaveCount(0)
  await item.getByRole('link', { name: 'View details' }).click()
  const details = page.getByRole('dialog', { name: new RegExp(title) })
  await expect(details.getByText('4 out of 5 stars')).toBeVisible()
  await expect(details.getByText('Full-stack check')).toBeVisible()
  await page.keyboard.press('Escape')

  // Delete with confirmation; the run is gone after a reload too.
  await expect(list).toBeVisible()
  await item.getByRole('button', { name: 'Delete' }).click()
  await page.getByRole('alertdialog', { name: 'Delete this run?' }).getByRole('button', { name: 'Delete' }).click()
  await expect(item).toHaveCount(0)
  await page.reload()
  await expect(page.getByRole('dialog', { name: /^Runs in / })).toBeVisible()
  await expect(page.getByText(title)).toHaveCount(0)
})
