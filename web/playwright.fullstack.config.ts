import { defineConfig, devices } from '@playwright/test'

// Full-stack journey against a RUNNING stack (SQL + API + web, e.g. `dotnet run --project src/RunnersDiary.AppHost
// --launch-profile http`). There is deliberately no `webServer`: CI (or the developer) starts the stack and sets
// FULLSTACK_BASE_URL. Nothing is mocked here.
const baseURL = process.env.FULLSTACK_BASE_URL ?? 'http://localhost:5173'

export default defineConfig({
  testDir: './e2e-fullstack',
  timeout: 90_000,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  workers: 1,
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : 'list',
  use: {
    baseURL,
    trace: 'retain-on-failure',
  },
  projects: [{ name: 'fullstack-chromium', use: { ...devices['Desktop Chrome'] } }],
})
