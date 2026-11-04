import { render } from '@testing-library/react'
import { LngLat } from 'maplibre-gl'
import { describe, expect, it, vi } from 'vitest'
import MapLibreMap from './MapLibreMap'

const handlers = vi.hoisted(() => new Map<string, (event: unknown) => void>())

vi.mock('maplibre-gl', async () => {
  const actual = await vi.importActual<typeof import('maplibre-gl')>('maplibre-gl')
  class FakeMap {
    on(type: string, handler: (event: unknown) => void) {
      handlers.set(type, handler)
    }
    addControl() {}
    remove() {}
  }
  return { ...actual, Map: FakeMap, setWorkerUrl: () => {} }
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
