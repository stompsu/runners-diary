import { useTranslation } from 'react-i18next'
import { DEFAULT_MAPLIBRE_STYLE_URL } from './mapConfig'
import { useEffectiveProvider } from './providerSelection'
import type { MapsConfig } from './types'
import '@/i18n/lazyBundles'

function hostOf(url: string): string {
  try {
    return new URL(url).host
  } catch {
    return new URL(DEFAULT_MAPLIBRE_STYLE_URL).host
  }
}

/**
 * Says once, under a map, that loading its tiles tells the tile provider which area is viewed (ADR 0033). It names the provider
 * that really draws, including after a session fallback from Azure Maps to MapLibre.
 */
export function MapPrivacyNote({ maps }: { maps?: MapsConfig }) {
  const { t } = useTranslation('maps')
  const active = useEffectiveProvider(maps?.provider, maps?.azureMapsDomain)
  return (
    <p className="text-xs text-muted-foreground">
      {active === 'AzureMaps'
        ? t('privacy.azureMaps')
        : t('privacy.tiles', { host: hostOf(maps?.mapLibreStyleUrl || DEFAULT_MAPLIBRE_STYLE_URL) })}
    </p>
  )
}
