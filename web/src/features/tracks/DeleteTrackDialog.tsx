import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
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
import { classifyTrackProblem, trackProblemMessage } from './trackProblem'
import { useDeleteTrack, useInvalidateTracks } from './trackQueries'
import type { RunTrackDto } from './trackTypes'
import '@/i18n/lazyBundles'

/**
 * Confirmation before deleting a GPX file (ADR 0023 §5). The server deletes the blobs first: a storage outage is a
 * retryable 503 and the dialog stays open with the message. A 404 means the file is already gone, which is the
 * outcome the user wanted.
 */
export function DeleteTrackDialog({
  runId,
  track,
  origin,
  onClose,
  onDeleted,
}: {
  runId: string
  track: RunTrackDto | null
  origin: HTMLElement | null
  onClose: () => void
  onDeleted: () => void
}) {
  const { t } = useTranslation('tracks')
  const { t: tErrors } = useTranslation('errors')
  const remove = useDeleteTrack()
  const invalidate = useInvalidateTracks(runId)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function confirm() {
    if (!track || busy) return
    setBusy(true)
    setError(null)
    try {
      await remove.mutateAsync({ params: { path: { runId, trackId: track.id } } })
      await invalidate(track.id)
      toast.success(t('toast.deleted'))
      onClose()
      onDeleted()
    } catch (caught) {
      const problem = classifyTrackProblem(caught)
      if (problem.kind === 'other' && problem.problem.kind === 'notFound') {
        await invalidate(track.id)
        toast.info(t('toast.gone'))
        onClose()
        onDeleted()
      } else {
        setError(trackProblemMessage(problem, t, tErrors))
      }
    } finally {
      setBusy(false)
    }
  }

  return (
    <AlertDialog
      open={track !== null}
      onOpenChange={(open) => {
        if (!open && !busy) {
          setError(null)
          onClose()
        }
      }}
    >
      <AlertDialogContent
        onCloseAutoFocus={(event) => {
          event.preventDefault()
          if (origin?.isConnected) origin.focus()
        }}
      >
        <AlertDialogHeader>
          <AlertDialogTitle>{t('delete.title')}</AlertDialogTitle>
          <AlertDialogDescription>
            {t('delete.description', {
              kind: track ? t(`kindLower.${track.kind}`) : '',
              file: track?.originalFileName ?? '',
            })}
          </AlertDialogDescription>
        </AlertDialogHeader>
        {error ? (
          <p role="alert" className="text-sm text-destructive">
            {error}
          </p>
        ) : null}
        <AlertDialogFooter>
          <AlertDialogCancel disabled={busy} className="h-11 sm:h-9">
            {t('actions.cancel')}
          </AlertDialogCancel>
          <AlertDialogAction
            variant="destructive"
            disabled={busy}
            className="h-11 sm:h-9"
            onClick={(event) => {
              event.preventDefault()
              void confirm()
            }}
          >
            {busy ? t('actions.deleting') : t('actions.delete')}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  )
}
