import { expect } from '@playwright/test'
import { test } from './support.ts'

// Self-test of the CSP guard fixture (plan M7 AC3): a deliberate violation must fail the test that causes it, otherwise a
// silent guard would let a broken policy through. Needs the nginx image (PLAYWRIGHT_BASE_URL): `vite preview` sends no CSP,
// so nothing could be violated there. `test.fail()` passes only when the auto fixture reports the violation.
test.describe('CSP guard fixture', () => {
  test.skip(!process.env.PLAYWRIGHT_BASE_URL, 'only meaningful against the web image, which sends the real CSP')

  test('catches an inline script blocked by script-src', async ({ page }) => {
    test.fail()
    await page.goto('/redirect.html')
    await page.evaluate(() => {
      const script = document.createElement('script')
      script.textContent = 'window.inlineRan = true'
      document.head.appendChild(script)
    })
    expect(await page.evaluate(() => (window as { inlineRan?: boolean }).inlineRan)).toBeUndefined()
  })

  test('catches a request to a foreign host blocked by connect-src', async ({ page }) => {
    test.fail()
    await page.goto('/redirect.html')
    await page.evaluate(() => fetch('https://csp-guard.invalid/ping').catch(() => 'blocked'))
  })

  test('stays quiet on a page that violates nothing', async ({ page }) => {
    await page.goto('/redirect.html')
    await page.evaluate(() => fetch('/nginx-alive').then((r) => r.status))
  })
})
