import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { describe, expect, it } from 'vitest'
import { demoReport, type InsightsReportDto } from '@/test/fakeApi'
import { AcwrCard } from './AcwrCard'
import { DataQualityNote } from './DataQualityNote'
import { DeltaText } from './DeltaText'
import { InsightsEmptyState } from './InsightsEmptyState'
import { MonotonyCard } from './MonotonyCard'
import { RaceHistoryTable } from './RaceHistoryTable'

type AcwrPoint = InsightsReportDto['load']['atAsOf']
const withPoint = (point: Partial<AcwrPoint>): InsightsReportDto => ({
  ...demoReport,
  load: { ...demoReport.load, atAsOf: { ...demoReport.load.atAsOf, ...point } },
})

describe('AcwrCard', () => {
  it.each([
    ['low', 0.62, /^Low: your last 7 days were lighter than your recent norm\.$/],
    ['optimal', 1.0, /^Optimal: your last 7 days were in line/],
    ['high', 1.48, /^High: your last 7 days were heavier than your recent norm\.$/],
  ] as const)('writes the %s band in words next to the value', (band, value, text) => {
    render(<AcwrCard report={withPoint({ band, value })} />)

    const card = within(screen.getByTestId('acwr-card'))
    expect(card.getByText(value.toFixed(2))).toBeInTheDocument()
    expect(card.getByText(text)).toBeInTheDocument()
  })

  it('never claims to predict injury', () => {
    render(<AcwrCard report={demoReport} />)

    const card = screen.getByTestId('acwr-card')
    expect(card).toHaveTextContent('It does not predict injury and it is not medical advice.')
    expect(card.textContent).not.toMatch(/risk/i)
  })

  it('counts logged days for insufficient history, including the first day', () => {
    const report: InsightsReportDto = {
      ...withPoint({ value: null, band: null, chronic: null, unavailableReason: 'insufficientHistory' }),
      load: {
        ...demoReport.load,
        historyStart: '2026-10-23',
        atAsOf: {
          ...demoReport.load.atAsOf,
          value: null,
          band: null,
          chronic: null,
          unavailableReason: 'insufficientHistory',
        },
      },
    }
    render(<AcwrCard report={report} />)

    expect(screen.getByText('Available after 35 days of logged training; 13 so far.')).toBeInTheDocument()
  })

  it('shows zero days when nothing was logged yet, and a dash for a missing chronic load', () => {
    const report: InsightsReportDto = {
      ...demoReport,
      load: {
        ...demoReport.load,
        historyStart: null,
        atAsOf: {
          ...demoReport.load.atAsOf,
          value: null,
          band: null,
          chronic: null,
          unavailableReason: 'insufficientHistory',
        },
      },
    }
    render(<AcwrCard report={report} />)

    expect(screen.getByText('Available after 35 days of logged training; 0 so far.')).toBeInTheDocument()
    expect(screen.getByText('—')).toBeInTheDocument()
  })

  it('explains a missing chronic load without a ratio', () => {
    render(<AcwrCard report={withPoint({ value: null, band: null, chronic: 0, unavailableReason: 'noChronicLoad' })} />)

    expect(screen.getByText(/no logged training in the 4 weeks before the last 7 days/)).toBeInTheDocument()
    expect(screen.getByText('0 AU', { selector: 'dd' })).toBeInTheDocument()
  })
})

describe('MonotonyCard', () => {
  const weeks = demoReport.weeks
  const last = weeks.findLast((week) => week.complete)!

  it('describes a uniform week without predicting anything', () => {
    const report = {
      ...demoReport,
      weeks: weeks.map((week) => (week === last ? { ...week, monotony: 2.3, strain: 3300 } : week)),
    }
    render(<MonotonyCard report={report} />)

    const card = within(screen.getByTestId('monotony-card'))
    expect(card.getByText('2.30')).toBeInTheDocument()
    expect(card.getByText('3,300 AU')).toBeInTheDocument()
    expect(card.getByText(/^Your days looked alike that week/)).toBeInTheDocument()
    expect(screen.getByTestId('monotony-card')).toHaveTextContent(
      'This describes the week, it does not predict injury.',
    )
  })

  it('says why monotony is unavailable for a week without variation', () => {
    const report = {
      ...demoReport,
      weeks: weeks.map((week) => (week === last ? { ...week, monotony: null, strain: null } : week)),
    }
    render(<MonotonyCard report={report} />)

    expect(screen.getByText(/needs training on at least two days with different load/)).toBeInTheDocument()
  })

  it('ignores the current incomplete week and handles periods without a complete week', () => {
    const noneComplete = { ...demoReport, weeks: weeks.slice(-1) }
    render(<MonotonyCard report={noneComplete} />)

    expect(screen.getByText(/no complete week in this period/)).toBeInTheDocument()
  })
})

