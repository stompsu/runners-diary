import { useEffect } from 'react'
import { markAzureMapsUnavailable } from '@/components/map/azure/azureSession'
import type { MapProviderProps } from '@/components/map/types'
import { fakeMap } from './fakeMapState'

/**
 * Stand-in for `azure/AzureMapsMap` in jsdom. Tests use it with
 * `vi.mock('@/components/map/azure/AzureMapsMap', () => import('@/test/fakeAzureMapProvider'))`. It reports the state set in
 * `fakeMap.state`, shows the domain it received, and has a button that simulates a token failure.
 */
export default function FakeAzureMapsMap({ azureMapsDomain, onStateChange }: MapProviderProps) {
  useEffect(() => {
    onStateChange(fakeMap.state)
  }, [onStateChange])
  return (
    <div data-testid="fake-azure-map" data-domain={azureMapsDomain ?? ''}>
      <button type="button" onClick={() => markAzureMapsUnavailable()}>
        Fail token
      </button>
    </div>
  )
}
