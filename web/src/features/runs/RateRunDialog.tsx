import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ResponsiveDialog } from '@/components/ResponsiveDialog'
import { Button } from '@/components/ui/button'
import { Textarea } from '@/components/ui/textarea'
import { parseProblem } from '@/lib/api/problem'
import { RatingField } from './RatingField'
import { toFormValues, toWriteRequest } from './runFormMapping'
import type { RunDto } from './runFormTypes'
import { writeProblemMessage } from './runProblemMessage'
import { fetchRun, useInvalidateRuns, useUpdateRun } from './runQueries'

/**
 * Rating after the fact: stars (1-5) and a comment for a completed run. The PUT is the form's own mapping applied
 * to the loaded run plus the new rating, so nothing else about the run changes (ADR 0018 §6).
 */
export function RateRunDialog({ run, onClose, onGone }: { run: RunDto; onClose: () => void; onGone: () => void }) {
  const { t } = useTranslation('runs')
  const { t: tErrors } = useTranslation('errors')
  const queryClient = useQueryClient()
  const update = useUpdateRun()
  const invalidate = useInvalidateRuns()
  const [base, setBase] = useState(run)
  const [stars, setStars] = useState(run.rating?.stars ?? 0)
  const [comment, setComment] = useState(run.rating?.comment ?? '')
  const [error, setError] = useState<string | null>(null)
  const [needsStars, setNeedsStars] = useState(false)
  const [busy, setBusy] = useState(false)

  async function save() {
    if (busy) return
    if (stars < 1) {
      setNeedsStars(true)
      return
    }
    setBusy(true)
    setError(null)
    setNeedsStars(false)
    try {
      const values = { ...toFormValues(base), rating: { stars, comment } }
      await update.mutateAsync({ params: { path: { id: base.id } }, body: toWriteRequest(values, base) })
      await invalidate(base.id)
      toast.success(t('toast.rated'))
      onClose()
    } catch (caught) {
      const problem = parseProblem(caught)
      if (problem.kind === 'notFound') {
        await invalidate(base.id)
        toast.error(t('toast.gone'))
        onGone()
      } else if (problem.kind === 'concurrency') {
        // Someone changed the run: take the latest version and keep the typed rating; the user saves again.
        try {
          setBase(await fetchRun(queryClient, base.id))
          setError(t('rate.conflict'))
        } catch {
          setError(tErrors('problem.concurrency'))
        }
      } else if (problem.kind === 'validation') {
        setError(Object.values(problem.fieldErrors).flat().join(' ') || tErrors('problem.validation'))
      } else {
        setError(writeProblemMessage(problem, tErrors))
      }
    } finally {
      setBusy(false)
    }
  }

  return (
    <ResponsiveDialog
      title={t('rate.title')}
      onClose={onClose}
      restoreFocusDate={run.date}
      panel="rate"
      footer={
        <>
          <Button type="button" variant="outline" className="h-11 sm:h-9" onClick={onClose}>
            {t('actions.cancel')}
          </Button>
          <Button type="button" className="h-11 sm:h-9" disabled={busy} onClick={() => void save()}>
            {busy ? t('actions.saving') : t('actions.save')}
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-4">
        {error ? (
          <p role="alert" className="rounded-md border border-destructive/60 p-3 text-sm text-destructive">
            {error}
          </p>
        ) : null}
        <RatingField
          value={stars}
          onChange={setStars}
          error={needsStars ? t('validation.ratingRequired') : undefined}
        />
        <label className="flex flex-col gap-2 text-sm font-medium">
          {t('rating.comment')}
          <Textarea rows={3} maxLength={500} value={comment} onChange={(event) => setComment(event.target.value)} />
        </label>
      </div>
    </ResponsiveDialog>
  )
}
