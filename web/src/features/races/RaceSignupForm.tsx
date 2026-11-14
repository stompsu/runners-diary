import { zodResolver } from '@hookform/resolvers/zod'
import { useState, type ReactNode } from 'react'
import { useForm } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate } from 'react-router'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { FieldError, FieldLegend, FieldSet } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { diaryPath } from '@/features/diary/diaryMonth'
import { FormField } from '@/features/runs/FormField'
import { writeProblemMessage } from '@/features/runs/runProblemMessage'
import { useInvalidateRuns } from '@/features/runs/runQueries'
import { usePanelMonth } from '@/features/runs/usePanel'
import { parseProblem } from '@/lib/api/problem'
import { dayLabel } from '@/lib/dates/plainDate'
import { formatRaceDistances } from './raceFormat'
import { raceConflict } from './raceProblems'
import { useSignUpForRace } from './raceQueries'
import { raceSignupSchema, toSignupRequest, type RaceSignupFormValues } from './raceSignupSchema'
import type { RaceDetails } from './raceTypes'
import '@/i18n/lazyBundles'

/**
 * "I signed up" (ADR 0036 §5): creates a planned race run from the listing. A distance choice appears only when the
 * race has more than one; the start time and a target time are optional. On success the run opens.
 */
export function RaceSignupForm({ race, onCancel }: { race: RaceDetails; onCancel: () => void }) {
  const { t, i18n } = useTranslation('races')
  const { t: tErrors } = useTranslation('errors')
  const navigate = useNavigate()
  const month = usePanelMonth()
  const signUp = useSignUpForRace()
  const invalidate = useInvalidateRuns()
  const [formError, setFormError] = useState<ReactNode>(null)
  const distances = race.distancesMeters
  const choose = distances.length > 1
  const only = distances.length === 1 ? distances[0]! : null

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<RaceSignupFormValues>({
    resolver: zodResolver(raceSignupSchema(choose)),
    defaultValues: { distanceMeters: '', startTime: race.startTime ?? '', targetTime: '' },
  })
  const message = (key: string | undefined) =>
    key ? t(`signup.validation.${key as 'distanceRequired' | 'startTime' | 'targetTime'}`) : undefined

  async function submit(values: RaceSignupFormValues) {
    setFormError(null)
    try {
      const run = await signUp.mutateAsync({
        params: { path: { raceId: race.raceId } },
        body: toSignupRequest(values, only),
      })
      await invalidate(run.id)
      toast.success(t('signup.success'))
      void navigate(`${diaryPath(month)}/runs/${run.id}`, { replace: true })
    } catch (error) {
      const conflict = raceConflict(error)
      const problem = parseProblem(error)
      if (conflict?.kind === 'alreadyLinked') {
        await invalidate(conflict.runId ?? undefined)
        setFormError(
          <>
            {t('signup.alreadyLinked')}{' '}
            {conflict.runId ? (
              <Link className="underline" to={`${diaryPath(month)}/runs/${conflict.runId}`}>
                {t('details.actions.viewRun')}
              </Link>
            ) : null}
          </>,
        )
      } else if (problem.kind === 'notFound') {
        await invalidate()
        setFormError(t('signup.gone'))
      } else if (problem.kind === 'validation' && problem.fieldErrors.raceId) {
        setFormError(t('signup.past'))
      } else {
        setFormError(writeProblemMessage(problem, tErrors))
      }
    }
  }

  return (
    <form
      aria-label={t('signup.title')}
      noValidate
      onSubmit={(event) => void handleSubmit(submit)(event)}
      className="flex flex-col gap-4 rounded-lg border p-3"
    >
      <p className="text-sm text-muted-foreground">{t('signup.intro', { date: dayLabel(race.date, i18n.language) })}</p>

      {choose ? (
        <FieldSet className="gap-2" data-invalid-group={errors.distanceMeters ? 'true' : undefined}>
          <FieldLegend variant="label">{t('signup.distance')}</FieldLegend>
          <div role="radiogroup" aria-label={t('signup.distance')} className="flex flex-wrap gap-2">
            {distances.map((meters) => (
              <label
                key={meters}
                className="flex min-h-11 cursor-pointer items-center gap-2 rounded-md border px-3 has-checked:border-primary has-checked:bg-primary/5 has-focus-visible:ring-[3px] has-focus-visible:ring-ring/50 sm:min-h-9"
              >
                <input type="radio" value={String(meters)} className="size-4" {...register('distanceMeters')} />
                {formatRaceDistances([meters], i18n.language)}
              </label>
            ))}
          </div>
          {errors.distanceMeters ? <FieldError>{message(errors.distanceMeters.message)}</FieldError> : null}
        </FieldSet>
      ) : only !== null ? (
        <p className="text-sm">
          {t('signup.distanceSingle', { distance: formatRaceDistances([only], i18n.language) })}
        </p>
      ) : null}

      <div className="grid gap-4 sm:grid-cols-2">
        <FormField label={t('signup.startTime')} error={message(errors.startTime?.message)}>
          {(control) => <Input {...control} type="time" className="h-11 sm:h-9" {...register('startTime')} />}
        </FormField>
        <FormField label={t('signup.targetTime')} error={message(errors.targetTime?.message)}>
          {(control) => (
            <Input
              {...control}
              inputMode="numeric"
              placeholder={t('signup.targetTimePlaceholder')}
              className="h-11 sm:h-9"
              {...register('targetTime')}
            />
          )}
        </FormField>
      </div>

      {formError ? (
        <p role="alert" className="text-sm text-destructive">
          {formError}
        </p>
      ) : null}

      <div className="flex flex-wrap gap-2">
        <Button type="submit" className="h-11 sm:h-9" disabled={isSubmitting}>
          {t(isSubmitting ? 'signup.submitting' : 'signup.submit')}
        </Button>
        <Button type="button" variant="outline" className="h-11 sm:h-9" onClick={onCancel}>
          {t('signup.cancel')}
        </Button>
      </div>
    </form>
  )
}
