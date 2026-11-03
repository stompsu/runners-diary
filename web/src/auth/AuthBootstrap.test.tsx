import { act, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { BrowserAuthError } from '@azure/msal-browser'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { getFakeApi } from '@/test/fake'
import { renderRoute } from '@/test/render'
import { ENTRA_HINT_KEY, getSession, signOut, type EntraController } from './session'

const bootEntra = vi.hoisted(() => vi.fn<(config: unknown) => Promise<unknown>>())

// MSAL itself is not exercised here (no tenant); the boundary is tested against a stand-in boot result.
vi.mock('./entra/msalClient', async (importOriginal) => ({
  ...(await importOriginal<typeof import('./entra/msalClient')>()),
  bootEntra,
}))

const ENTRA = {
  clientId: '11111111-1111-4111-8111-111111111111',
  authority: 'https://contoso.ciamlogin.com/',
  apiScope: 'api://22222222-2222-4222-8222-222222222222/access_as_user',
}

function controller(): EntraController {
  let token: string | undefined
  return {
    acquireToken: vi.fn<EntraController['acquireToken']>(() =>
      Promise.resolve((token ??= getFakeApi().issueToken('User'))),
    ),
    login: vi.fn<EntraController['login']>(() => Promise.resolve()),
    logout: vi.fn<EntraController['logout']>(() => Promise.resolve()),
  }
}

describe('sign-out cleanup', () => {
  it('clears the query cache and the mutation cache when the session ends', async () => {
    const { queryClient } = renderRoute('/diary/2026/11')
    await screen.findByRole('grid')
    expect(queryClient.getQueryCache().getAll().length).toBeGreaterThan(0)
    const mutation = queryClient.getMutationCache().build(queryClient, { mutationFn: () => Promise.resolve(1) })
    expect(queryClient.getMutationCache().getAll()).toContain(mutation)

    act(() => signOut('signedOut'))

    await waitFor(() => expect(queryClient.getMutationCache().getAll()).toHaveLength(0))
    expect(
      queryClient
        .getQueryCache()
        .getAll()
        .filter((q) => q.queryKey[1] !== '/api/client-config'),
    ).toHaveLength(0)
  })
})

describe('Entra boundary', () => {
  beforeEach(() => {
    bootEntra.mockReset()
  })

  it('is not loaded when the API reports no Entra config', async () => {
    renderRoute('/signin', { session: 'none' })

    await screen.findByRole('button', { name: 'Try as guest' })
    await waitFor(() => expect(getFakeApi().requests).toContain('GET /api/client-config'))
    expect(bootEntra).not.toHaveBeenCalled()
    expect(screen.queryByRole('button', { name: 'Sign in' })).not.toBeInTheDocument()
  })

  it('shows Sign in once MSAL is ready and starts the redirect (returnTo stays in the /signin URL)', async () => {
    const user = userEvent.setup()
    const c = controller()
    bootEntra.mockResolvedValue({ controller: c, account: null, redirectError: null })
    getFakeApi().setEntra(ENTRA)
    renderRoute('/signin?returnTo=%2Fdiary%2F2026%2F05', { session: 'none' })

    const button = await screen.findByRole('button', { name: 'Sign in' })
    await waitFor(() => expect(button).toBeEnabled())
    await user.click(button)

    expect(c.login).toHaveBeenCalledExactlyOnceWith()
    expect(bootEntra).toHaveBeenCalledWith(ENTRA)
    expect(sessionStorage.getItem(ENTRA_HINT_KEY)).toBe('pending')
  })

  it('disables Sign in until MSAL has loaded', async () => {
    bootEntra.mockReturnValue(new Promise(() => undefined))
    getFakeApi().setEntra(ENTRA)
    renderRoute('/signin', { session: 'none' })

    expect(await screen.findByRole('button', { name: 'Sign in' })).toBeDisabled()
  })

  it('completes a redirect sign-in and continues to returnTo', async () => {
    sessionStorage.setItem(ENTRA_HINT_KEY, 'pending')
    const { resetSessionForTests } = await import('./session')
    resetSessionForTests() // a fresh page load coming back from the identity provider
    bootEntra.mockResolvedValue({
      controller: controller(),
      account: { name: 'Ada', username: 'ada@example.com' },
      redirectError: null,
    })
    getFakeApi().setEntra(ENTRA)
    const { router } = renderRoute('/signin?returnTo=%2Finsights', { session: 'none' })

    await waitFor(() => expect(router.state.location.pathname).toBe('/insights'))
    expect(getSession()).toEqual({ kind: 'entra', name: 'Ada' })
    expect(await screen.findByText('Signed in as Ada')).toBeInTheDocument()
  })

  it('restores the cached account after a reload without bouncing through the sign-in page', async () => {
    sessionStorage.setItem(ENTRA_HINT_KEY, 'active')
    const { resetSessionForTests } = await import('./session')
    resetSessionForTests()
    bootEntra.mockResolvedValue({
      controller: controller(),
      account: { name: 'Ada', username: 'ada@example.com' },
      redirectError: null,
    })
    getFakeApi().setEntra(ENTRA)
    const { router } = renderRoute('/insights', { session: 'none' })

    expect(await screen.findByRole('heading', { level: 1, name: 'Insights' })).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/insights')
  })

  it('ends up on the sign-in page with a message when the redirect fails (e.g. cancelled)', async () => {
    sessionStorage.setItem(ENTRA_HINT_KEY, 'pending')
    const { resetSessionForTests } = await import('./session')
    resetSessionForTests()
    bootEntra.mockResolvedValue({ controller: controller(), account: null, redirectError: new Error('access_denied') })
    getFakeApi().setEntra(ENTRA)
    renderRoute('/signin', { session: 'none' })

    expect(await screen.findByText('Your session has ended. Please sign in again.')).toBeInTheDocument()
  })

  it('returns quietly to the sign-in page, without a "session ended" notice, when the user cancels at the IdP', async () => {
    sessionStorage.setItem(ENTRA_HINT_KEY, 'pending')
    const { resetSessionForTests } = await import('./session')
    resetSessionForTests()
    bootEntra.mockResolvedValue({
      controller: controller(),
      account: null,
      redirectError: new BrowserAuthError('user_cancelled', 'User cancelled the flow'),
    })
    getFakeApi().setEntra(ENTRA)
    renderRoute('/signin', { session: 'none' })

    expect(await screen.findByRole('button', { name: 'Sign in' })).toBeInTheDocument()
    await waitFor(() => expect(getSession()).toEqual({ kind: 'anonymous', reason: null }))
    expect(screen.queryByText('Your session has ended. Please sign in again.')).not.toBeInTheDocument()
  })

  it('resolves a stale Entra hint when the API has no Entra config', async () => {
    sessionStorage.setItem(ENTRA_HINT_KEY, 'active')
    const { resetSessionForTests } = await import('./session')
    resetSessionForTests()
    const { router } = renderRoute('/insights', { session: 'none' })

    await waitFor(() => expect(router.state.location.pathname).toBe('/signin'))
    expect(bootEntra).not.toHaveBeenCalled()
  })
})
