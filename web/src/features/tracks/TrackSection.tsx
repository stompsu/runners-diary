import { Download, Route, Trash2, Upload, Wand2, Eye, Repeat } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { returnFocusState } from '@/features/runs/returnFocus'
import type { RunDto, RunTypeRules } from '@/features/runs/runFormTypes'
import { DeleteTrackDialog } from './DeleteTrackDialog'
import { downloadTrackFile } from './downloadTrack'
import { classifyTrackProblem, isFileMissing, trackProblemMessage } from './trackProblem'
import { useInvalidateTracks, useSeedTrack } from './trackQueries'
import { TrackPrefillDialog } from './TrackPrefillDialog'
import { TrackUploadDialog } from './TrackUploadDialog'
import { TRACK_KINDS, trackOfKind, type RunTrackDto, type TrackDto, type TrackKind } from './trackTypes'
import '@/i18n/lazyBundles'

export interface TrackSectionProps {
  run: RunDto
  rules: RunTypeRules
  /** `/diary/:year/:month` of the open panel. */
  base: string
  isGuest: boolean
  /** Open the upload dialog on mount (after "Save & add GPX"). */
  openUpload?: boolean
  onUploadOpened?: () => void
}

/**
 * The GPX files of a run (ADR 0023): per kind view / download / use values / replace / delete, and the upload dialog.
 * Tracks are a sub-resource: nothing here changes the run row or its `version`.
 */
