import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { toast } from 'sonner'
import { describe, expect, it, vi } from 'vitest'
import { startGuestSession } from '@/auth/session'
import i18n from '@/i18n'
import * as language from '@/i18n/language'
import { LANGUAGE_STORAGE_KEY, setLanguage } from '@/i18n/language'
import { getFakeApi } from '@/test/fake'
import { renderRoute } from '@/test/render'
import { recordRequests } from '@/test/requests'

const switcher = (name = 'Language') => screen.findByRole('combobox', { name })

describe('language switcher and profile sync', () => {
  it('is in the header of a guest, names each option in its own language and switches without reload', async () => {
    const user = userEvent.setup()
    renderRoute('/diary/2026/11')

    const select = await switcher()
    const options = screen.getAllByRole('option')
    expect(options.map((o) => [o.textContent, o.getAttribute('lang')])).toEqual([
      ['English', 'en'],
      ['Polski', 'pl'],
    ])

    await user.selectOptions(select, 'pl')

    expect(await screen.findByRole('link', { name: 'Dziennik' })).toBeInTheDocument()
    expect(screen.getByRole('combobox', { name: 'Język' })).toHaveValue('pl')
    expect(document.documentElement.lang).toBe('pl')
    expect(localStorage.getItem(LANGUAGE_STORAGE_KEY)).toBe('pl')
  })

  it('shows a compact code for phones that is hidden from assistive technology; the names stay full', async () => {
    const user = userEvent.setup()
    renderRoute('/diary/2026/11')

    const select = await switcher()
    const code = screen.getByText('EN')
    expect(code).toHaveAttribute('aria-hidden', 'true')
    expect(select).toHaveAccessibleName('Language')

    await user.selectOptions(select, 'pl')

    expect(await screen.findByText('PL')).toHaveAttribute('aria-hidden', 'true')
    expect(screen.getByRole('combobox', { name: 'Język' })).toBeInTheDocument()
    expect(screen.getAllByRole('option').map((o) => o.textContent)).toEqual(['English', 'Polski'])
  })

  it('is a labelled native select in the tab order (the browser provides the keyboard operation)', async () => {
    const user = userEvent.setup()
    renderRoute('/diary/2026/11')

    const select = await switcher()
    expect(select.tagName).toBe('SELECT')
    await user.tab()
    await user.tab()
    for (let i = 0; i < 10 && document.activeElement !== select; i++) await user.tab()

    expect(select).toHaveFocus()
  })

  it('saves the choice to the profile of a guest and of a user (PUT /api/me/language)', async () => {
    for (const kind of ['guest', 'user'] as const) {
      const user = userEvent.setup()
      const requests = recordRequests()
      const view = renderRoute('/diary/2026/11', { session: kind })

      await user.selectOptions(await switcher(), 'pl')

      await waitFor(() =>
        expect(requests.filter((r) => r.method === 'PUT' && r.path === '/api/me/language')).toEqual([
          { method: 'PUT', path: '/api/me/language', body: { language: 'pl' } },
        ]),
      )
      view.unmount()
      await i18n.changeLanguage('en')
    }
  })

  it('does not call the API on the sign-in page, but offers the switcher there', async () => {
    const user = userEvent.setup()
    const requests = recordRequests()
    renderRoute('/signin', { session: 'none' })

    await user.selectOptions(await switcher(), 'pl')

    await waitFor(() => expect(document.documentElement.lang).toBe('pl'))
    expect(requests.some((r) => r.path === '/api/me/language')).toBe(false)
  })

  it('keeps the local choice when saving it fails', async () => {
    const user = userEvent.setup()
    const fallback = vi.mocked(fetch).getMockImplementation()!
    vi.mocked(fetch).mockImplementation((input, init) => {
      const request = input instanceof Request ? input : new Request(input, init)
      if (new URL(request.url).pathname === '/api/me/language') return Promise.reject(new TypeError('offline'))
      return fallback(input, init)
    })
    renderRoute('/diary/2026/11')

    await user.selectOptions(await switcher(), 'pl')

    expect(await screen.findByRole('link', { name: 'Dziennik' })).toBeInTheDocument()
    expect(i18n.language).toBe('pl')
  })

  it('applies the profile language once the profile is loaded: the profile wins over rd.lang', async () => {
    localStorage.setItem(LANGUAGE_STORAGE_KEY, 'en')
    const token = getFakeApi().issueToken('Guest', { language: 'pl' })
    startGuestSession({ accessToken: token, expiresAt: Date.now() + 3_600_000, userId: 'pl-guest' })

    renderRoute('/diary/2026/11', { session: 'none' })

    expect(await screen.findByRole('link', { name: 'Dziennik' })).toBeInTheDocument()
    expect(localStorage.getItem(LANGUAGE_STORAGE_KEY)).toBe('pl')
    expect(document.documentElement.lang).toBe('pl')
  })

  it('applies the profile language once per account: a later switch is not undone by a refetch', async () => {
    const user = userEvent.setup()
    const token = getFakeApi().issueToken('Guest', { language: 'pl' })
    startGuestSession({ accessToken: token, expiresAt: Date.now() + 3_600_000, userId: 'pl-guest' })
    const { queryClient } = renderRoute('/diary/2026/11', { session: 'none' })
    await screen.findByRole('link', { name: 'Dziennik' })

    // The save fails (the profile keeps `pl`), the user stays on English, and /api/me is refetched.
    vi.mocked(fetch).mockImplementationOnce(() => Promise.reject(new TypeError('offline')))
    await user.selectOptions(screen.getByRole('combobox', { name: 'Język' }), 'en')
    await queryClient.invalidateQueries({ queryKey: ['get', '/api/me'] })

    expect(await screen.findByRole('link', { name: 'Diary' })).toBeInTheDocument()
    expect(i18n.language).toBe('en')
  })

  it('a null profile language leaves the browser language alone', async () => {
    await setLanguage('pl')
    renderRoute('/diary/2026/11')

    expect(await screen.findByRole('link', { name: 'Dziennik' })).toBeInTheDocument()
    expect(i18n.language).toBe('pl')
  })

  it('a switch made while the first /api/me is in flight is not undone by the older profile value', async () => {
    const user = userEvent.setup()
    const token = getFakeApi().issueToken('Guest', { language: 'pl' })
    startGuestSession({ accessToken: token, expiresAt: Date.now() + 3_600_000, userId: 'slow-guest' })
    const fallback = vi.mocked(fetch).getMockImplementation()!
    let release!: () => void
    const gate = new Promise<void>((resolve) => (release = resolve))
    let held = false
    vi.mocked(fetch).mockImplementation(async (input, init) => {
      const request = input instanceof Request ? input : new Request(input, init)
      if (!held && request.method === 'GET' && new URL(request.url).pathname === '/api/me') {
        held = true
        const stale = await fallback(input, init) // the profile as it was before the switch (pl)
        await gate
        return stale
      }
      return fallback(input, init)
    })
    renderRoute('/diary/2026/11', { session: 'none' })

    // The profile says pl; the user switches to en while the first /api/me is held back.
    await language.setLanguage('pl')
    await user.selectOptions(await switcher('Język'), 'en')
    await waitFor(() => expect(i18n.language).toBe('en'))
    release()

    expect(await screen.findByRole('link', { name: 'Diary' })).toBeInTheDocument()
    await new Promise((resolve) => setTimeout(resolve, 50))
    expect(i18n.language).toBe('en')
    expect(localStorage.getItem(LANGUAGE_STORAGE_KEY)).toBe('en')
  })

  it('shows a toast and keeps the language when the Polish bundle fails to load', async () => {
    const user = userEvent.setup()
    const error = vi.spyOn(toast, 'error').mockReturnValue('id')
    const rejection = vi.fn<(reason: unknown) => void>()
    window.addEventListener('unhandledrejection', rejection)
    renderRoute('/diary/2026/11')
    const select = await switcher()
    vi.spyOn(i18n, 'hasResourceBundle').mockReturnValue(false)
    vi.spyOn(i18n, 'changeLanguage').mockRejectedValueOnce(new Error('chunk failed'))

    await user.selectOptions(select, 'pl')

    await waitFor(() => expect(error).toHaveBeenCalledWith('Could not switch the language. Try again.'))
    await new Promise((resolve) => setTimeout(resolve, 20))
    window.removeEventListener('unhandledrejection', rejection)
    expect(rejection).not.toHaveBeenCalled()
    expect(localStorage.getItem(LANGUAGE_STORAGE_KEY)).toBeNull()
    expect(language.hasExplicitLanguageChoice()).toBe(false)
    vi.restoreAllMocks()
  })
})
