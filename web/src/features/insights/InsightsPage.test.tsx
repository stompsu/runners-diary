import { QueryClientProvider } from '@tanstack/react-query'
import { renderHook, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it } from 'vitest'
import { useInvalidateRuns } from '@/features/runs/runQueries'
import { getFakeApi, resetFakeApi } from '@/test/fake'
import { DEMO_REPORT_TODAY, faults, type InsightsScenario } from '@/test/fakeApi'
import { renderRoute } from '@/test/render'
import { stubResizeObserver } from '@/test/resizeObserver'

const reportRequests = () => getFakeApi().requests.filter((request) => request === 'GET /api/insights/report')

function open(path = '/insights', scenario?: InsightsScenario) {
  stubResizeObserver()
  resetFakeApi({ today: DEMO_REPORT_TODAY, ...(scenario ? { insights: scenario } : {}) })
  return renderRoute(path)
}

async function loaded() {
  return screen.findByTestId('kpi-grid', undefined, { timeout: 10_000 })
}

const kpi = (name: string) => within(document.querySelector(`[data-kpi="${name}"]`) as HTMLElement)

describe('InsightsPage (default view)', () => {
  beforeEach(() => stubResizeObserver())

  it('shows the last 12 weeks against the previous 12, labelled from the response', async () => {
    open()

    await loaded()
    expect(screen.getByRole('heading', { level: 1, name: 'Insights' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Last 12 weeks' })).toHaveAttribute('aria-pressed', 'true')
    expect(screen.getByTestId('period-label').textContent?.replace(/\s/g, ' ')).toBe('Showing Aug 17 – Nov 8, 2026')
    expect(screen.getByTestId('partial-note')).toHaveTextContent(
      'Partial period: data through Nov 4, 2026 (80 of 84 days).',
    )
    expect(screen.getByTestId('comparison-label').textContent?.replace(/\s/g, ' ')).toBe(
      'Compared with May 25 – Aug 12, 2026 (Previous period).',
    )
    expect(getFakeApi().requests).toContain('GET /api/insights/report')
  })

  it('requests the preset from me.today, not the browser clock', async () => {
    stubResizeObserver()
    resetFakeApi({ today: '2030-03-13' })
    renderRoute('/insights')

    await screen.findByTestId('period-label', undefined, { timeout: 10_000 })
    // Monday of 2030-03-13 is 2030-03-11; minus 77 days = 2029-12-24; Sunday = 2030-03-17.
    expect(screen.getByTestId('period-label').textContent?.replace(/\s/g, ' ')).toBe(
      'Showing Dec 24, 2029 – Mar 17, 2030',
    )
  })

  it('shows the six KPIs with changes stated in words, and a dash when there is nothing to divide by', async () => {
    open()

    await loaded()
    expect(kpi('distance').getByText('382.6 km')).toBeInTheDocument()
    expect(kpi('distance').getByText('Up +47.7 km (+14.2%)')).toBeInTheDocument()
    expect(kpi('duration').getByText('37:20:12')).toBeInTheDocument()
    expect(kpi('duration').getByText('Up +5:15:43 (+16.4%)')).toBeInTheDocument()
    expect(kpi('runs').getByText('39')).toBeInTheDocument()
    expect(kpi('elevation').getByText('2,594 m')).toBeInTheDocument()
    expect(kpi('load').getByText('11,677 AU')).toBeInTheDocument()
    expect(kpi('pace').getByText('5:51 /km')).toBeInTheDocument()
    // Pace is derived from both totals: 351.3 vs 344.8 s/km is slower.
    expect(kpi('pace').getByText('Slower +7 s/km (+1.9%)')).toBeInTheDocument()
  })

  it('shows "—" instead of a percentage when the comparison period had nothing', async () => {
    const user = userEvent.setup()
    open()
    await loaded()

    await user.selectOptions(screen.getByLabelText('Compare with'), 'previousYear')

    await waitFor(() => expect(kpi('distance').getByText(/^Up \+382\.6 km \(—\)$/)).toBeInTheDocument())
  })

  it('summarises planned runs', async () => {
    open()

    await loaded()
    const planned = screen.getByRole('region', { name: 'Planned' })
    expect(planned).toHaveTextContent('3 planned runs: 30.3 km, 2:51:54.')
    expect(planned).toHaveTextContent('3 upcoming')
    expect(planned).toHaveTextContent('0 missed')
  })
})

describe('ACWR and monotony cards', () => {
  it('shows the value, the band in words, acute and chronic load and the disclaimer', async () => {
    open()

    await loaded()
    const card = within(screen.getByTestId('acwr-card'))
    expect(card.getByRole('heading', { name: 'Load change vs your recent norm' })).toBeInTheDocument()
    expect(card.getByText('0.80')).toBeInTheDocument()
    expect(card.getByText(/^Optimal: your last 7 days were in line with your recent norm\.$/)).toBeInTheDocument()
    expect(card.getByText('926 AU')).toBeInTheDocument()
    expect(card.getByText('1,155 AU')).toBeInTheDocument()
    expect(card.getByText(/Highest in this period: 1\.79 on Oct 23 \(High\)\./)).toBeInTheDocument()
    expect(card.getByText(/It does not predict injury/)).toBeInTheDocument()
  })

  it('explains insufficient history with the number of days logged so far', async () => {
    open('/insights', 'insufficientHistory')

    await loaded()
    const card = within(screen.getByTestId('acwr-card'))
    expect(card.getByText('Available after 35 days of logged training; 13 so far.')).toBeInTheDocument()
    expect(card.queryByText(/^Optimal/)).not.toBeInTheDocument()
  })

  it('explains a missing chronic load', async () => {
    open('/insights', 'noChronicLoad')

    await loaded()
    expect(within(screen.getByTestId('acwr-card')).getByText(/no logged training in the 4 weeks/)).toBeInTheDocument()
  })

  it('shows monotony and strain of the last complete week, worded as a description rather than a prediction', async () => {
    open()

    await loaded()
    const card = within(screen.getByTestId('monotony-card'))
    expect(card.getByText(/Last complete week: Oct 26 – Nov 1, 2026/)).toBeInTheDocument()
    expect(card.getByText('0.77')).toBeInTheDocument()
    expect(card.getByText('Your days differed between hard and easy that week.')).toBeInTheDocument()
    expect(card.getByText(/does not predict injury|it does not predict injury/i)).toBeInTheDocument()
  })
})

describe('empty and data quality states', () => {
  it('offers to add a run when the period has no completed runs', async () => {
    open('/insights?view=month&at=2025-03-10')

    const empty = await screen.findByTestId('insights-empty', undefined, { timeout: 10_000 })
    expect(within(empty).getByRole('heading', { name: 'No completed runs in this period' })).toBeInTheDocument()
    expect(within(empty).getByRole('link', { name: 'Add a run' })).toHaveAttribute('href', '/diary/2025/03')
    expect(screen.queryByTestId('kpi-grid')).not.toBeInTheDocument()
  })

  it('still shows the summary card in an empty period, so the automatic note can appear', async () => {
    stubResizeObserver()
    resetFakeApi({ today: DEMO_REPORT_TODAY, summary: { scenario: 'template' } })
    renderRoute('/insights?view=month&at=2025-03-10')

    const card = await screen.findByTestId('summary-card', undefined, { timeout: 10_000 })
    expect(screen.getByTestId('insights-empty')).toBeInTheDocument()
    await within(card).findByTestId('summary-badge')
    expect(within(card).queryByRole('button', { name: 'Generate summary' })).not.toBeInTheDocument()
  })

  it('shows the data quality note only when load is estimated', async () => {
    open('/insights', 'estimated')

    await loaded()
    const note = screen.getByRole('complementary', { name: 'About these numbers' })
    expect(note).toHaveTextContent('18% of the training load comes from 6 sessions without a logged effort (RPE)')
    expect(note).toHaveTextContent('1 sessions have no duration')
  })

  it('has no note for clean data', async () => {
    open()

    await loaded()
    expect(screen.queryByRole('complementary', { name: 'About these numbers' })).not.toBeInTheDocument()
  })
})

describe('URL state', () => {
  it('writes the chosen period to the URL, and Back restores the previous one', async () => {
    const user = userEvent.setup()
    const { router } = open()
    await loaded()

    await user.click(screen.getByRole('button', { name: 'This month' }))

    await waitFor(() => expect(router.state.location.search).toBe('?view=month&at=2026-11-04'))
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'This month' })).toHaveAttribute('aria-pressed', 'true'),
    )
    await waitFor(() =>
      expect(screen.getByTestId('period-label').textContent?.replace(/\s/g, ' ')).toBe('Showing Nov 1 – 30, 2026'),
    )

    await router.navigate(-1)

    await waitFor(() => expect(router.state.location.search).toBe(''))
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Last 12 weeks' })).toHaveAttribute('aria-pressed', 'true'),
    )
  })

  it('pages with earlier and later, and loads the matching report', async () => {
    const user = userEvent.setup()
    const { router } = open('/insights?view=month&at=2026-10-15')
    await loaded()

    await user.click(screen.getByRole('button', { name: 'Show earlier period' }))
    await waitFor(() => expect(router.state.location.search).toBe('?view=month&at=2026-09-01'))
    await user.click(screen.getByRole('button', { name: 'Show later period' }))
    await waitFor(() => expect(router.state.location.search).toBe('?view=month&at=2026-10-01'))
  })

  it('hides earlier and later for year to date', async () => {
    open('/insights?view=ytd')

    await loaded()
    expect(screen.queryByRole('button', { name: 'Show earlier period' })).not.toBeInTheDocument()
    expect(screen.getByLabelText('Compare with')).toHaveValue('previousYear')
  })

  it('replaces a ytd URL carrying at, and a period outside the API window', async () => {
    const { router } = open('/insights?view=ytd&at=2025-03-01')
    await loaded()
    await waitFor(() => expect(router.state.location.search).toBe('?view=ytd'))
    expect(router.state.historyAction).toBe('REPLACE')
  })

  it('falls back to the default view when the computed week starts before 1950', async () => {
    const { router } = open('/insights?view=week&at=1950-01-01')
    await loaded()
    await waitFor(() => expect(router.state.location.search).toBe(''))
    expect(router.state.historyAction).toBe('REPLACE')
  })

  it('replaces an invalid URL with the default view', async () => {
    const { router } = open('/insights?view=decade&at=banana')

    await loaded()
    await waitFor(() => expect(router.state.location.search).toBe(''))
    expect(screen.getByRole('button', { name: 'Last 12 weeks' })).toHaveAttribute('aria-pressed', 'true')
    // `replace`: there is no history entry for the invalid URL to go back to.
    expect(router.state.historyAction).toBe('REPLACE')
  })

  it('keeps the comparison in the URL and drops the comparison text for "no comparison"', async () => {
    const user = userEvent.setup()
    const { router } = open()
    await loaded()

    await user.selectOptions(screen.getByLabelText('Compare with'), 'none')

    await waitFor(() => expect(router.state.location.search).toBe('?compare=none'))
    await waitFor(() => expect(screen.queryByTestId('comparison-label')).not.toBeInTheDocument())
    expect(
      within(document.querySelector('[data-kpi="distance"]') as HTMLElement).queryByText(/^Up/),
    ).not.toBeInTheDocument()
  })
})

