import { lazy, Suspense } from 'react'
import { useTranslation } from 'react-i18next'
import { useClientConfig } from '@/auth/useClientConfig'
import { Skeleton } from '@/components/ui/skeleton'
import { formatCoordinates } from './locationCoordinates'
import type { RunDto } from './runFormTypes'
import '@/i18n/lazyBundles'

// The map engine stays out of the details dialog's chunk until a run with a location is opened.
const MapView = lazy(() => import('@/components/map/MapView').then((m) => ({ default: m.MapView })))

/** Place name, coordinates and a small map with a pin, in the run details. */
export function LocationSummary({ location }: { location: NonNullable<RunDto['location']> }) {
  const { t } = useTranslation('runs')
  const { t: tMaps } = useTranslation('maps')
  const maps = useClientConfig().data?.maps

  return (
    <section aria-labelledby="details-location" className="flex flex-col gap-2">
      <h3 id="details-location" className="text-sm font-semibold">
        {t('location.legend')}
      </h3>
      <p>
        {location.name ? <span className="font-medium">{location.name} · </span> : null}
        <span className="text-muted-foreground tabular-nums">
          {formatCoordinates(location.latitude, location.longitude)}
        </span>
      </p>
      <Suspense fallback={<Skeleton className="h-40 w-full" />}>
        <MapView
          ariaLabel={tMaps('location.mapLabel')}
          marker={{ latitude: location.latitude, longitude: location.longitude }}
          fitTo="marker"
          provider={maps?.provider}
          styleUrl={maps?.mapLibreStyleUrl}
          className="h-40 w-full"
        />
      </Suspense>
    </section>
  )
}
