import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { supportsWebGl2 } from '@/components/map/webgl'
import { setPhoneViewport } from '@/test/viewport'
import { resetFakeMap } from '@/test/fakeMapState'
import { bareRouteGeoJson, bareRouteSummary, gliwice10kGeoJson, gliwice10kSummary } from '@/test/fixtures/tracks'
import { profilePosition } from './trackGeoJson'
import { TrackViewer } from './TrackViewer'

// The profile is a lazy chunk (Recharts). Under parallel workers its first on-demand transform can exceed findBy's
// timeout, so load it during collection, where no test timeout applies (same approach as src/test/render.tsx).
await import('@/components/map/ElevationProfile')

vi.mock('@/components/map/webgl', () => ({ supportsWebGl2: vi.fn<() => boolean>(() => true) }))
vi.mock('@/components/map/maplibre/MapLibreMap', () => import('@/test/fakeMapProvider'))

beforeAll(() => {
  vi.stubGlobal(
    'ResizeObserver',
    class {
      callback: ResizeObserverCallback
      constructor(callback: ResizeObserverCallback) {
        this.callback = callback
      }
      observe(target: Element) {
        this.callback([{ target, contentRect: { width: 600, height: 160 } } as ResizeObserverEntry], this as never)
      }
      unobserve() {}
      disconnect() {}
    },
  )
})

beforeEach(() => {
  resetFakeMap()
  vi.mocked(supportsWebGl2).mockReturnValue(true)
})

const profile = gliwice10kGeoJson.profile!
const slider = () => screen.findByRole('slider', { name: /^Elevation profile/ })

describe('TrackViewer', () => {
  it('shows the map, the profile and the text alternative together', async () => {
    render(<TrackViewer geoJson={gliwice10kGeoJson} summary={gliwice10kSummary} />)
    expect(await screen.findByRole('region', { name: 'Map of the route' })).toBeInTheDocument()
    expect(await slider()).toBeInTheDocument()
    expect(screen.getByText('Distance').nextElementSibling).toHaveTextContent('10 km')
    expect(screen.getByRole('table', { name: 'Kilometre splits' })).toBeVisible()
  })

  it('moves the map highlight with the keyboard on the profile and clears it again', async () => {
    render(<TrackViewer geoJson={gliwice10kGeoJson} summary={gliwice10kSummary} />)
    const map = await screen.findByTestId('fake-map')
    expect(map).toHaveAttribute('data-highlight', '')

    await userEvent.click(await slider())
    ;(await slider()).focus()
    await userEvent.keyboard('{ArrowRight}{ArrowRight}')
    expect(map).toHaveAttribute('data-highlight', profilePosition(profile[2]!).join(','))

    await userEvent.keyboard('{End}')
    expect(map).toHaveAttribute('data-highlight', profilePosition(profile.at(-1)!).join(','))

    await userEvent.keyboard('{Escape}')
    expect(map).toHaveAttribute('data-highlight', '')
  })

  it('keeps the text alternative when the map cannot be drawn', async () => {
    vi.mocked(supportsWebGl2).mockReturnValue(false)
    render(<TrackViewer geoJson={gliwice10kGeoJson} summary={gliwice10kSummary} />)
    expect(screen.getByRole('region', { name: 'Map of the route' })).toHaveAttribute('data-map-state', 'unavailable')
    expect(screen.getByRole('status')).toHaveTextContent('Map unavailable')
    expect(screen.queryByTestId('fake-map')).not.toBeInTheDocument()
    // The profile is a separate widget and still works; the table and summary are plain markup.
    expect(await slider()).toBeInTheDocument()
    expect(screen.getByRole('table', { name: 'Kilometre splits' })).toBeInTheDocument()
  })

  it('has no profile for a track without elevation and explains the warnings', async () => {
    render(<TrackViewer geoJson={bareRouteGeoJson} summary={bareRouteSummary} warnings={['noElevation']} planned />)
    const map = await screen.findByTestId('fake-map')
    expect(map).toHaveAttribute('data-dashed', 'true')
    expect(screen.queryByRole('slider')).not.toBeInTheDocument()
    expect(screen.getByRole('region', { name: 'Notes about this file' })).toHaveTextContent('no elevation data')
  })

  it('draws a recorded track solid and passes the configured provider settings on', async () => {
    render(
      <TrackViewer
        geoJson={gliwice10kGeoJson}
        summary={gliwice10kSummary}
        maps={{ provider: 'MapLibre', mapLibreStyleUrl: 'https://tiles.example.test/style' }}
      />,
    )
    const map = await screen.findByTestId('fake-map')
    expect(map).toHaveAttribute('data-dashed', 'false')
    expect(map).toHaveAttribute('data-style-url', 'https://tiles.example.test/style')
  })

  it('collapses the splits table on a phone and opens it on a desktop viewport', async () => {
    setPhoneViewport(true)
    const { unmount } = render(<TrackViewer geoJson={gliwice10kGeoJson} summary={gliwice10kSummary} />)
    expect(screen.getByText('Show splits table').closest('details')).not.toHaveAttribute('open')
    unmount()
    setPhoneViewport(false)
    render(<TrackViewer geoJson={gliwice10kGeoJson} summary={gliwice10kSummary} />)
    expect(screen.getByText('Show splits table').closest('details')).toHaveAttribute('open')
  })
})
