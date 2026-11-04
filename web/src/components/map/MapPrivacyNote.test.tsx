import { render, screen } from '@testing-library/react'
import { act } from 'react'
import { beforeEach, describe, expect, it } from 'vitest'
import { markAzureMapsUnavailable, resetAzureMapsFallback } from './azure/azureSession'
import { MapPrivacyNote } from './MapPrivacyNote'

beforeEach(() => resetAzureMapsFallback())

describe('MapPrivacyNote', () => {
  it('names the OpenFreeMap host by default', () => {
    render(<MapPrivacyNote />)

    expect(screen.getByText(/tiles\.openfreemap\.org/)).toHaveTextContent('can see the area you are viewing')
  })

  it('names the configured MapLibre style host', () => {
    render(
      <MapPrivacyNote maps={{ provider: 'MapLibre', mapLibreStyleUrl: 'https://tiles.example.test/style.json' }} />,
    )

    expect(screen.getByText(/tiles\.example\.test/)).toBeInTheDocument()
  })

  it('names Azure Maps only while it really draws, and OpenFreeMap again after a session fallback', () => {
    render(
      <MapPrivacyNote
        maps={{ provider: 'AzureMaps', azureMapsDomain: 'eu.atlas.microsoft.com', mapLibreStyleUrl: null }}
      />,
    )
    expect(screen.getByText(/Microsoft Azure Maps/)).toBeInTheDocument()

    act(() => markAzureMapsUnavailable())

    expect(screen.queryByText(/Microsoft Azure Maps/)).not.toBeInTheDocument()
    expect(screen.getByText(/tiles\.openfreemap\.org/)).toBeInTheDocument()
  })
})