export function TrackSection({ run, rules, base, isGuest, openUpload, onUploadOpened }: TrackSectionProps) {
  const { t } = useTranslation('tracks')
  const { t: tErrors } = useTranslation('errors')
  const invalidate = useInvalidateTracks(run.id)
  const seed = useSeedTrack()
  const [upload, setUpload] = useState<{ kind: TrackKind } | null>(() =>
    openUpload ? { kind: defaultKind(run) } : null,
  )
  const [deleting, setDeleting] = useState<RunTrackDto | null>(null)
  const [prefill, setPrefill] = useState<{ trackId: string; fromUpload: boolean } | null>(null)
  const [downloading, setDownloading] = useState<string | null>(null)
  const [missing, setMissing] = useState<ReadonlySet<string>>(new Set())
  const [origin, setOrigin] = useState<HTMLElement | null>(null)
  const heading = useRef<HTMLHeadingElement>(null)

  // The request to open the dialog (a URL parameter) is consumed once, so a reload does not reopen it.
  useEffect(() => {
    if (openUpload) onUploadOpened?.()
  }, [openUpload, onUploadOpened])

  if (rules.isRestDay) return null

  const action = 'h-11 sm:h-9'
  const remember = (event: { currentTarget: HTMLElement }) => setOrigin(event.currentTarget)
  const returnToOrigin = () => origin
  const tracks = run.tracks

  async function download(track: RunTrackDto) {
    setDownloading(track.id)
    try {
      await downloadTrackFile(run.id, track)
    } catch (caught) {
      if (isFileMissing(caught)) {
        setMissing((current) => new Set(current).add(track.id))
        toast.error(t('section.fileMissing'))
        return
      }
      toast.error(trackProblemMessage(classifyTrackProblem(caught), t, tErrors))
    } finally {
      setDownloading(null)
    }
  }

  async function uploaded(track: TrackDto) {
    setUpload(null)
    toast.success(t('toast.uploaded'))
    seed(track, track)
    await invalidate()
    setPrefill({ trackId: track.id, fromUpload: true })
  }

  return (
    <section aria-labelledby="details-tracks" className="flex flex-col gap-3">
      <h3 id="details-tracks" ref={heading} tabIndex={-1} className="text-sm font-semibold outline-none">
        {t('section.title')}
      </h3>
      {tracks.length === 0 ? <p className="text-sm text-muted-foreground">{t('section.empty')}</p> : null}
      {tracks.length > 0 ? (
        <ul className="flex flex-col gap-3">
          {TRACK_KINDS.map((kind) => {
            const track = trackOfKind(tracks, kind)
            if (!track) return null
            const kindLabel = t(`kindLower.${kind}`)
            const fileMissing = missing.has(track.id)
            const usable = track.status === 'Processed' && !fileMissing
            return (
              <li key={track.id} data-track-kind={kind} className="rounded-lg border p-3">
                <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
                  <Route className="size-4 shrink-0" aria-hidden="true" />
                  <h4 className="text-sm font-medium">{t(`kind.${kind}`)}</h4>
                  <span className="text-sm text-muted-foreground break-all">{track.originalFileName}</span>
                </div>
                {usable ? null : (
                  <p className="mt-1 text-sm text-muted-foreground" data-testid="track-status">
                    {t(
                      fileMissing
                        ? 'section.fileMissing'
                        : track.status === 'Failed'
                          ? 'section.failed'
                          : 'section.notProcessed',
                    )}
                  </p>
                )}
                <div className="mt-2 flex flex-wrap gap-2">
                  {usable ? (
                    <>
                      <Button asChild variant="outline" className={action}>
                        <Link
                          to={`${base}/runs/${run.id}/tracks/${track.id}`}
                          data-track-view={track.id}
                          aria-label={t('actions.viewNamed', { kind: kindLabel })}
                          state={returnFocusState(`[data-panel="details"] a[data-track-view="${track.id}"]`)}
                        >
                          <Eye aria-hidden="true" />
                          {t('actions.view')}
                        </Link>
                      </Button>
                      <Button
                        type="button"
                        variant="outline"
                        className={action}
                        disabled={downloading === track.id}
                        aria-label={t('actions.downloadNamed', { kind: kindLabel })}
                        onClick={() => void download(track)}
                      >
                        <Download aria-hidden="true" />
                        {t('actions.download')}
                      </Button>
                      <Button
                        type="button"
                        variant="outline"
                        className={action}
                        data-use-values={track.id}
                        aria-label={t('actions.useValuesNamed', { kind: kindLabel })}
                        onClick={(event) => {
                          remember(event)
                          setPrefill({ trackId: track.id, fromUpload: false })
                        }}
                      >
                        <Wand2 aria-hidden="true" />
                        {t('actions.useValues')}
                      </Button>
                    </>
                  ) : null}
                  <Button
                    type="button"
                    variant="outline"
                    className={action}
                    aria-label={t('actions.replaceNamed', { kind: kindLabel })}
                    onClick={(event) => {
                      remember(event)
                      setUpload({ kind })
                    }}
                  >
                    <Repeat aria-hidden="true" />
                    {t('actions.replace')}
                  </Button>
                  <Button
                    type="button"
                    variant="outline"
                    className={`${action} text-destructive`}
                    aria-label={t('actions.deleteNamed', { kind: kindLabel })}
                    onClick={(event) => {
                      remember(event)
                      setDeleting(track)
                    }}
                  >
                    <Trash2 aria-hidden="true" />
                    {t('actions.delete')}
                  </Button>
                </div>
              </li>
            )
          })}
        </ul>
      ) : null}
      <div>
        <Button
          type="button"
          variant="outline"
          className={action}
          data-action="add-gpx"
          onClick={(event) => {
            remember(event)
            setUpload({ kind: defaultKind(run) })
          }}
        >
          <Upload aria-hidden="true" />
          {t('actions.add')}
        </Button>
      </div>

      {upload ? (
        <TrackUploadDialog
          run={run}
          defaultKind={upload.kind}
          isGuest={isGuest}
          onClose={() => setUpload(null)}
          onUploaded={(track) => void uploaded(track)}
          returnFocusTo={returnToOrigin}
        />
      ) : null}
      {prefill ? (
        <TrackPrefillDialog
          run={run}
          rules={rules}
          trackId={prefill.trackId}
          base={base}
          fromUpload={prefill.fromUpload}
          onClose={() => setPrefill(null)}
          returnFocusTo={returnToOrigin}
        />
      ) : null}
      <DeleteTrackDialog
        runId={run.id}
        track={deleting}
        origin={origin}
        onClose={() => setDeleting(null)}
        onDeleted={() => heading.current?.focus()}
      />
    </section>
  )
}

/** Recorded track for a run that has happened; the planned course for one that has not (unless that one exists). */
function defaultKind(run: RunDto): TrackKind {
  const preferred: TrackKind = run.status === 'Completed' ? 'RecordedTrack' : 'PlannedCourse'
  const other: TrackKind = preferred === 'RecordedTrack' ? 'PlannedCourse' : 'RecordedTrack'
  return trackOfKind(run.tracks, preferred) && !trackOfKind(run.tracks, other) ? other : preferred
}
