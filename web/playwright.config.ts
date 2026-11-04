import { defineConfig, devices } from '@playwright/test'

const port = 4173
// The track viewer harness (e2e/harness/map.html) is only served by the Vite dev server; it is not part of the build.
const harnessPort = 4174

// Smoke tests run against the production build (`vite preview`); the API is mocked per test with page.route.
export default defineConfig({
  testDir: './e2e',
  // Software WebGL (SwiftShader) is CPU-bound: with several workers each rendering a map, a map reaching `ready` can
  // take well over the 5 s default. A longer expect timeout only bounds the wait; a passing test is not slower.
  expect: { timeout: 20_000 },
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : 'list',
  use: {
    baseURL: `http://localhost:${port}`,
    trace: 'retain-on-failure',
  },
  projects: [
    {
      name: 'chromium',
      use: {
        ...devices['Desktop Chrome'],
        // MapLibre GL JS 6 needs WebGL2. Headless Chromium on a CI runner has no GPU; Chrome stopped falling back to the
        // software rasteriser automatically, so opt in (M4 step 4 spike, see docs/plans/M4-maps-gpx.md).
        launchOptions: { args: ['--enable-unsafe-swiftshader'] },
      },
      testIgnore: /.*\.phone\.spec\.ts/,
    },
    // AC12/AC14: a 360x640 touch viewport whose browser zone (Los Angeles) differs from the user's profile zone
    // (the fake API's guests are in Europe/Warsaw), so any date that shifts through UTC or the browser zone shows up.
    {
      name: 'phone-la',
      testMatch: /.*\.phone\.spec\.ts/,
      use: {
        ...devices['Desktop Chrome'],
        viewport: { width: 360, height: 640 },
        hasTouch: true,
        timezoneId: 'America/Los_Angeles',
        locale: 'en-US',
        launchOptions: { args: ['--enable-unsafe-swiftshader'] },
      },
    },
  ],
  webServer: [
    {
      command: `npm run build && npm run preview -- --port ${port} --strictPort`,
      url: `http://localhost:${port}`,
      reuseExistingServer: !process.env.CI,
      timeout: 120_000,
    },
    {
      command: `npx vite --port ${harnessPort} --strictPort`,
      url: `http://localhost:${harnessPort}/e2e/harness/map.html`,
      reuseExistingServer: !process.env.CI,
      timeout: 120_000,
    },
  ],
})
