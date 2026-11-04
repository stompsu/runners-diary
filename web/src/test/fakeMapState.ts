import type { MapState } from '@/components/map/types'

/** Knobs of `fakeMapProvider.tsx` (kept in a plain module so the component file only exports a component). */
export const fakeMap: { state: MapState; pick: { latitude: number; longitude: number } } = {
  state: 'ready',
  pick: { latitude: 50.3, longitude: 18.7 },
}

export function resetFakeMap() {
  fakeMap.state = 'ready'
  fakeMap.pick = { latitude: 50.3, longitude: 18.7 }
}
