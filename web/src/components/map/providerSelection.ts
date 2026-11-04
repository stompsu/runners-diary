import { useAzureMapsFailed } from './azure/azureSession'
import type { MapProviderId } from './types'

/**
 * The provider that actually draws: Azure Maps only when client-config asks for it and gives its domain, and not after a token or
 * SDK failure in this session; everything else is MapLibre (the default everywhere, ADR 0033).
 */
export function useEffectiveProvider(provider?: MapProviderId, azureMapsDomain?: string | null): MapProviderId {
  const azureFailed = useAzureMapsFailed()
  return provider === 'AzureMaps' && !!azureMapsDomain && !azureFailed ? 'AzureMaps' : 'MapLibre'
}
