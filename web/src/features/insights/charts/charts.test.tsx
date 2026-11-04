import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeAll, describe, expect, it } from 'vitest'
import { demoReport, type InsightsReportDto } from '@/test/fakeApi'
import { stubResizeObserver } from '@/test/resizeObserver'
import { ChartFigure } from '../ChartFigure'
import AcwrChart from './AcwrChart'
import LoadChart from './LoadChart'
import PaceTrendChart from './PaceTrendChart'
import TypeDistributionChart from './TypeDistributionChart'
import WeeklyVolumeChart from './WeeklyVolumeChart'

beforeAll(() => stubResizeObserver())

/** The figure of a chart, its table and the table's body rows. */
function figure(name: string) {
  const element = screen.getByRole('figure', { name })
  return {
    element,
    within: within(element),
    rows: () => within(element).getAllByRole('row').slice(1),
    headers: () =>
      within(element)
        .getAllByRole('columnheader')
        .map((header) => header.textContent),
  }
}

describe('ChartFigure', () => {
  it('is a figure with a caption, a text summary, a hidden decorative chart and a data table', async () => {
    const user = userEvent.setup()
    render(
      <ChartFigure
        title="Weekly thing"
        summary="Highest week: Oct 12."
        table={{
          headers: ['Week', 'Distance'],
          rows: [
            ['Oct 5', '40 km'],
            ['Oct 12', '50 km'],
          ],
        }}
      >
        <svg data-testid="chart" />
      </ChartFigure>,
    )

    const fig = screen.getByRole('figure', { name: 'Weekly thing' })
    expect(fig).toHaveAccessibleDescription('Highest week: Oct 12.')
    expect(screen.getByTestId('chart').closest('[aria-hidden="true"]')).not.toBeNull()
    expect(fig.querySelector('details')).not.toHaveAttribute('open')

    await user.click(within(fig).getByText('Show data'))

    const table = within(fig).getByRole('table', { name: 'Weekly thing' })
    expect(
      within(table)
        .getAllByRole('columnheader')
        .map((cell) => cell.textContent),
    ).toEqual(['Week', 'Distance'])
    expect(within(table).getByRole('rowheader', { name: 'Oct 12' })).toBeInTheDocument()
    expect(within(table).getByRole('cell', { name: '50 km' })).toBeInTheDocument()
  })

  it('puts the table in a labelled, keyboard-focusable scroll region', async () => {
    const user = userEvent.setup()
    render(
      <ChartFigure title="Things" summary="s" table={{ headers: ['A'], rows: [['1']] }}>
        <svg />
      </ChartFigure>,
    )
    await user.click(screen.getByText('Show data'))

    const region = screen.getByRole('region', { name: 'Things: data table' })
    expect(region).toHaveAttribute('tabindex', '0')
    await user.tab() // from the open summary into the scroll region
    expect(region).toHaveFocus()
  })
})

describe('WeeklyVolumeChart', () => {
  it('lists one table row per week with completed and planned values, and keeps the chart out of the tree', async () => {
    const user = userEvent.setup()
    const { container } = render(<WeeklyVolumeChart report={demoReport} />)

    const fig = figure('Volume by week')
    expect(fig.element).toHaveAccessibleDescription(
      'Highest week: Week of Aug 17 with 51.9 km; average 31.9 km per week over 12 weeks.',
    )
    await user.click(fig.within.getByText('Show data'))
    expect(fig.headers()).toEqual(['Period', 'Completed', 'Planned'])
    expect(fig.rows()).toHaveLength(12)
    expect(within(fig.rows()[0]!).getByRole('rowheader')).toHaveTextContent('Week of Aug 17')
    expect(
      within(fig.rows()[0]!)
        .getAllByRole('cell')
        .map((cell) => cell.textContent),
    ).toEqual(['51.9 km', '0 km'])
    expect(container.querySelector('svg.recharts-surface')?.closest('[aria-hidden="true"]')).not.toBeNull()
    // Nothing inside the hidden chart is a tab stop (aria-hidden content must not be focusable).
    const hidden = container.querySelector('[aria-hidden="true"]')!
    expect(hidden.querySelector('[tabindex]:not([tabindex^="-"])')).toBeNull()
  })

  it('switches the measure and states planned bars in a legend', async () => {
    const user = userEvent.setup()
    render(<WeeklyVolumeChart report={demoReport} />)

    const group = screen.getByRole('group', { name: 'Measure' })
    await user.click(within(group).getByRole('button', { name: 'Load' }))

    expect(within(group).getByRole('button', { name: 'Load' })).toHaveAttribute('aria-pressed', 'true')
    expect(within(group).getByRole('button', { name: 'Distance' })).toHaveAttribute('aria-pressed', 'false')
    await user.click(screen.getByText('Show data'))
    expect(figure('Volume by week').headers()).toEqual(['Period', 'Completed'])
    expect(screen.getByRole('rowheader', { name: 'Week of Aug 17' }).nextSibling).toHaveTextContent('1,441 AU')

    await user.click(within(group).getByRole('button', { name: 'Time' }))
    expect(screen.getByRole('rowheader', { name: 'Week of Aug 17' }).nextSibling).toHaveTextContent('5:02:09')
  })

  it('uses months for periods longer than 16 weeks', async () => {
    const user = userEvent.setup()
    const longPeriod: InsightsReportDto = { ...demoReport, period: { ...demoReport.period, days: 200 } }
    render(<WeeklyVolumeChart report={longPeriod} />)

    const fig = figure('Volume by month')
    await user.click(fig.within.getByText('Show data'))
    expect(fig.rows()).toHaveLength(demoReport.months.length)
    expect(fig.within.getByRole('rowheader', { name: 'August 2026' })).toBeInTheDocument()
  })

  it('shows planned distance next to completed when there is some', async () => {
    const user = userEvent.setup()
    render(<WeeklyVolumeChart report={demoReport} />)

    await user.click(screen.getByText('Show data'))
    const current = screen.getByRole('rowheader', { name: 'Week of Nov 2' }).parentElement!
    expect(within(current).getAllByRole('cell')[1]).not.toHaveTextContent('0 km')
    expect(screen.getByText('Hatched bars are planned runs not yet completed.')).toBeInTheDocument()
  })

  it('says there is no data when every week is empty', () => {
    render(
      <WeeklyVolumeChart
        report={{
          ...demoReport,
          weeks: demoReport.weeks.map((w) => ({ ...w, completed: { ...w.completed, distanceMeters: 0 } })),
        }}
      />,
    )
    expect(screen.getByRole('figure', { name: 'Volume by week' })).toHaveAccessibleDescription(
      'No data for this period.',
    )
  })
})

