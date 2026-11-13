import { act, render } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { gliwice10kGeoJson } from '@/test/fixtures/tracks'
import AzureMapsMap from './AzureMapsMap'
import { resetAzureMapsFallback, useAzureMapsFailed } from './azureSession'
import { getMapsToken } from './mapsToken'

vi.mock('azure-maps-control/dist/atlas.min.css', () => ({}))
vi.mock('./mapsToken', () => ({ getMapsToken: vi.fn<() => Promise<string>>() }))

type Spy = ReturnType<typeof vi.fn<(...args: unknown[]) => void>>

interface FakeMapInstance {
  container: unknown
  options: Record<string, unknown>
  handlers: Map<string, (event?: unknown) => void>
  disposed: boolean
  setCamera: Spy
  markers: { add: Spy; remove: Spy }
  layers: { add: Spy; remove: Spy }
}

const maps = vi.hoisted(() => ({ created: [] as FakeMapInstance[], throwOnCreate: false }))

vi.mock('azure-maps-control', () => {
  class FakeMap {
    container: unknown
    options: Record<string, unknown>
    handlers = new Map<string, (event?: unknown) => void>()
    disposed = false
    setCamera = vi.fn<(...args: unknown[]) => void>()
    getCamera = () => ({ bounds: [18, 50, 19, 51] })
    markers = { add: vi.fn<(...args: unknown[]) => void>(), remove: vi.fn<(...args: unknown[]) => void>() }
    layers = { add: vi.fn<(...args: unknown[]) => void>(), remove: vi.fn<(...args: unknown[]) => void>() }
    sources = { add: vi.fn<(...args: unknown[]) => void>(), remove: vi.fn<(...args: unknown[]) => void>() }
    controls = { add: vi.fn<(...args: unknown[]) => void>() }
    events = { add: (type: string, handler: (event?: unknown) => void) => this.handlers.set(type, handler) }
    constructor(container: unknown, options: Record<string, unknown>) {
      if (maps.throwOnCreate) throw new Error('boom')
      this.container = container
      this.options = options
      maps.created.push(this as unknown as FakeMapInstance)
    }
    dispose() {
      this.disposed = true
    }
  }
  class HtmlMarker {
    options: Record<string, unknown>
    element = document.createElement('div')
    constructor(options: Record<string, unknown>) {
      this.options = options
    }
    getElement() {
      return this.element
    }
  }
  class DataSource {
    add = vi.fn<(...args: unknown[]) => void>()
  }
  class LineLayer {
    options: Record<string, unknown>
    constructor(_source: unknown, _id: unknown, options: Record<string, unknown>) {
      this.options = options
    }
  }
  class Polygon {
    coordinates: unknown
    constructor(coordinates: unknown) {
      this.coordinates = coordinates
    }
  }
  class PolygonLayer {
    options: Record<string, unknown>
    constructor(_source: unknown, _id: unknown, options: Record<string, unknown>) {
      this.options = options
    }
  }
  class MultiLineString {
    coordinates: unknown
    constructor(coordinates: unknown) {
      this.coordinates = coordinates
    }
  }
  return {
    Map: FakeMap,
    HtmlMarker,
    AuthenticationType: { sas: 'sas' },
    ControlPosition: { TopRight: 'top-right' },
    control: { ZoomControl: function ZoomControl() {} },
    source: { DataSource },
    layer: { LineLayer, PolygonLayer },
    data: { MultiLineString, Polygon },
  }
})

const created = () => maps.created[maps.created.length - 1]!
const DOMAIN = 'eu.atlas.microsoft.com'

/** Fires a map event the way the SDK would: outside React, so state updates need `act`. */
const fire = (type: string, event?: unknown) => act(() => created().handlers.get(type)!(event))

beforeEach(() => {
  maps.created.length = 0
  maps.throwOnCreate = false
  resetAzureMapsFallback()
  vi.mocked(getMapsToken).mockReset()
})
afterEach(() => resetAzureMapsFallback())

function Probe({ onFailed }: { onFailed: (failed: boolean) => void }) {
  onFailed(useAzureMapsFailed())
  return null
}