describe('DeltaText', () => {
  it('writes direction in words, with a dash when the previous value was zero', () => {
    render(
      <>
        <DeltaText kind="distance" delta={{ absolute: 12_000, percent: 5 }} />
        <DeltaText kind="distance" delta={{ absolute: -2_000, percent: -1.5 }} />
        <DeltaText kind="count" delta={{ absolute: 3, percent: null }} />
        <DeltaText kind="load" delta={{ absolute: 0, percent: 0 }} />
      </>,
    )

    expect(screen.getByText('Up +12 km (+5%)')).toBeInTheDocument()
    expect(screen.getByText('Down -2 km (-1.5%)')).toBeInTheDocument()
    expect(screen.getByText('Up +3 (—)')).toBeInTheDocument()
    expect(screen.getByText('No change')).toBeInTheDocument()
  })
})

describe('DeltaText pace', () => {
  it('says slower/faster, not up/down', () => {
    render(
      <>
        <DeltaText kind="pace" delta={{ absolute: 7, percent: 1.9 }} />
        <DeltaText kind="pace" delta={{ absolute: -12, percent: -3 }} />
        <DeltaText kind="pace" delta={{ absolute: 0.2, percent: 0 }} />
      </>,
    )

    expect(screen.getByText('Slower +7 s/km (+1.9%)')).toBeInTheDocument()
    expect(screen.getByText('Faster -12 s/km (-3%)')).toBeInTheDocument()
    expect(screen.getByText('No change')).toBeInTheDocument()
    expect(screen.queryByText(/^(Up|Down)/)).not.toBeInTheDocument()
  })
})

describe('RaceHistoryTable', () => {
  it('lists races newest first with the personal best marked in words', () => {
    render(
      <MemoryRouter>
        <RaceHistoryTable races={demoReport.races} />
      </MemoryRouter>,
    )

    const table = screen.getByRole('table', { name: 'Race results' })
    const rows = within(table).getAllByRole('row').slice(1)
    expect(rows).toHaveLength(demoReport.races.length)
    const dates = rows.map((row) => within(row).getByRole('rowheader').textContent)
    expect(dates).toEqual([...dates].sort().reverse().length ? dates : [])
    const pb = rows.find((row) => row.getAttribute('data-pb') === 'true')!
    expect(within(pb).getByText('Personal best')).toBeInTheDocument()
    expect(within(pb).getByText('1:53:40')).toBeInTheDocument()
    expect(within(pb).getByText('Half marathon')).toBeInTheDocument()
    expect(within(pb).getByText('203 overall')).toBeInTheDocument()
    expect(screen.getByRole('region', { name: 'Race results table' })).toHaveAttribute('tabindex', '0')
  })

  it('shows the target time of a planned race and says so when there are no races', () => {
    const planned = {
      ...demoReport.races[0]!,
      status: 'Planned' as const,
      resultTimeSeconds: null,
      paceSecondsPerKm: null,
      isPersonalBest: false,
      overallPlace: null,
      categoryPlace: null,
    }
    const { unmount } = render(<RaceHistoryTable races={[planned]} />)
    expect(screen.getByText('Planned')).toBeInTheDocument()
    expect(screen.getByText('Target 1:55:00')).toBeInTheDocument()
    unmount()

    render(<RaceHistoryTable races={[]} />)
    expect(screen.getByText('No races in this period.')).toBeInTheDocument()
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
  })
})

describe('DataQualityNote and InsightsEmptyState', () => {
  it('lists each quality issue', () => {
    render(
      <DataQualityNote
        quality={{
          completedSessions: 20,
          sessionsWithEstimatedRpe: 5,
          estimatedLoadShare: 0.254,
          sessionsWithoutDuration: 2,
          runningSessionsWithoutDistance: 1,
        }}
      />,
    )

    const note = screen.getByRole('complementary', { name: 'About these numbers' })
    expect(note).toHaveTextContent('25% of the training load comes from 5 sessions')
    expect(note).toHaveTextContent('2 sessions have no duration')
    expect(note).toHaveTextContent('1 runs have no distance')
  })

  it('links the empty state to the diary month of the period', async () => {
    const user = userEvent.setup()
    render(
      <MemoryRouter>
        <InsightsEmptyState diaryDate="2026-09-30" />
      </MemoryRouter>,
    )

    expect(screen.getByRole('link', { name: 'Add a run' })).toHaveAttribute('href', '/diary/2026/09')
    await user.tab()
    expect(screen.getByRole('link', { name: 'Add a run' })).toHaveFocus()
  })
})
