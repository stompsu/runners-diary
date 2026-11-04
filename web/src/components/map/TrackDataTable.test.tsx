import { render, screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { bareRouteGeoJson, bareRouteSummary, gliwice10kGeoJson, gliwice10kSummary } from '@/test/fixtures/tracks'
import { TrackDataTable } from './TrackDataTable'

describe('TrackDataTable', () => {
  it('lists the summary in km, h:mm:ss, min/km and metres', () => {
    render(<TrackDataTable summary={gliwice10kSummary} warnings={[]} splits={gliwice10kGeoJson.splits} />)
    const value = (label: string) => screen.getByText(label).nextElementSibling
    expect(value('Distance')).toHaveTextContent('10 km')
    expect(value('Elapsed time')).toHaveTextContent('0:49:30')
    expect(value('Moving time')).toHaveTextContent('0:49:25')
    expect(value('Average pace')).toHaveTextContent('4:57 /km')
    expect(value('Elevation gain')).toHaveTextContent('15 m')
    expect(value('Lowest point')).toHaveTextContent('214 m')
    expect(value('Highest point')).toHaveTextContent('231 m')
  })

  it('renders the splits as a table with headers, a numbered row per km and a short last row', () => {
    render(<TrackDataTable summary={gliwice10kSummary} warnings={[]} splits={gliwice10kGeoJson.splits} splitsOpen />)
    const table = screen.getByRole('table', { name: 'Kilometre splits' })
    expect(
      within(table)
        .getAllByRole('columnheader')
        .map((h) => h.textContent),
    ).toEqual(['Km', 'Time', 'Pace', 'Elevation change'])
    const rows = within(table).getAllByRole('row').slice(1)
    expect(rows).toHaveLength(11)
    expect(within(rows[0]!).getByRole('rowheader')).toHaveTextContent('1')
    expect(within(rows[10]!).getByRole('rowheader')).toHaveTextContent('0.005 km')
  })

  it('shows "Not available" for untimed and elevation-less values instead of zeros', () => {
    render(<TrackDataTable summary={bareRouteSummary} warnings={[]} splits={bareRouteGeoJson.splits} splitsOpen />)
    expect(screen.getByText('Elapsed time').nextElementSibling).toHaveTextContent('Not available')
    expect(screen.getByText('Average pace').nextElementSibling).toHaveTextContent('Not available')
    expect(screen.getByText('Elevation gain').nextElementSibling).toHaveTextContent('Not available')
    const firstRow = within(screen.getByRole('table')).getAllByRole('row')[1]!
    expect(
      within(firstRow)
        .getAllByRole('cell')
        .map((c) => c.textContent),
    ).toEqual(['Not available', 'Not available', 'Not available'])
  })

  it('explains file warnings in words', () => {
    render(<TrackDataTable summary={bareRouteSummary} warnings={['noElevation', 'routeConverted']} splits={[]} />)
    const notes = screen.getByRole('region', { name: 'Notes about this file' })
    expect(within(notes).getAllByRole('listitem')).toHaveLength(2)
    expect(notes).toHaveTextContent('no elevation data')
    expect(notes).toHaveTextContent('planned route')
  })

  it('omits the notes section when there are no warnings and says so when there are no splits', () => {
    render(<TrackDataTable summary={gliwice10kSummary} warnings={[]} splits={[]} splitsOpen />)
    expect(screen.queryByRole('region', { name: 'Notes about this file' })).not.toBeInTheDocument()
    expect(screen.getByText('No splits for this track.')).toBeInTheDocument()
  })

  it('collapses the splits by default (phone) and the toggle is a real disclosure', () => {
    render(<TrackDataTable summary={gliwice10kSummary} warnings={[]} splits={gliwice10kGeoJson.splits} />)
    expect(screen.getByText('Show splits table').closest('details')).not.toHaveAttribute('open')
  })
})