describe('AzureMapsMap', () => {
  it('creates the map with SAS auth against the configured domain, without telemetry or a key', () => {
    render(<AzureMapsMap azureMapsDomain={DOMAIN} onStateChange={() => {}} />)

    const { options } = created()
    expect(options).toMatchObject({ domain: DOMAIN, disableTelemetry: true, authOptions: { authType: 'sas' } })
    expect(JSON.stringify(options)).not.toMatch(/subscription|key/i)
    expect(options.scrollZoomInteraction).toBe(false) // view-only maps leave wheel scrolling to the page
  })

  it('does not create a map without a domain', () => {
    render(<AzureMapsMap azureMapsDomain={null} onStateChange={() => {}} />)

    expect(maps.created).toHaveLength(0)
  })

  it('lets a picker map capture the wheel', () => {
    render(<AzureMapsMap azureMapsDomain={DOMAIN} onPick={() => {}} onStateChange={() => {}} />)

    expect(created().options.scrollZoomInteraction).toBe(true)
  })

  it('hands the SDK the API-issued token through its getToken callback', async () => {
    vi.mocked(getMapsToken).mockResolvedValue('sas-token')
    render(<AzureMapsMap azureMapsDomain={DOMAIN} onStateChange={() => {}} />)
    const resolve = vi.fn<(token?: string) => void>()
    const reject = vi.fn<(reason?: unknown) => void>()

    ;(created().options.authOptions as { getToken: (r: typeof resolve, j: typeof reject) => void }).getToken(
      resolve,
      reject,
    )

    await vi.waitFor(() => expect(resolve).toHaveBeenCalledWith('sas-token'))
    expect(reject).not.toHaveBeenCalled()
  })

  it('marks Azure Maps unavailable for the session and reports an error when the token cannot be fetched', async () => {
    vi.mocked(getMapsToken).mockRejectedValue(new Error('503'))
    const states: string[] = []
    const failed: boolean[] = []
    render(
      <>
        <AzureMapsMap azureMapsDomain={DOMAIN} onStateChange={(s) => states.push(s)} />
        <Probe onFailed={(f) => failed.push(f)} />
      </>,
    )
    const resolve = vi.fn<(token?: string) => void>()
    const reject = vi.fn<(reason?: unknown) => void>()

    ;(created().options.authOptions as { getToken: (r: typeof resolve, j: typeof reject) => void }).getToken(
      resolve,
      reject,
    )

    await vi.waitFor(() => expect(reject).toHaveBeenCalled())
    expect(states).toContain('error')
    expect(failed.at(-1)).toBe(true)
  })

  it('marks Azure Maps unavailable when the SDK cannot start or reports an error before ready, but not after', () => {
    const failed: boolean[] = []
    maps.throwOnCreate = true
    const first = render(
      <>
        <AzureMapsMap azureMapsDomain={DOMAIN} onStateChange={() => {}} />
        <Probe onFailed={(f) => failed.push(f)} />
      </>,
    )
    expect(failed.at(-1)).toBe(true)
    first.unmount()

    resetAzureMapsFallback()
    maps.throwOnCreate = false
    const states: string[] = []
    render(
      <>
        <AzureMapsMap azureMapsDomain={DOMAIN} onStateChange={(s) => states.push(s)} />
        <Probe onFailed={(f) => failed.push(f)} />
      </>,
    )
    fire('ready')
    fire('error') // a single tile failing after the map is ready
    expect(states).toEqual(['ready'])
    expect(failed.at(-1)).toBe(false)
  })

  it('reports ready and wraps picked longitudes back into range', () => {
    const states: string[] = []
    const onPick = vi.fn<(position: { latitude: number; longitude: number }) => void>()
    render(<AzureMapsMap azureMapsDomain={DOMAIN} onPick={onPick} onStateChange={(s) => states.push(s)} />)

    fire('ready')
    fire('click', { position: [190, 52] })
    fire('click', { position: [-365, 10] })
    fire('click', {})

    expect(states).toEqual(['ready'])
    expect(onPick).toHaveBeenNthCalledWith(1, { latitude: 52, longitude: -170 })
    expect(onPick).toHaveBeenNthCalledWith(2, { latitude: 10, longitude: -5 })
    expect(onPick).toHaveBeenCalledTimes(2)
  })

  it('draws the track with start and finish markers and fits the view once ready', () => {
    render(<AzureMapsMap azureMapsDomain={DOMAIN} track={gliwice10kGeoJson} fitTo="track" onStateChange={() => {}} />)
    fire('ready')

    expect(created().layers.add).toHaveBeenCalledTimes(1)
    expect(created().markers.add).toHaveBeenCalledTimes(1)
    const markers = created().markers.add.mock.calls[0]![0] as { options: { htmlContent: HTMLElement } }[]
    expect(markers.map((m) => m.options.htmlContent.dataset.testid)).toEqual(['map-start', 'map-finish'])
    expect(created().setCamera).toHaveBeenCalledWith(expect.objectContaining({ bounds: [...gliwice10kGeoJson.bbox] }))
  })

  it('dashes planned courses', () => {
    render(<AzureMapsMap azureMapsDomain={DOMAIN} track={gliwice10kGeoJson} dashed onStateChange={() => {}} />)
    fire('ready')

    const layers = created().layers.add.mock.calls[0]![0] as { options: { strokeDashArray?: number[] } }[]
    expect(layers[1]!.options.strokeDashArray).toEqual([2, 2])
  })

  it('places a single marker and centres on it', () => {
    render(
      <AzureMapsMap
        azureMapsDomain={DOMAIN}
        marker={{ latitude: 50.29, longitude: 18.67 }}
        fitTo="marker"
        onStateChange={() => {}}
      />,
    )
    fire('ready')

    const pin = created().markers.add.mock.calls[0]![0] as { getElement: () => HTMLElement }
    expect(pin.getElement().dataset.testid).toBe('map-marker')
    expect(created().setCamera).toHaveBeenCalledWith(expect.objectContaining({ center: [18.67, 50.29], zoom: 14 }))
  })

  it('draws the area circle, fits to it and replaces it when it changes', () => {
    const area = { center: { latitude: 50.29, longitude: 18.67 }, radiusMeters: 25_000 }
    const view = render(<AzureMapsMap azureMapsDomain={DOMAIN} area={area} onStateChange={() => {}} />)
    fire('ready')

    expect(created().layers.add).toHaveBeenCalledTimes(1)
    const [fill] = created().layers.add.mock.calls[0]![0] as { options: { fillOpacity: number } }[]
    expect(fill!.options.fillOpacity).toBe(0.1)
    expect(created().setCamera).toHaveBeenCalledWith(expect.objectContaining({ bounds: expect.any(Array) }))

    view.rerender(
      <AzureMapsMap azureMapsDomain={DOMAIN} area={{ ...area, radiusMeters: 50_000 }} onStateChange={() => {}} />,
    )
    expect(created().layers.remove).toHaveBeenCalledTimes(1)
    expect(created().layers.add).toHaveBeenCalledTimes(2)

    view.unmount()
    expect(created().layers.remove).toHaveBeenCalledTimes(2)
  })

  it('shows a race marker with the supplied label', () => {
    render(
      <AzureMapsMap
        azureMapsDomain={DOMAIN}
        marker={{ latitude: 50.29, longitude: 18.67 }}
        markerKind="race"
        markerLabel="Race location"
        onStateChange={() => {}}
      />,
    )
    fire('ready')

    const pin = created().markers.add.mock.calls[0]![0] as { options: { htmlContent: HTMLElement } }
    expect(pin.options.htmlContent).toHaveAttribute('aria-label', 'Race location')
    expect(pin.options.htmlContent).toHaveAttribute('role', 'img')
  })

  it('disposes the map on unmount', () => {
    const view = render(<AzureMapsMap azureMapsDomain={DOMAIN} onStateChange={() => {}} />)
    const instance = created()

    view.unmount()

    expect(instance.disposed).toBe(true)
  })
})