describe('custom period', () => {
  it('validates the dates and shows messages before asking the API', async () => {
    const user = userEvent.setup()
    const { router } = open()
    await loaded()
    await user.click(screen.getByRole('button', { name: 'Custom' }))
    await waitFor(() => expect(router.state.location.search).toContain('view=custom'))
    const before = reportRequests().length

    const to = screen.getByLabelText('To')
    await user.clear(to)
    await user.type(to, '2025-01-01')
    await user.click(screen.getByRole('button', { name: 'Show period' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('The end date must not be before the start date.')
    expect(to).toHaveAttribute('aria-invalid', 'true')

    const from = screen.getByLabelText('From')
    await user.clear(from)
    await user.type(from, '2024-01-01')
    await user.clear(to)
    await user.type(to, '2026-01-01')
    await user.click(screen.getByRole('button', { name: 'Show period' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('A period can be at most 366 days.')
    expect(reportRequests()).toHaveLength(before)
  })

  it('applies a valid range to the URL', async () => {
    const user = userEvent.setup()
    const { router } = open('/insights?view=custom&from=2026-09-01&to=2026-09-30')
    await loaded()

    const to = screen.getByLabelText('To')
    await user.clear(to)
    await user.type(to, '2026-10-15')
    await user.click(screen.getByRole('button', { name: 'Show period' }))

    await waitFor(() => expect(router.state.location.search).toBe('?view=custom&from=2026-09-01&to=2026-10-15'))
  })
})

describe('refreshing after run changes (M5 AC14)', () => {
  it('refetches the report when runs change, and shows the new numbers', async () => {
    const { queryClient } = open()
    await loaded()
    expect(kpi('runs').getByText('39')).toBeInTheDocument()
    const before = reportRequests().length

    getFakeApi().setInsightsScenario('empty')
    const { result } = renderHook(() => useInvalidateRuns(), {
      wrapper: ({ children }) => <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>,
    })
    await result.current('run-1')

    await waitFor(() => expect(reportRequests().length).toBeGreaterThan(before))
    expect(await screen.findByTestId('insights-empty')).toBeInTheDocument()
  })
})

describe('request errors', () => {
  it('shows a retryable error when the report cannot be loaded', async () => {
    const user = userEvent.setup()
    stubResizeObserver()
    resetFakeApi({ today: DEMO_REPORT_TODAY })
    getFakeApi().addFault(faults.status({ method: 'GET', path: '/api/insights/report' }, 500, 1))
    renderRoute('/insights')

    expect(await screen.findByRole('alert', undefined, { timeout: 10_000 })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Try again' }))

    await loaded()
  })
})

describe('AI summary card on the page', () => {
  it('sits below the KPI grid and loads the stored summary for the shown period', async () => {
    open()

    const grid = await loaded()
    const card = await screen.findByTestId('summary-card')
    expect(grid.compareDocumentPosition(card) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(within(card).getByRole('heading', { level: 2, name: 'AI summary' })).toBeInTheDocument()
    await within(card).findByRole('button', { name: 'Generate summary' })
    expect(getFakeApi().requests).toContain('GET /api/insights/summary')
  })

  it('loads the summary of another period when the period changes (and aborts nothing it did not start)', async () => {
    const user = userEvent.setup()
    open()
    await loaded()
    await screen.findByRole('button', { name: 'Generate summary' })

    await user.click(screen.getByRole('button', { name: 'Show earlier period' }))

    await waitFor(() =>
      expect(getFakeApi().requests.filter((request) => request === 'GET /api/insights/summary').length).toBe(2),
    )
  })
})
