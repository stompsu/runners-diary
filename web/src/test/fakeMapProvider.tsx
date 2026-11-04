import { useEffect } from 'react'
import type { MapProviderProps } from '@/components/map/types'
import { fakeMap } from './fakeMapState'

/**
 * Stand-in for the MapLibre provider module in jsdom (no WebGL there). Tests use it with
 * `vi.mock('@/components/map/maplibre/MapLibreMap', () => import('@/test/fakeMapProvider'))`.
 * It renders what the provider received as `data-*` attributes, reports the state set in `fakeMap.state`, and has a
 * button that simulates a click on the map.
 */
export default function FakeMapLibreMap({
  track,
  marker,
  highlight,
  onPick,
  fitTo,
  center,
  dashed,
  styleUrl,
  onStateChange,
}: MapProviderProps) {
  useEffect(() => {
    onStateChange(fakeMap.state)
  }, [onStateChange])
  return (
    <div
      data-testid="fake-map"
      data-style-url={styleUrl ?? ''}
      data-highlight={highlight ? highlight.join(',') : ''}
      data-marker={marker ? `${marker.latitude},${marker.longitude}` : ''}
      data-fit-to={fitTo ?? ''}
      data-center={center ? `${center.latitude},${center.longitude}` : ''}
      data-dashed={String(!!dashed)}
      data-has-track={String(!!track)}
    >
      {onPick && (
        <button type="button" onClick={() => onPick(fakeMap.pick)}>
          Pick here
        </button>
      )}
    </div>
  )
}
