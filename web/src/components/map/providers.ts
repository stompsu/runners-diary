import { lazy, type ComponentType } from 'react'
import type { MapProviderId, MapProviderProps } from './types'

type ProviderComponent = ComponentType<MapProviderProps>

/**
 * Provider components are lazy chunks (ADR 0022): the map engine is never part of the initial load. `lazy()` is
 * created once per provider at module level so a re-render never re-suspends.
 *
 * Azure Maps (`azure/AzureMapsMap.tsx`, `azure-maps-control`, M7) is a separate lazy chunk that only loads when client-config
 * selects it (ADR 0033); `MapView` falls back to MapLibre for the session if it cannot get a token.
 */
const MapLibre = lazy<ProviderComponent>(() => import('./maplibre/MapLibreMap'))

const AzureMaps = lazy<ProviderComponent>(() => import('./azure/AzureMapsMap'))

export const MAP_PROVIDERS: Record<MapProviderId, ProviderComponent> = {
  MapLibre,
  AzureMaps,
}
