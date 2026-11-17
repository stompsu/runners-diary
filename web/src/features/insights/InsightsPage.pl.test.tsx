import { act, screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import i18n from '@/i18n'
import { setLanguage } from '@/i18n/language'
import { resetFakeApi } from '@/test/fake'
import { DEMO_REPORT_TODAY } from '@/test/fakeApi'
import { renderRoute } from '@/test/render'
import { stubResizeObserver } from '@/test/resizeObserver'

describe('Insights in Polish (every key of the namespaces it renders exists in pl, enforced by the test setup)', () => {
  it('shows the Polish page, period picker, KPIs and notes', async () => {
    stubResizeObserver()
    resetFakeApi({ today: DEMO_REPORT_TODAY })
    await act(() => setLanguage('pl'))

    renderRoute('/insights')

    const grid = await screen.findByTestId('kpi-grid', undefined, { timeout: 10_000 })
    expect(screen.getByRole('heading', { level: 1, name: 'Analiza' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Ostatnie 12 tygodni' })).toHaveAttribute('aria-pressed', 'true')
    expect(screen.getByTestId('partial-note')).toHaveTextContent(/Okres częściowy: dane do .* \(80 z 84 dni\)\./)
    expect(screen.getByTestId('period-label').textContent).toMatch(/^Wyświetlany okres: /)
    expect(screen.getByTestId('comparison-label').textContent).toMatch(/\(Poprzedni okres\)\.$/)
    expect(within(grid).getByText('Średnie tempo')).toBeInTheDocument()
    expect(within(grid).getByText('Przewyższenie')).toBeInTheDocument()
  })
})

describe('lazy Polish bundles', () => {
  it('are registered by lazyBundles for every lazy namespace that has a pl file', async () => {
    await import('@/i18n/lazyBundles')
    for (const ns of ['maps', 'tracks', 'insights']) {
      expect([ns, i18n.hasResourceBundle('pl', ns)]).toEqual([ns, true])
    }
  })
})