describe('LoadChart and AcwrChart', () => {
  it('summarise the last day and list every day', async () => {
    const user = userEvent.setup()
    render(
      <>
        <LoadChart report={demoReport} />
        <AcwrChart report={demoReport} />
      </>,
    )

    const load = figure('Acute and chronic load')
    expect(load.element).toHaveAccessibleDescription(
      'On Nov 4 the acute load was 926 AU and the chronic load 1,155 AU.',
    )
    const ratio = figure('Load change over time')
    expect(ratio.element).toHaveAccessibleDescription(
      'On Nov 4 the ratio was 0.80 (Optimal). It was low on 13 days, optimal on 48 and high on 19.',
    )
    await user.click(load.within.getByText('Show data'))
    await user.click(ratio.within.getByText('Show data'))
    expect(load.rows()).toHaveLength(demoReport.load.daily.length)
    expect(ratio.headers()).toEqual(['Date', 'Ratio', 'Band'])
    // The band is written out for each day, so colour is never the only signal.
    expect(
      within(ratio.rows()[0]!)
        .getAllByRole('cell')
        .map((cell) => cell.textContent),
    ).toEqual(['1.32', 'High'])
  })

  it('handles a day without a ratio', () => {
    const last = demoReport.load.daily.at(-1)!
    const daily = [...demoReport.load.daily.slice(0, -1), { ...last, chronic: null, acwr: null, band: null }]
    render(<AcwrChart report={{ ...demoReport, load: { ...demoReport.load, daily } }} />)

    expect(figure('Load change over time').element).toHaveAccessibleDescription('No ratio is available on Nov 4.')
  })
})

describe('TypeDistributionChart', () => {
  it('shows the share of time per run type with names and numbers in the table', async () => {
    const user = userEvent.setup()
    render(<TypeDistributionChart report={demoReport} />)

    const fig = figure('Time by run type')
    expect(fig.element).toHaveAccessibleDescription(/^Easy run took the largest share|Long run took the largest share/)
    await user.click(fig.within.getByText('Show data'))
    expect(fig.headers()).toEqual(['Run type', 'Runs', 'Distance', 'Time', 'Share of time', 'Load'])
    const shares = fig.rows().map((row) => within(row).getAllByRole('cell')[3]!.textContent)
    expect(shares.every((share) => /^\d+%$/.test(share ?? ''))).toBe(true)
    expect(fig.within.getByRole('rowheader', { name: 'Long run' })).toBeInTheDocument()
  })
})

describe('PaceTrendChart', () => {
  it('states the slope in words, faster being negative', async () => {
    const user = userEvent.setup()
    render(<PaceTrendChart report={demoReport} />)

    const fig = figure('Easy-run pace')
    expect(fig.element).toHaveAccessibleDescription('Easy-run pace got faster by 0.5 s/km per week on average.')
    await user.click(fig.within.getByText('Show data'))
    expect(fig.rows()).toHaveLength(12)
    expect(
      within(fig.rows()[0]!)
        .getAllByRole('cell')
        .map((cell) => cell.textContent),
    ).toEqual(['2', '14.5 km', '5:46 /km', '145'])
  })

  it('says slower when the slope is positive, and when there are too few weeks for a trend', () => {
    const slower = { ...demoReport, paceTrend: { ...demoReport.paceTrend, slopeSecondsPerKmPerWeek: 2.04 } }
    const { unmount } = render(<PaceTrendChart report={slower} />)
    expect(screen.getByRole('figure', { name: 'Easy-run pace' })).toHaveAccessibleDescription(
      'Easy-run pace got slower by 2 s/km per week on average.',
    )
    unmount()

    const few = {
      ...demoReport,
      paceTrend: {
        ...demoReport.paceTrend,
        slopeSecondsPerKmPerWeek: null,
        weeks: demoReport.paceTrend.weeks.slice(0, 2),
        excludedRunCount: 3,
      },
    }
    render(<PaceTrendChart report={few} />)
    expect(screen.getByRole('figure', { name: 'Easy-run pace' })).toHaveAccessibleDescription(
      'Not enough weeks with easy runs for a trend; it needs at least 4. 3 easy runs were left out (trail surface, under 1 km or an unusual pace).',
    )
  })

  it('explains why there is no pace card when the period has no easy runs', () => {
    render(
      <PaceTrendChart
        report={{ ...demoReport, paceTrend: { ...demoReport.paceTrend, weeks: [], slopeSecondsPerKmPerWeek: null } }}
      />,
    )

    expect(screen.queryByRole('figure')).not.toBeInTheDocument()
    expect(screen.getByTestId('pace-empty')).toHaveTextContent('No easy runs in this period.')
    expect(screen.getByTestId('pace-empty')).toHaveTextContent('trail runs and very short runs are left out')
  })
})
