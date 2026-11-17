import { act, screen } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import i18n from '@/i18n'
import enCalendar from '@/i18n/en/calendar.json'
import { setLanguage } from '@/i18n/language'
import { renderRoute } from '@/test/render'

describe('month header in Polish (AC6)', () => {
  afterEach(() => {
    i18n.removeResourceBundle('pl', 'calendar')
  })

  it('capitalises the month in the title and the picker trigger, and keeps the grid Monday-first', async () => {
    // `pl/calendar.json` is a Part B deliverable: a stand-in with the real title pattern and the other keys in English.
    i18n.addResourceBundle('pl', 'calendar', { ...enCalendar, title: 'Dziennik – {{month}}' }, true, true)
    await act(() => setLanguage('pl'))

    renderRoute('/diary/2026/11')

    expect(await screen.findByRole('heading', { level: 1, name: 'Dziennik – Listopad 2026' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /listopad 2026/ })).toHaveTextContent('Listopad 2026')
    const headers = (await screen.findAllByRole('columnheader')).map((h) => h.textContent)
    expect(headers[0]).toBe('Week') // the week-number column, then Monday first
    expect(headers[1]).toMatch(/pon/i)
    expect(headers[7]).toMatch(/niedz|nd/i)
  })
})
