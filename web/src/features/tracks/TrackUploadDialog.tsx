import { useId, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useClientConfig } from '@/auth/useClientConfig'
import { ResponsiveDialog } from '@/components/ResponsiveDialog'
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import type { RunDto } from '@/features/runs/runFormTypes'
import { classifyTrackProblem, trackProblemMessage, type TrackProblem } from './trackProblem'
import { useUploadTrack } from './trackQueries'
import { TRACK_KINDS, maxFileBytes, trackOfKind, type TrackDto, type TrackKind } from './trackTypes'
import '@/i18n/lazyBundles'

export interface TrackUploadDialogProps {
  run: RunDto
  defaultKind: TrackKind
  /** Guests have a smaller size limit than users (ADR 0020). */
  isGuest: boolean
  onClose: () => void
  onUploaded: (track: TrackDto) => void
  returnFocusTo?: () => HTMLElement | null | undefined
}

const bytesToMegabytes = (bytes: number) => Math.round(bytes / (1024 * 1024))

/** Cheap checks before a possibly 20 MB upload; the server stays authoritative (it re-checks everything). */
function preCheck(file: File, limit: number): TrackProblem | null {
  if (!/\.gpx$/i.test(file.name)) return { kind: 'invalidGpx', code: 'wrongExtension' }
  if (file.size === 0) return { kind: 'invalidGpx', code: 'malformedXml' }
  if (file.size > limit) return { kind: 'other', problem: { kind: 'payloadTooLarge', status: 413 } }
  return null
}

/**
 * Upload dialog (ADR 0020, 0023): choose the kind, pick a `.gpx` file, upload. An existing file of the chosen kind
 * is replaced only after a confirmation, and a 409 `track-exists` (another tab got there first) asks the same
 * question. Every failure is shown in words in the dialog, which stays open so the user can choose another file.
 */
