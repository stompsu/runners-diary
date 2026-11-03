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
import { parseProblem } from '@/lib/api/problem'
import { useDeleteRun, useInvalidateRuns } from './runQueries'
import { writeProblemMessage } from './runProblemMessage'

export interface DeleteTarget {
  id: string
  /** "Easy run on Saturday, November 14, 2026" */
  summary: string
}

/**
 * Confirmation before DELETE (AC8). Stays open with an inline message when the call fails; a 404 means the run is
 * already gone, which is the outcome the user wanted. Focus returns to the control that opened it, or to the panel
 * title when that control disappeared with the run.
 */
export function DeleteRunDialog({
  target,
  origin,
  onClose,
  onDeleted,
}: {
  target: DeleteTarget | null
  /** The element that had the focus when the dialog opened. */
  origin: HTMLElement | null
  onClose: () => void
  /** Called after the run is gone (deleted, or found already deleted). */
  onDeleted: (id: string) => void
}) {
  const { t } = useTranslation('runs')
  const { t: tErrors } = useTranslation('errors')
  const remove = useDeleteRun()
  const invalidate = useInvalidateRuns()
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function confirm() {
    if (!target || busy) return
    setBusy(true)
    setError(null)
    try {
      await remove.mutateAsync({ params: { path: { id: target.id } } })
      await invalidate(target.id)
      toast.success(t('toast.deleted'))
      onDeleted(target.id)
      onClose()
    } catch (caught) {
      const problem = parseProblem(caught)
      if (problem.kind === 'notFound') {
        await invalidate(target.id)
        toast.info(t('toast.gone'))
        onDeleted(target.id)
        onClose()
      } else {
        setError(writeProblemMessage(problem, tErrors))
      }
    } finally {
      setBusy(false)
    }
  }

  return (
    <AlertDialog open={target !== null} onOpenChange={(open) => (open || busy ? undefined : onClose())}>
      <AlertDialogContent
        onCloseAutoFocus={(event) => {
          event.preventDefault()
          if (origin?.isConnected) origin.focus()
          else document.querySelector<HTMLElement>('[data-panel] [data-slot="dialog-title"]')?.focus()
        }}
      >
        <AlertDialogHeader>
          <AlertDialogTitle>{t('delete.title')}</AlertDialogTitle>
          <AlertDialogDescription>{t('delete.description', { run: target?.summary ?? '' })}</AlertDialogDescription>
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
