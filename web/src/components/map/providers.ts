import { lazy, type ComponentType } from 'react'
import type { MapProviderId, MapProviderProps } from './types'

type ProviderComponent = ComponentType<MapProviderProps>

/**
 * Provider components are lazy chunks (ADR 0022): the map engine is never part of the initial load. `lazy()` is
 * created once per provider at module level so a re-render never re-suspends.
 *
 * Azure Maps (`azure/AzureMapsMap.tsx`, `azure-maps-control`) is deferred to M7 (plan §9 Q1); until then a
 * configuration that asks for it gets MapLibre.
 */
const MapLibre = lazy<ProviderComponent>(() => import('./maplibre/MapLibreMap'))

/** Azure Maps: TODO(M7) `azure/AzureMapsMap.tsx`; until then a configuration that asks for it gets MapLibre. */
export const MAP_PROVIDERS: Record<MapProviderId, ProviderComponent> = {
  MapLibre,
  AzureMaps: MapLibre,
}
