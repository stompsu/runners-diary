import { render } from '@testing-library/react'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { Providers } from '@/app/providers'
import { createQueryClient } from '@/app/queryClient'
import { routes } from '@/app/routes'
import { registerEntraController, startEntraSession, startGuestSession, type EntraController } from '@/auth/session'
import { loadAllPanels } from '@/features/runs/panelRoutes'
import { getFakeApi } from './fake'

// The panel routes are `lazy`. The first import of a chunk (react-hook-form, zod, the dialogs) is transformed on demand
// by Vite and under CPU load took longer than testing-library's findBy timeout, which made the first test of a file
// flaky. Loading the chunks during collection (no test timeout applies) makes every later `lazy()` resolve from the
// module cache. Only files that render routes import this module, so unit tests do not pay for it.
await loadAllPanels()

export type TestSession = 'guest' | 'user' | 'none'

const DAY_MS = 24 * 60 * 60 * 1000

/** Signs in against the current fake API: a guest (stored token) or a real user (fake Entra controller). */
export function signInForTest(kind: Exclude<TestSession, 'none'>): string {
  const fake = getFakeApi()
  if (kind === 'guest') {
    const token = fake.issueToken('Guest')
    startGuestSession({ accessToken: token, expiresAt: Date.now() + DAY_MS, userId: 'test-guest' })
    return token
  }
  const token = fake.issueToken('User')
  const controller: EntraController = {
    acquireToken: () => Promise.resolve(token),
    login: () => Promise.resolve(),
    logout: () => Promise.resolve(),
  }
  registerEntraController(controller)
  startEntraSession('Test Runner')
  return token
}

/**
 * Renders the real route tree at `path` with fresh providers (no cache shared between tests).
 * `session` defaults to a signed-in guest; pass 'none' to exercise the guard / sign-in page.
 */
export function renderRoute(path: string, options: { session?: TestSession } = {}) {
  const session = options.session ?? 'guest'
  if (session !== 'none') signInForTest(session)
  const router = createMemoryRouter(routes, { initialEntries: [path] })
  const queryClient = createQueryClient()
  const result = render(
    <Providers queryClient={queryClient}>
      <RouterProvider router={router} />
    </Providers>,
  )
  return { ...result, router, queryClient }
}
