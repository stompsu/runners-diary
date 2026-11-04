import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fakeMap, resetFakeMap } from '@/test/fakeMapState'
import { gliwice10kGeoJson } from '@/test/fixtures/tracks'
import { resetAzureMapsFallback } from './azure/azureSession'
import { MapView } from './MapView'
import { supportsWebGl2 } from './webgl'

vi.mock('./webgl', () => ({ supportsWebGl2: vi.fn<() => boolean>(() => true) }))
vi.mock('./maplibre/MapLibreMap', () => import('@/test/fakeMapProvider'))
vi.mock('./azure/AzureMapsMap', () => import('@/test/fakeAzureMapProvider'))

const region = () => screen.getByRole('region', { name: 'Map of the route' })

beforeEach(() => {
  resetFakeMap()
  resetAzureMapsFallback()
  vi.mocked(supportsWebGl2).mockReturnValue(true)
})

describe('MapView', () => {
  it('lazily loads the provider and reports ready, with the vertex count as an attribute', async () => {
    render(<MapView ariaLabel="Map of the route" track={gliwice10kGeoJson} />)
    expect(await screen.findByTestId('fake-map')).toBeInTheDocument()
    await vi.waitFor(() => expect(region()).toHaveAttribute('data-map-state', 'ready'))
    expect(region()).toHaveAttribute('data-track-vertices', '50')
    expect(screen.queryByText('Loading map…')).not.toBeInTheDocument()
  })

  it('shows a loading note until the provider reports', async () => {
    fakeMap.state = 'loading'
    render(<MapView ariaLabel="Map of the route" />)
    expect(await screen.findByTestId('fake-map')).toBeInTheDocument()
    expect(region()).toHaveAttribute('data-map-state', 'loading')
    expect(screen.getByText('Loading map…')).toBeInTheDocument()
    expect(region()).toHaveAttribute('data-track-vertices', '0')
  })

  it('shows the accessible fallback panel without WebGL2 and never loads the engine', async () => {
    vi.mocked(supportsWebGl2).mockReturnValue(false)
    render(<MapView ariaLabel="Map of the route" track={gliwice10kGeoJson} />)
    expect(region()).toHaveAttribute('data-map-state', 'unavailable')
    expect(screen.getByRole('status')).toHaveTextContent('Map unavailable')
    expect(screen.getByRole('status')).toHaveTextContent('WebGL 2 is not available')
    expect(screen.queryByTestId('fake-map')).not.toBeInTheDocument()
  })

  it('shows the panel when the engine or the style fails, without unmounting the provider', async () => {
    fakeMap.state = 'error'
    render(<MapView ariaLabel="Map of the route" />)
    await vi.waitFor(() => expect(region()).toHaveAttribute('data-map-state', 'error'))
    expect(screen.getByRole('status')).toHaveTextContent('could not be loaded')
    expect(screen.getByTestId('fake-map')).toBeInTheDocument()
  })

  it('forwards the provider-neutral props, including the configured style URL', async () => {
    render(
      <MapView
        ariaLabel="Map of the route"
        track={gliwice10kGeoJson}
        highlight={[18.68, 50.29]}
        marker={{ latitude: 50.29, longitude: 18.68 }}
        fitTo="track"
        dashed
        styleUrl="https://tiles.example.test/styles/custom"
      />,
    )
    const provider = await screen.findByTestId('fake-map')
    expect(provider).toHaveAttribute('data-style-url', 'https://tiles.example.test/styles/custom')
    expect(provider).toHaveAttribute('data-highlight', '18.68,50.29')
    expect(provider).toHaveAttribute('data-marker', '50.29,18.68')
    expect(provider).toHaveAttribute('data-fit-to', 'track')
    expect(provider).toHaveAttribute('data-dashed', 'true')
  })

  it('uses MapLibre by default and by an unknown or incomplete configuration, never loading the Azure Maps provider', async () => {
    const { unmount } = render(
      <MapView ariaLabel="Map of the route" provider="MapLibre" azureMapsDomain="eu.atlas.microsoft.com" />,
    )
    expect(await screen.findByTestId('fake-map')).toBeInTheDocument()
    unmount()

    // Azure Maps without its domain (misconfigured client-config) is not usable: MapLibre.
    render(<MapView ariaLabel="Map of the route" provider="AzureMaps" azureMapsDomain={null} />)
    expect(await screen.findByTestId('fake-map')).toBeInTheDocument()
    expect(screen.queryByTestId('fake-azure-map')).not.toBeInTheDocument()
  })

  it('uses the Azure Maps provider when client-config selects it, handing it the domain', async () => {
    render(<MapView ariaLabel="Map of the route" provider="AzureMaps" azureMapsDomain="eu.atlas.microsoft.com" />)

    const azure = await screen.findByTestId('fake-azure-map')
    expect(azure).toHaveAttribute('data-domain', 'eu.atlas.microsoft.com')
    expect(screen.queryByTestId('fake-map')).not.toBeInTheDocument()
    await vi.waitFor(() => expect(region()).toHaveAttribute('data-map-state', 'ready'))
  })

  it('falls back to MapLibre for the rest of the session when Azure Maps cannot get a token', async () => {
    const user = userEvent.setup()
    const { unmount } = render(
      <MapView ariaLabel="Map of the route" provider="AzureMaps" azureMapsDomain="eu.atlas.microsoft.com" />,
    )
    await user.click(await screen.findByRole('button', { name: 'Fail token' }))

    expect(await screen.findByTestId('fake-map')).toBeInTheDocument()
    expect(screen.queryByTestId('fake-azure-map')).not.toBeInTheDocument()
    await vi.waitFor(() => expect(region()).toHaveAttribute('data-map-state', 'ready'))
    unmount()

    // A map created later in the same session does not try Azure Maps again.
    render(<MapView ariaLabel="Map of the route" provider="AzureMaps" azureMapsDomain="eu.atlas.microsoft.com" />)
    expect(await screen.findByTestId('fake-map')).toBeInTheDocument()
    expect(screen.queryByTestId('fake-azure-map')).not.toBeInTheDocument()
  })

  it("does not show the failed provider's error state after the fallback", async () => {
    const user = userEvent.setup()
    fakeMap.state = 'error'
    render(<MapView ariaLabel="Map of the route" provider="AzureMaps" azureMapsDomain="eu.atlas.microsoft.com" />)
    await vi.waitFor(() => expect(region()).toHaveAttribute('data-map-state', 'error'))

    fakeMap.state = 'ready'
    await user.click(screen.getByRole('button', { name: 'Fail token' }))

    await vi.waitFor(() => expect(region()).toHaveAttribute('data-map-state', 'ready'))
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })

  it('passes picks from the map through to the caller', async () => {
    const onPick = vi.fn<(position: { latitude: number; longitude: number }) => void>()
    render(<MapView ariaLabel="Pick a place" onPick={onPick} fitTo="marker" />)
    await userEvent.click(await screen.findByRole('button', { name: 'Pick here' }))
    expect(onPick).toHaveBeenCalledWith({ latitude: 50.3, longitude: 18.7 })
  })
})
