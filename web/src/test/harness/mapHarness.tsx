import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import '@/i18n'
import '@/index.css'
import { TrackViewer } from '@/features/tracks/TrackViewer'
import { bareRouteGeoJson, bareRouteSummary, gliwice10kGeoJson, gliwice10kSummary } from '@/test/fixtures/tracks'

/**
 * Playwright-only page for the track viewer (served by the Vite dev server, see playwright.config.ts). It is not an entry
 * of `vite build`, so it never ships. The real route arrives with the tracks in run details (M4 step 5).
 * `?fixture=bare` shows an untimed, elevation-less route; `?planned=1` draws the line dashed.
 */
const params = new URLSearchParams(window.location.search)
const bare = params.get('fixture') === 'bare'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <main className="mx-auto max-w-3xl p-4">
      <h1 className="mb-4 text-xl font-semibold">Track viewer harness</h1>
      <TrackViewer
        geoJson={bare ? bareRouteGeoJson : gliwice10kGeoJson}
        summary={bare ? bareRouteSummary : gliwice10kSummary}
        warnings={bare ? ['timestampsIgnored', 'noElevation'] : []}
        planned={params.get('planned') === '1'}
      />
    </main>
  </StrictMode>,
)
