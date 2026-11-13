import type { LonLat, TrackGeoJson } from '@/features/tracks/trackGeoJson'

export type { LonLat, TrackGeoJson }

export interface LatLon {
  latitude: number
  longitude: number
}

/** Providers are chosen at runtime from `GET /api/client-config` `maps.provider` (ADR 0022). */
export type MapProviderId = 'MapLibre' | 'AzureMaps'

/** The `maps` section of client-config, as far as the map UI reads it (all optional so callers can pass a partial). */
export interface MapsConfig {
  provider?: MapProviderId
  mapLibreStyleUrl?: string | null
  /** Azure Maps host (`eu.atlas.microsoft.com`); null unless the provider is `AzureMaps`. */
  azureMapsDomain?: string | null
}

/** `loading` until the style is ready; `unavailable` = no WebGL2; `error` = the engine or the style failed. */
export type MapState = 'loading' | 'ready' | 'unavailable' | 'error'

/** Provider-neutral props (ADR 0022 §1). */
/** A circular search area (race interests preview): drawn as an outline with a light fill. */
export interface MapArea {
  center: LatLon
  radiusMeters: number
}

export interface MapViewProps {
  track?: TrackGeoJson | null
  /** A single place (location picker, run details). */
  marker?: LatLon | null
  /** `race` draws a flag pin (needs `markerLabel`); `default` is the plain pin. */
  markerKind?: 'default' | 'race'
  /** Accessible name of a `race` marker, supplied by the caller (translated). */
  markerLabel?: string
  /** A radius circle; with no track the view fits to it (unless `fitTo="marker"`). */
  area?: MapArea | null
  /** Hover/keyboard sync from the elevation profile; draws one moving marker. */
  highlight?: LonLat | null
  /** Clicking or tapping the map reports the position (location picker). */
  onPick?: (position: LatLon) => void
  fitTo?: 'track' | 'marker'
  /** Where the view starts when there is neither a track nor a marker (the location picker). Read once. */
  center?: LatLon
  /** Planned courses are drawn dashed. */
  dashed?: boolean
  /** Accessible name of the map region; required. */
  ariaLabel: string
  /** `maps.provider` of client-config; unknown or unconfigured falls back to MapLibre. */
  provider?: MapProviderId
  /** `maps.mapLibreStyleUrl` of client-config; null/undefined = the built-in default. */
  styleUrl?: string | null
  /** `maps.azureMapsDomain` of client-config; the Azure Maps provider is used only when this is set. */
  azureMapsDomain?: string | null
  className?: string
}

/** What `MapView` hands to a provider component (which renders only the map surface). */
export interface MapProviderProps extends Pick<
  MapViewProps,
  | 'track'
  | 'marker'
  | 'markerKind'
  | 'markerLabel'
  | 'area'
  | 'highlight'
  | 'onPick'
  | 'fitTo'
  | 'center'
  | 'dashed'
  | 'styleUrl'
  | 'azureMapsDomain'
> {
  onStateChange: (state: MapState) => void
}
