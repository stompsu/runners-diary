import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { toFormValues, toWriteRequest } from '@/features/runs/runFormMapping'
import { writeProblemMessage } from '@/features/runs/runProblemMessage'
import { useInvalidateRuns, useRun, useUpdateRun } from '@/features/runs/runQueries'
import { parseProblem } from '@/lib/api/problem'
import { dayLabel } from '@/lib/dates/plainDate'
import type { RaceDetails } from './raceTypes'
import { useRaceStateChange } from './useRaceStateChange'
import '@/i18n/lazyBundles'

const banner =
  'flex flex-col items-start gap-2 rounded-lg border border-amber-600/60 bg-amber-50 p-3 text-sm text-amber-950'

/**
 * The listing's date differs from the user's: for a signed-up race the planned run is on another day (button: move
 * the run), for an Interested one the user acknowledges the new date (button: OK, which re-sends Interested).
 */
export function DateChangedBanner({ race }: { race: RaceDetails }) {
  if (race.state === 'SignedUp' && race.linkedRunId) return <SignedUpBanner race={race} runId={race.linkedRunId} />
  if (race.state === 'Interested') return <InterestedBanner race={race} />
  return null
}

function InterestedBanner({ race }: { race: RaceDetails }) {
  const { t, i18n } = useTranslation('races')
  const { change, pending } = useRaceStateChange()
  const newDate = dayLabel(race.date, i18n.language)
  return (
    <output data-banner="date-changed" className={banner}>
      <p>
        {race.previousDate
          ? t('banners.dateChangedInterested', { newDate, oldDate: dayLabel(race.previousDate, i18n.language) })
          : t('banners.dateChangedInterestedNoOld', { newDate })}
      </p>
      <Button
        type="button"
        variant="outline"
        className="h-11 bg-background sm:h-9"
        disabled={pending}
        onClick={() => void change(race.raceId, 'Interested')}
      >
        {t('banners.ok')}
      </Button>
    </output>
  )
}

function SignedUpBanner({ race, runId }: { race: RaceDetails; runId: string }) {
  const { t, i18n } = useTranslation('races')
  const { t: tErrors } = useTranslation('errors')
  const run = useRun(runId)
  const update = useUpdateRun()
  const invalidate = useInvalidateRuns()
  const [conflict, setConflict] = useState(false)
  const [error, setError] = useState<string | null>(null)
  if (!run.data) return null
  const loaded = run.data
  const newDate = dayLabel(race.date, i18n.language)

  async function move() {
    setError(null)
    // A full PUT built from the loaded run: every other field stays as the user has it.
    const body = toWriteRequest({ ...toFormValues(loaded), date: race.date }, loaded)
    try {
      await update.mutateAsync({ params: { path: { id: loaded.id } }, body })
      await invalidate(loaded.id)
      toast.success(t('banners.moved', { date: newDate }))
      window.setTimeout(
        () => document.querySelector<HTMLElement>('[data-panel] [data-slot="dialog-title"]')?.focus(),
        0,
      )
    } catch (caught) {
      const problem = parseProblem(caught)
      if (problem.kind === 'concurrency') setConflict(true)
      else setError(writeProblemMessage(problem, tErrors))
    }
  }

  return (
    <output data-banner="date-changed" className={banner}>
      <p>{t('banners.dateChangedSignedUp', { newDate, oldDate: dayLabel(loaded.date, i18n.language) })}</p>
      {conflict ? (
        <>
          <p role="alert">{t('banners.conflict')}</p>
          <Button
            type="button"
            variant="outline"
            className="h-11 bg-background sm:h-9"
            onClick={() => void run.refetch().then(() => setConflict(false))}
          >
            {t('banners.reload')}
          </Button>
        </>
      ) : (
        <>
          {error ? <p role="alert">{error}</p> : null}
          <Button type="button" className="h-11 sm:h-9" disabled={update.isPending} onClick={() => void move()}>
            {t(update.isPending ? 'banners.moving' : 'banners.moveRun', { date: newDate })}
          </Button>
        </>
      )}
    </output>
  )
}
