import { Suspense, useCallback, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { vertexCount } from '@/features/tracks/trackGeoJson'
import { cn } from '@/lib/utils'
import { MapUnavailable } from './MapUnavailable'
import { MAP_PROVIDERS } from './providers'
import type { MapState, MapViewProps } from './types'
import { supportsWebGl2 } from './webgl'
import '@/i18n/lazyBundles'

/**
 * The provider-neutral map (ADR 0022). Lazily loads the configured provider, shows an accessible "map unavailable"
 * panel without WebGL2 (or when the engine fails), and exposes its state for tests as `data-*` attributes.
 * The wrapper is a labelled region; the track's text alternative (summary, splits table) lives next to it in
 * `TrackViewer`, so the map itself is a visual enhancement.
 *
 * Give it a size (height) from outside: the surface fills the region.
 */
export function MapView({ ariaLabel, provider, styleUrl, className, ...rest }: MapViewProps) {
  const { t } = useTranslation('maps')
  const webGl = supportsWebGl2()
  const [reported, setReported] = useState<MapState>('loading')
  const handleState = useCallback((state: MapState) => setReported(state), [])
  const state: MapState = webGl ? reported : 'unavailable'
  const Provider = MAP_PROVIDERS[provider ?? 'MapLibre'] ?? MAP_PROVIDERS.MapLibre

  return (
    <section
      aria-label={ariaLabel}
      data-map-state={state}
      data-track-vertices={rest.track ? vertexCount(rest.track) : 0}
      className={cn('relative overflow-hidden rounded-md border bg-muted', className)}
    >
      {webGl && (
        <Suspense fallback={null}>
          <Provider {...rest} styleUrl={styleUrl} onStateChange={handleState} />
        </Suspense>
      )}
      {state === 'loading' && webGl && (
        <p className="pointer-events-none absolute inset-0 grid place-items-center text-sm text-muted-foreground">
          {t('loading')}
        </p>
      )}
      {(state === 'unavailable' || state === 'error') && (
        <div className="absolute inset-0">
          <MapUnavailable reason={state === 'unavailable' ? 'noWebGl' : 'error'} />
        </div>
      )}
    </section>
  )
}
