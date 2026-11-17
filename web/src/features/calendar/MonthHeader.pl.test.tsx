import { act, screen, waitFor, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { setLanguage } from '@/i18n/language'
import { renderRoute } from '@/test/render'

describe('Diary page in Polish (AC6, real strings)', () => {
  it('shows the Polish shell, the capitalised month title and picker, and a Monday-first grid', async () => {
    await act(() => setLanguage('pl'))

    renderRoute('/diary/2026/11')

    expect(await screen.findByRole('heading', { level: 1, name: 'Dziennik – Listopad 2026' })).toBeInTheDocument()
    const nav = screen.getByRole('navigation', { name: 'Główna nawigacja' })
    expect(within(nav).getByRole('link', { name: 'Dziennik' })).toBeInTheDocument()
    expect(within(nav).getByRole('link', { name: 'Analiza' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'listopad 2026, wybierz miesiąc i rok' })).toHaveTextContent(
      'Listopad 2026',
    )
    expect(screen.getByRole('link', { name: 'Poprzedni miesiąc' })).toBeInTheDocument()

    const grid = await screen.findByRole('grid', { name: 'Kalendarz: listopad 2026' })
    const headers = within(grid)
      .getAllByRole('columnheader')
      .map((h) => h.textContent)
    expect(headers[0]).toBe('Tydz.')
    expect(headers[1]).toMatch(/^pon/i)
    expect(headers[7]).toMatch(/^niedz|^nd/i)
  })

  it('names day cells with the right Polish plural form and the run type and status', async () => {
    await act(() => setLanguage('pl'))

    renderRoute('/diary/2026/11')

    const grid = await screen.findByRole('grid', { name: 'Kalendarz: listopad 2026' })
    const names = () =>
      within(grid)
        .getAllByRole('gridcell')
        .map((cell) => cell.querySelector('.sr-only')?.textContent ?? '')
    // The runs load after the grid renders: a single run is the `_one` form, with a Polish type and status.
    await waitFor(() => expect(names().some((n) => / 1 trening: .*(Zaplanowany|Wykonany)/.test(n))).toBe(true))
    expect(names().some((n) => /brak treningów$/.test(n))).toBe(true)
  })

  it('opens the Day panel with a capitalised Polish day title', async () => {
    await act(() => setLanguage('pl'))

    renderRoute('/diary/2026/11/day/2026-11-14')

    expect(await screen.findByRole('dialog', { name: 'Sobota, 14 listopada 2026' })).toBeInTheDocument()
  })
})
