import { Download } from 'lucide-react'
import { lazy, Suspense, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate, useNavigationType, useParams } from 'react-router'
import { toast } from 'sonner'
import { useClientConfig } from '@/auth/useClientConfig'
import { ResponsiveDialog } from '@/components/ResponsiveDialog'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { diaryPath } from '@/features/diary/diaryMonth'
import { QueryError } from '@/features/errors/QueryError'
import { RunGone } from '@/features/runs/RunGone'
import { useRun } from '@/features/runs/runQueries'
import { useClosePanel, usePanelMonth } from '@/features/runs/usePanel'
import { parseProblem } from '@/lib/api/problem'
import { downloadTrackFile } from './downloadTrack'
import { toTrackGeoJson } from './trackGeoJson'
import { classifyTrackProblem, isFileMissing, trackProblemMessage } from './trackProblem'
import { useTrack, useTrackGeoJson } from './trackQueries'
import '@/i18n/lazyBundles'

// The map and the profile are heavy: the viewer is its own chunk (and MapView/Recharts load lazily inside it).
const TrackViewer = lazy(() => import('./TrackViewer').then((m) => ({ default: m.TrackViewer })))

/** `/diary/:year/:month/runs/:runId/tracks/:trackId`: one GPX track with map, elevation profile and the text alternative. */
export function TrackViewPanel() {
  const { runId = '', trackId = '' } = useParams()
  const { t } = useTranslation('tracks')
  const { t: tErrors } = useTranslation('errors')
  const month = usePanelMonth()
  const closeToPrevious = useClosePanel()
  const navigationType = useNavigationType()
  const navigate = useNavigate()
  const run = useRun(runId)
  const track = useTrack(runId, trackId)
  const processed = track.data?.status === 'Processed'
  const geo = useTrackGeoJson(runId, trackId, processed)
  const maps = useClientConfig().data?.maps
  const [downloading, setDownloading] = useState(false)
  const [downloadMissing, setDownloadMissing] = useState(false)

  // Opened from the run's details: go back to them. Opened by a deep link: go to them (not to the bare month).
  const close = () => {
    if (navigationType === 'PUSH') closeToPrevious()
    else void navigate(`${diaryPath(month)}/runs/${runId}`, { replace: true })
  }

  if (run.isError && parseProblem(run.error).kind === 'notFound') return <RunGone onClose={closeToPrevious} />

  // The summary loads but the file does not: the row outlived its blobs.
  const fileMissing = downloadMissing || (geo.isError && parseProblem(geo.error).kind === 'notFound')
  const gone = track.isError && parseProblem(track.error).kind === 'notFound'
  const data = track.data
  const title = data ? `${t(`kind.${data.kind}`)} · ${data.originalFileName}` : t('viewer.title')
  const geoJson = geo.data ? toTrackGeoJson(geo.data) : null

  async function download() {
    if (!data) return
    setDownloading(true)
    try {
      await downloadTrackFile(runId, data)
    } catch (caught) {
      if (isFileMissing(caught)) {
        setDownloadMissing(true)
        toast.error(t('viewer.fileMissing'))
        return
      }
      toast.error(trackProblemMessage(classifyTrackProblem(caught), t, tErrors))
    } finally {
      setDownloading(false)
    }
  }

  let body
  if (gone) {
    body = <p>{t('viewer.gone')}</p>
  } else if (track.isError) {
    body = <QueryError error={track.error} onRetry={() => void track.refetch()} />
  } else if (!data) {
    body = <Skeleton className="h-64 w-full" />
  } else if (!processed || !data.summary) {
    body = <p>{t(data.status === 'Failed' ? 'section.failed' : 'section.notProcessed')}</p>
  } else if (fileMissing) {
    body = <p role="alert">{t('viewer.fileMissing')}</p>
  } else if (geo.isError) {
    body = <QueryError error={geo.error} onRetry={() => void geo.refetch()} />
  } else if (!geo.data) {
    body = <Skeleton className="h-64 w-full" />
  } else if (!geoJson) {
    body = <p role="alert">{t('viewer.unreadable')}</p>
  } else {
    body = (
      <Suspense fallback={<Skeleton className="h-64 w-full" />}>
        <TrackViewer
          geoJson={geoJson}
          summary={data.summary}
          warnings={data.warnings}
          planned={data.kind === 'PlannedCourse'}
          maps={maps}
        />
      </Suspense>
    )
  }

  return (
    <ResponsiveDialog
      title={title}
      onClose={close}
      size="lg"
      restoreFocusDate={run.data?.date}
      panel="track-view"
      footer={
        processed && !fileMissing ? (
          <Button
            type="button"
            variant="outline"
            className="h-11 sm:h-9"
            disabled={downloading}
            onClick={() => void download()}
          >
            <Download aria-hidden="true" />
            {t('actions.download')}
          </Button>
        ) : undefined
      }
    >
      {body}
    </ResponsiveDialog>
  )
}
