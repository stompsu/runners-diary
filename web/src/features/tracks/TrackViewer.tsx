import { lazy, Suspense, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { MapPrivacyNote } from '@/components/map/MapPrivacyNote'
import { MapView } from '@/components/map/MapView'
import type { MapsConfig } from '@/components/map/types'
import { TrackDataTable } from '@/components/map/TrackDataTable'
import { Skeleton } from '@/components/ui/skeleton'
import { useIsPhone } from '@/hooks/useIsPhone'
import { profilePosition, type TrackGeoJson } from './trackGeoJson'
import type { TrackSummary, TrackWarning } from './trackSummary'
import '@/i18n/lazyBundles'

// Recharts is a lazy chunk: the map shows immediately, the profile follows.
const ElevationProfile = lazy(() => import('@/components/map/ElevationProfile'))

export interface TrackViewerProps {
  geoJson: TrackGeoJson
  summary: TrackSummary
  warnings?: readonly TrackWarning[]
  /** Planned courses are drawn dashed. */
  planned?: boolean
  /** `maps` section of client-config (ADR 0022). */
  maps?: MapsConfig
}

/**
 * One track: the map, the elevation profile linked to it (pointer and keyboard on the profile move a marker on the
 * map), and the text alternative (summary, notes, splits table). Pure presentation: the data comes from props, so a
 * route or dialog (step 5) decides how it is fetched.
 */
export function TrackViewer({ geoJson, summary, warnings = [], planned = false, maps }: TrackViewerProps) {
  const { t } = useTranslation('tracks')
  const isPhone = useIsPhone()
  const [hoverIndex, setHoverIndex] = useState<number | null>(null)
  const profile = geoJson.profile
  const sample = profile && hoverIndex !== null ? profile[hoverIndex] : undefined

  return (
    <div className="space-y-4">
      <MapView
        ariaLabel={t('viewer.mapLabel')}
        track={geoJson}
        fitTo="track"
        dashed={planned}
        highlight={sample ? profilePosition(sample) : null}
        provider={maps?.provider}
        styleUrl={maps?.mapLibreStyleUrl}
        azureMapsDomain={maps?.azureMapsDomain}
        className="h-[max(240px,40vh)] w-full"
      />
      <MapPrivacyNote maps={maps} />
      {profile && profile.length > 1 && (
        <section aria-labelledby="track-profile-title">
          <h3 id="track-profile-title" className="sr-only">
            {t('profile.title')}
          </h3>
          <Suspense fallback={<Skeleton className="h-[120px] sm:h-40" aria-label={t('viewer.profileLoading')} />}>
            <ElevationProfile profile={profile} activeIndex={hoverIndex} onHover={setHoverIndex} />
          </Suspense>
        </section>
      )}
      <TrackDataTable summary={summary} warnings={warnings} splits={geoJson.splits} splitsOpen={!isPhone} />
    </div>
  )
}
