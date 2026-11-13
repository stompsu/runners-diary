import { act, render } from '@testing-library/react'
import { LngLat } from 'maplibre-gl'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { gliwice10kGeoJson } from '@/test/fixtures/tracks'
import MapLibreMap from './MapLibreMap'

const handlers = vi.hoisted(() => new Map<string, (event: unknown) => void>())
const fake = vi.hoisted(() => ({
  sources: new Map<string, { data: unknown; setData: (d: unknown) => void }>(),
  layers: [] as string[],
  fitBounds: [] as unknown[][],
  markers: [] as { element?: HTMLElement; removed: boolean }[],
}))

vi.mock('maplibre-gl', async () => {
  const actual = await vi.importActual<typeof import('maplibre-gl')>('maplibre-gl')
  class FakeMap {
    on(type: string, handler: (event: unknown) => void) {
      handlers.set(type, handler)
    }
    addControl() {}
    remove() {}
    getSource(id: string) {
      return fake.sources.get(id)
    }
    addSource(id: string, spec: { data: unknown }) {
      const source = {
        data: spec.data,
        setData(d: unknown) {
          source.data = d
        },
      }
      fake.sources.set(id, source)
    }
    addLayer(layer: { id: string }) {
      fake.layers.push(layer.id)
    }
    setPaintProperty() {}
    getBounds() {
      return { contains: () => true }
    }
    jumpTo() {}
    fitBounds(...args: unknown[]) {
      fake.fitBounds.push(args)
    }
  }
  class FakeMarker {
    entry: { element?: HTMLElement; removed: boolean }
    element: HTMLElement
    constructor(options: { element?: HTMLElement }) {
      this.element = options.element ?? document.createElement('div')
      this.entry = { element: options.element, removed: false }
      fake.markers.push(this.entry)
    }
    setLngLat() {
      return this
    }
    addTo() {
      return this
    }
    getElement() {
      return this.element
    }
    remove() {
      this.entry.removed = true
    }
  }
  return { ...actual, Map: FakeMap, Marker: FakeMarker, setWorkerUrl: () => {} }
})

describe('MapLibreMap picking', () => {
  it('wraps a longitude picked on a repeated world copy back into -180..180', () => {
    const onPick = vi.fn<(position: { latitude: number; longitude: number }) => void>()
    render(<MapLibreMap onPick={onPick} onStateChange={() => {}} />)

    handlers.get('click')!({ lngLat: new LngLat(190, 52) })
    handlers.get('click')!({ lngLat: new LngLat(-365, 10) })

    expect(onPick).toHaveBeenNthCalledWith(1, { latitude: 52, longitude: -170 })
    expect(onPick).toHaveBeenNthCalledWith(2, { latitude: 10, longitude: -5 })
  })
})

const AREA = { center: { latitude: 50.29, longitude: 18.67 }, radiusMeters: 25_000 }
const ringOf = (data: unknown) =>
  (data as { features: { geometry: { coordinates: number[][][] } }[] }).features[0]?.geometry.coordinates[0]

describe('MapLibreMap area and race marker', () => {
  const ready = () => act(() => handlers.get('load')!(undefined))
  beforeEach(() => {
    fake.sources.clear()
    fake.layers.length = 0
    fake.fitBounds.length = 0
    fake.markers.length = 0
  })

  it('draws a 64-vertex circle with fill and outline, fits to it, updates it and clears it', () => {
    const view = render(<MapLibreMap area={AREA} onStateChange={() => {}} />)
    ready()

    expect(fake.layers.filter((id) => id.startsWith('rd-area'))).toEqual(['rd-area-fill', 'rd-area-line'])
    expect(ringOf(fake.sources.get('rd-area')!.data)).toHaveLength(65)
    expect(fake.fitBounds).toHaveLength(1)

    view.rerender(<MapLibreMap area={{ ...AREA, radiusMeters: 100_000 }} onStateChange={() => {}} />)
    const widened = ringOf(fake.sources.get('rd-area')!.data)!
    expect(fake.layers.filter((id) => id.startsWith('rd-area'))).toHaveLength(2) // updated in place, not re-added
    expect(Math.max(...widened.map((p) => p[1]!))).toBeGreaterThan(51)

    view.rerender(<MapLibreMap area={null} onStateChange={() => {}} />)
    expect(ringOf(fake.sources.get('rd-area')!.data)).toBeUndefined()
  })

  it('does not fit to the area when a track is shown', () => {
    render(<MapLibreMap area={AREA} track={gliwice10kGeoJson} onStateChange={() => {}} />)
    ready()

    expect(fake.fitBounds).toHaveLength(1) // the track fit only
    expect(fake.fitBounds[0]![0]).toMatchObject({ _sw: { lng: gliwice10kGeoJson.bbox[0] } })
  })

  it('shows a race marker with the caller-supplied label and removes it on unmount', () => {
    const view = render(
      <MapLibreMap
        marker={{ latitude: 50.29, longitude: 18.67 }}
        markerKind="race"
        markerLabel="Race location"
        onStateChange={() => {}}
      />,
    )
    ready()

    const el = fake.markers[0]!.element!
    expect(el).toHaveAttribute('role', 'img')
    expect(el).toHaveAttribute('aria-label', 'Race location')
    view.unmount()
    expect(fake.markers[0]!.removed).toBe(true)
  })

  it('keeps the plain pin by default', () => {
    render(<MapLibreMap marker={{ latitude: 50.29, longitude: 18.67 }} onStateChange={() => {}} />)
    ready()

    expect(fake.markers[0]!.element).toBeUndefined()
  })
})
