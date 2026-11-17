import { act, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { supportsWebGl2 } from '@/components/map/webgl'
import { setLanguage } from '@/i18n/language'
import { resetFakeApi } from '@/test/fake'
import { DEMO_RACE_IDS } from '@/test/fakeApi'
import { resetFakeMap } from '@/test/fakeMapState'
import { renderRoute, signInForTest } from '@/test/render'
import { stubResizeObserver } from '@/test/resizeObserver'
import { setPhoneViewport } from '@/test/viewport'
import '@/i18n/lazyBundles'

vi.mock('@/components/map/webgl', () => ({ supportsWebGl2: vi.fn<() => boolean>(() => true) }))
vi.mock('@/components/map/maplibre/MapLibreMap', () => import('@/test/fakeMapProvider'))

// Every key these screens use must exist in pl/races.json (and maps/runs/...): the test setup fails on a missing key.
beforeEach(async () => {
  stubResizeObserver()
  resetFakeMap()
  setPhoneViewport(false)
  vi.mocked(supportsWebGl2).mockReturnValue(true)
  resetFakeApi()
  signInForTest('guest')
  await act(() => setLanguage('pl'))
})

describe('race proposals in Polish', () => {
  it('lists the proposals of the month with Polish headings, badges and actions', async () => {
    renderRoute('/diary/2026/11/races', { session: 'none' })

    const dialog = await screen.findByRole('dialog', { name: 'Propozycje startów – listopad 2026' })
    await within(dialog).findByText('Demo Gliwice Night 10K')
    const headings = within(dialog)
      .getAllByRole('heading', { level: 3 })
      .map((h) => h.textContent)
      .filter((text) => /2026/.test(text ?? ''))
    expect(headings[0]).toBe('Czwartek, 12 listopada 2026')
    expect(within(dialog).getAllByText('Nowe')).toHaveLength(3)
    expect(within(dialog).getAllByRole('button', { name: /Interesuje mnie/ }).length).toBeGreaterThan(0)
    expect(within(dialog).getByText(/Lista startów zaktualizowana:/)).toBeInTheDocument()
  })

  it('names the day cells with the Polish plural of "propozycja startu"', async () => {
    renderRoute('/diary/2026/11', { session: 'none' })

    await screen.findByRole('grid', { hidden: true })
    const cell = document.querySelector<HTMLElement>('[data-date="2026-11-12"]')!
    await screen.findByText(/1 propozycja startu: Demo Gliwice Night 10K/, { selector: '.sr-only' })
    expect(cell).toHaveAccessibleName(/czwartek, 12 listopada 2026, brak treningów; 1 propozycja startu: Demo Gliwice/)
  })

  it('shows the race details with the Polish sign-up action and facts', async () => {
    const user = userEvent.setup()
    renderRoute(`/diary/2026/11/races/${DEMO_RACE_IDS.night10k}`, { session: 'none' })

    const dialog = await screen.findByRole('dialog', { name: /Demo Gliwice Night 10K/ })
    expect(await within(dialog).findByText('Termin zapisów')).toBeInTheDocument()
    expect(within(dialog).getByText('Organizator')).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'Mam zapis' }))
    expect(await within(dialog).findByRole('form', { name: 'Mam zapis' })).toBeInTheDocument()
    expect(within(dialog).getByText(/To doda zaplanowany start do twojego dziennika w dniu: /)).toBeInTheDocument()
    expect(within(dialog).getByRole('button', { name: 'Dodaj do dziennika' })).toBeInTheDocument()
  })

  it('renders the race interests settings with Polish voivodeship names and the plural count', async () => {
    renderRoute('/settings/races', { session: 'none' })

    expect(await screen.findByRole('heading', { level: 1, name: 'Zainteresowania startami' })).toBeInTheDocument()
    expect(await screen.findByLabelText('Śląskie')).toBeInTheDocument()
    expect(screen.queryByText(/Silesian/)).toBeNull()
    expect(await screen.findByText(/Wybrano \d+ z 16 województw/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Zapisz zainteresowania' })).toBeInTheDocument()
  })
})
