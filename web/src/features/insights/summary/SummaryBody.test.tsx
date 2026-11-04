import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import '@/i18n/lazyBundles'
import type { SummaryContent, SummaryResponse } from '../insightsTypes'
import { SummaryBody } from './SummaryBody'
import { sampleSummaryContent } from '@/test/fakeApi'

function show(patch: Partial<SummaryContent>) {
  const summary = { ...sampleSummaryContent('model', '2026-11-04T09:00:00Z'), ...patch }
  const response: SummaryResponse = {
    period: { from: '2026-08-17', to: '2026-11-08', compare: 'previous', lang: 'en' },
    state: 'fresh',
    summary,
    generation: { availability: 'available', used: 0, limit: 3, resetsAt: null, scope: 'account' },
  }
  return render(<SummaryBody response={response} summary={summary} zone="Europe/Warsaw" />)
}

describe('SummaryBody text safety', () => {
  it('renders markup in a headline or list item as text, never as elements', () => {
    const attack = '<img src=x onerror="alert(1)"> <b>bold</b> {totals.runCount}'
    const { container } = show({
      headline: { text: attack, keys: ['totals.runCount'] },
      wentWell: [{ text: '<script>alert(1)</script>', keys: [] }],
    })

    expect(container.querySelector('img, b, script')).toBeNull()
    expect(screen.getByRole('heading', { level: 3 }).textContent).toBe('<img src=x onerror="alert(1)"> <b>bold</b> 39')
    expect(screen.getByText('<script>alert(1)</script>')).toBeInTheDocument()
  })

  it('never shows a brace, even for unknown keys, nested tokens or keys named like prototype members', () => {
    const { container } = show({
      headline: { text: 'Ran {nope} and {{totals.runCount}} {constructor} {__proto__} {', keys: [] },
      suggestion: { text: 'Do {unknown.key} } more', keys: [] },
    })

    expect(container.textContent).not.toMatch(/[{}]/)
    expect(screen.getByRole('heading', { level: 3 }).textContent).toBe('Ran — and 39 — —')
  })

  it('omits empty lists and the suggestion when the summary has none', () => {
    show({ wentWell: [], watchOuts: [], dataNotes: [], suggestion: null })

    expect(screen.queryByRole('heading', { level: 4 })).not.toBeInTheDocument()
  })
})