export function TrackUploadDialog({
  run,
  defaultKind,
  isGuest,
  onClose,
  onUploaded,
  returnFocusTo,
}: TrackUploadDialogProps) {
  const { t } = useTranslation('tracks')
  const { t: tErrors } = useTranslation('errors')
  const upload = useUploadTrack()
  const [kind, setKind] = useState<TrackKind>(defaultKind)
  const [file, setFile] = useState<File | null>(null)
  const [fileProblem, setFileProblem] = useState<TrackProblem | null>(null)
  const [problem, setProblem] = useState<TrackProblem | null>(null)
  const [confirmReplace, setConfirmReplace] = useState(false)
  const [busy, setBusy] = useState(false)
  const busyRef = useRef(false)
  const inputId = useId()
  const hintId = `${inputId}-hint`
  const errorId = `${inputId}-error`

  // The server's limits (client-config); the built-in defaults only apply while that is loading or unavailable.
  const gpx = useClientConfig().data?.gpx
  const limit = gpx ? (isGuest ? gpx.maxFileBytesGuest : gpx.maxFileBytesUser) : maxFileBytes(isGuest)
  const existing = trackOfKind(run.tracks, kind)
  const megabytes = bytesToMegabytes(limit)
  const message = (value: TrackProblem) => trackProblemMessage(value, t, tErrors, { maxMegabytes: megabytes })
  const fileError = fileProblem ? message(fileProblem) : undefined

  async function send(replace: boolean) {
    if (!file || busyRef.current) return
    busyRef.current = true
    setBusy(true)
    setProblem(null)
    setConfirmReplace(false)
    try {
      const track = await upload.mutateAsync({ runId: run.id, kind, file, replace })
      onUploaded(track)
    } catch (caught) {
      const classified = classifyTrackProblem(caught)
      if (classified.kind === 'exists') setConfirmReplace(true)
      else setProblem(classified)
    } finally {
      busyRef.current = false
      setBusy(false)
    }
  }

  function start() {
    if (!file || fileProblem) return
    if (existing) setConfirmReplace(true)
    else void send(false)
  }

  return (
    <ResponsiveDialog
      title={t('upload.title')}
      description={t('upload.description')}
      onClose={() => (busy ? undefined : onClose())}
      size="md"
      panel="track-upload"
      returnFocusTo={returnFocusTo}
      footer={
        <>
          <Button type="button" variant="outline" className="h-11 sm:h-9" disabled={busy} onClick={onClose}>
            {t('actions.cancel')}
          </Button>
          <Button type="button" className="h-11 sm:h-9" disabled={!file || !!fileProblem || busy} onClick={start}>
            {busy ? t('upload.uploading') : t(existing ? 'actions.replaceFile' : 'actions.upload')}
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-4">
        {problem ? (
          <p role="alert" className="rounded-md border border-destructive/60 p-3 text-sm text-destructive">
            {message(problem)}
          </p>
        ) : null}

        <fieldset className="flex flex-col gap-2">
          <legend className="mb-1 text-sm font-medium">{t('upload.kind')}</legend>
          <div className="flex flex-wrap gap-2">
            {TRACK_KINDS.map((value) => (
              <label
                key={value}
                className="flex min-h-11 cursor-pointer items-center gap-2 rounded-md border px-3 has-checked:border-primary has-checked:bg-primary/5 has-focus-visible:ring-[3px] has-focus-visible:ring-ring/50 sm:min-h-9"
              >
                <input
                  type="radio"
                  name="track-kind"
                  value={value}
                  checked={kind === value}
                  disabled={busy}
                  onChange={() => setKind(value)}
                  className="size-4"
                />
                {t(`kind.${value}`)}
              </label>
            ))}
          </div>
          <p className="text-sm text-muted-foreground">{t(`upload.kindHint.${kind}`)}</p>
          {existing ? (
            <p className="text-sm" data-testid="replace-notice">
              {t('upload.replaceNotice', { file: existing.originalFileName })}
            </p>
          ) : null}
        </fieldset>

        <div className="flex flex-col gap-2">
          <Label htmlFor={inputId}>{t('upload.file')}</Label>
          <Input
            id={inputId}
            type="file"
            accept=".gpx,application/gpx+xml,application/xml,text/xml"
            disabled={busy}
            aria-invalid={fileError ? true : undefined}
            aria-describedby={[hintId, fileError ? errorId : null].filter(Boolean).join(' ')}
            className="h-auto min-h-11 py-2 sm:min-h-9"
            onChange={(event) => {
              const chosen = event.target.files?.[0] ?? null
              setFile(chosen)
              setProblem(null)
              setFileProblem(chosen ? preCheck(chosen, limit) : null)
            }}
          />
          <p id={hintId} className="text-sm text-muted-foreground">
            {t('upload.hint', { size: megabytes })}
          </p>
          {fileError ? (
            <p id={errorId} role="alert" className="text-sm text-destructive">
              {fileError}
            </p>
          ) : null}
        </div>

        {/* Progress is an indeterminate state: fetch reports no upload progress, so say what is happening. */}
        <output className="block min-h-5 text-sm text-muted-foreground">{busy ? t('upload.progress') : ''}</output>
      </div>

      <AlertDialog open={confirmReplace} onOpenChange={(open) => (open ? undefined : setConfirmReplace(false))}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>{t('replace.title')}</AlertDialogTitle>
            <AlertDialogDescription>
              {existing
                ? t('replace.description', { kind: t(`kindLower.${kind}`), file: existing.originalFileName })
                : t('replace.descriptionUnknown', { kind: t(`kindLower.${kind}`) })}
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel className="h-11 sm:h-9">{t('actions.cancel')}</AlertDialogCancel>
            <AlertDialogAction
              className="h-11 sm:h-9"
              onClick={(event) => {
                event.preventDefault()
                void send(true)
              }}
            >
              {t('actions.replaceFile')}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </ResponsiveDialog>
  )
}
