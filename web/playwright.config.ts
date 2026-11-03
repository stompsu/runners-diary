import { defineConfig, devices } from '@playwright/test'

const port = 4173

// Smoke tests run against the production build (`vite preview`); the API is mocked per test with page.route.
export default defineConfig({
  testDir: './e2e',
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : 'list',
  use: {
    baseURL: `http://localhost:${port}`,
    trace: 'retain-on-failure',
  },
  projects: [
    { name: 'chromium', use: { ...devices['Desktop Chrome'] }, testIgnore: /.*\.phone\.spec\.ts/ },
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
      },
    },
  ],
  webServer: {
    command: `npm run build && npm run preview -- --port ${port} --strictPort`,
    url: `http://localhost:${port}`,
    reuseExistingServer: !process.env.CI,
    timeout: 120_000,
  },
})
