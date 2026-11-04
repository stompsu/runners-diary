import { zodResolver } from '@hookform/resolvers/zod'
import { useQueryClient } from '@tanstack/react-query'
import { useEffect, useMemo, useRef, useState } from 'react'
import { Controller, useForm, useWatch, type Path } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { FieldLegend, FieldSet } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { NativeSelect, NativeSelectOption } from '@/components/ui/native-select'
import { Textarea } from '@/components/ui/textarea'
import { runTypeLabelKey } from '@/features/calendar/runTypePresentation'
import { useDebouncedValue } from '@/hooks/useDebouncedValue'
import { parseProblem, type ApiProblem } from '@/lib/api/problem'
import { parseDuration } from '@/lib/format/duration'
import { formatPace, kmToMeters, parseKm } from '@/lib/format/units'
import { FormField } from './FormField'
import { useFieldMessage } from './useFieldMessage'
import { LocationField } from './LocationField'
import { RaceFields } from './RaceFields'
import { RatingField } from './RatingField'
import { TimeZoneField } from './TimeZoneField'
import { createRunFormSchema } from './runFormSchema'
import { applyPrefill, applyRunType, applyStatus, METRIC_FIELDS, toFormValues, toWriteRequest } from './runFormMapping'
import {
  SURFACES,
  rulesOf,
  type PrefillState,
  type RunDto,
  type RunFormValues,
  type RunTypeDto,
  type RunWriteRequest,
} from './runFormTypes'
import { writeProblemMessage } from './runProblemMessage'
import { fetchRun, useCreateRun, useInvalidateRuns, useUpdateRun } from './runQueries'
import { mapRunFieldErrors } from './serverErrorMapping'

const RACE_RESULT_FIELDS = new Set(['race.resultTime', 'race.overallPlace', 'race.categoryPlace'])

/** Whether the form currently renders an input for `name` (mirrors the conditional sections below). */
function isFieldShown(name: string, values: RunFormValues, rules: ReturnType<typeof rulesOf>): boolean {
  const completed = values.status === 'Completed'
  if (name.startsWith('race.')) return values.race !== null && (!RACE_RESULT_FIELDS.has(name) || completed)
  if (name.startsWith('rating.')) return completed && !rules.isRestDay
  if ((METRIC_FIELDS as readonly string[]).includes(name)) return !rules.isRestDay
  return true
}

/** How long the pace must stay unchanged before a screen reader hears it. */
const PACE_ANNOUNCE_DELAY_MS = 1000

export interface RunFormProps {
  /** The loaded run (edit) or undefined (create). */
  run?: RunDto
  /** Create: initial values (date from `?date=`, the default type). */
  initialValues?: RunFormValues
  /** Edit with Mark as completed: start as Completed and focus the first empty actual. */
  completePreset?: boolean
  types: readonly RunTypeDto[]
  codes: ReadonlyMap<number, string>
  /** The profile zone, used as the zone of a new run. */
  profileZone: string
  onSaved: () => void
  /** Create only: "Save & add GPX" saves, then hands the new run over so its upload dialog can open. */
  onSavedAndAddGpx?: (run: RunDto) => void
  /** Edit opened from the GPX review (ADR 0023 §3): the values to put into the form, applied once as dirty values. */
  prefill?: PrefillState['prefill']
  /** The run no longer exists (404 on save): the caller closes the panel. */
  onGone: () => void
  onCancel: () => void
}

interface FormProblem {
  message: string
  details: string[]
}

/**
 * The add/edit form (ADR 0018). Display units in, SI out (`toWriteRequest`); the zod schema gives instant feedback and
 * the server's 400s land on the matching fields. A 409 concurrency conflict keeps the user's values and offers
 * "Reload latest" / "Save mine anyway".
 */
export function RunForm({
  run,
  initialValues,
  completePreset,
  types,
  codes,
  profileZone,
  onSaved,
  onSavedAndAddGpx,
  prefill,
  onGone,
  onCancel,
}: RunFormProps) {
  const { t } = useTranslation('runs')
  const { t: tErrors } = useTranslation('errors')
  const { t: tType } = useTranslation('runTypes')
  const queryClient = useQueryClient()
  const invalidate = useInvalidateRuns()
  const create = useCreateRun()
  const update = useUpdateRun()
  const message = useFieldMessage()
  const [base, setBase] = useState<RunDto | undefined>(run)
  const [problem, setProblem] = useState<FormProblem | null>(null)
  const [conflict, setConflict] = useState(false)
  const [resolving, setResolving] = useState(false)
  const summaryRef = useRef<HTMLDivElement>(null)
  const saving = useRef(false)

  const schema = useMemo(() => createRunFormSchema(types), [types])
  const defaults = useMemo<RunFormValues>(() => {
    const values = run ? toFormValues(run) : initialValues!
    return completePreset && run?.status === 'Planned' ? applyStatus(values, 'Completed') : values
  }, [run, initialValues, completePreset])

  const form = useForm<RunFormValues>({ resolver: zodResolver(schema), defaultValues: defaults })
  const { register, control, handleSubmit, setValue, getValues, setError, setFocus, reset, formState } = form
  const { errors } = formState
  const status = useWatch({ control, name: 'status' })
  const runTypeId = useWatch({ control, name: 'runTypeId' })
  const race = useWatch({ control, name: 'race' })
  const distanceKm = useWatch({ control, name: 'distanceKm' })
  const duration = useWatch({ control, name: 'duration' })
  const zoneChoice = useWatch({ control, name: 'timeZoneId' })
  const rules = rulesOf(types.find((type) => type.id === runTypeId))
  const completed = status === 'Completed'

  const km = parseKm(distanceKm)
  const seconds = parseDuration(duration)
  const pace = km !== null && seconds !== null ? formatPace(seconds, kmToMeters(km)) : null

  const announcedPace = useDebouncedValue(pace, PACE_ANNOUNCE_DELAY_MS)

  // Mark as completed: put the cursor on the first actual that is still empty.
  useEffect(() => {
    if (!completePreset) return
    const first = getValues('distanceKm') === '' ? 'distanceKm' : getValues('duration') === '' ? 'duration' : null
    if (first) setFocus(first)
  }, [completePreset, getValues, setFocus])

  // The GPX values go in as edits (dirty), once; they are never saved until the user presses Save (ADR 0023 §3).
  const prefillApplied = useRef(false)
  useEffect(() => {
    if (!prefill || prefillApplied.current) return
    prefillApplied.current = true
    reset(applyPrefill(getValues(), prefill.patch), { keepDefaultValues: true })
  }, [prefill, reset, getValues])

  useEffect(() => {
    if (problem) summaryRef.current?.focus()
  }, [problem])

  function changeStatus(next: RunFormValues['status']) {
    const values = applyStatus(getValues(), next)
    setValue('status', values.status)
    setValue('rating', values.rating)
    setValue('race', values.race)
  }

  function changeType(id: number) {
    const values = applyRunType(getValues(), id, rulesOf(types.find((type) => type.id === id)))
    setValue('runTypeId', id)
    setValue('race', values.race)
    setValue('rating', values.rating)
    for (const field of METRIC_FIELDS) {
      setValue(field, values[field])
    }
  }

  async function send(values: RunFormValues, target: RunDto | undefined): Promise<RunDto> {
    const body: RunWriteRequest = toWriteRequest(values, target)
    return target ? update.mutateAsync({ params: { path: { id: target.id } }, body }) : create.mutateAsync({ body })
  }

  function showProblem(apiProblem: ApiProblem) {
    setConflict(false)
    switch (apiProblem.kind) {
      case 'validation': {
        const mapped = mapRunFieldErrors(apiProblem.fieldErrors)
        // A server error on a field this form is not showing right now (race section of a non-race type, ...)
        // would be invisible: it goes to the summary with the others.
        const current = getValues()
        const currentRules = rulesOf(types.find((type) => type.id === current.runTypeId))
        const fields: Record<string, string> = {}
        const summary = [...mapped.summary]
        for (const [name, text] of Object.entries(mapped.fields)) {
          if (isFieldShown(name, current, currentRules)) fields[name] = text
          else summary.push(text)
        }
        const names = Object.keys(fields)
        names.forEach((name, index) =>
          setError(
            name as Path<RunFormValues>,
            { type: 'server', message: fields[name] },
            { shouldFocus: index === 0 && summary.length === 0 },
          ),
        )
        // Everything mapped onto fields: the fields speak for themselves and the first one gets the focus.
        // Anything unmapped (or an empty error list) goes to a summary that takes the focus instead.
        setProblem(
          summary.length > 0 || names.length === 0
            ? { message: tErrors('problem.validation'), details: summary }
            : null,
        )
        return
      }
      case 'concurrency':
        setProblem(null)
        setConflict(true)
        return
      default:
        setProblem({ message: writeProblemMessage(apiProblem, tErrors), details: [] })
    }
  }

  async function submit(values: RunFormValues, target: RunDto | undefined, addGpx = false) {
    if (saving.current) return
    saving.current = true
    setProblem(null)
    setConflict(false)
    try {
      const saved = await send(values, target)
      await invalidate(saved.id)
      toast.success(t(target ? 'toast.saved' : 'toast.created'))
      if (addGpx && onSavedAndAddGpx) onSavedAndAddGpx(saved)
      else onSaved()
    } catch (caught) {
      const apiProblem = parseProblem(caught)
      if (apiProblem.kind === 'notFound' && target) {
        await invalidate(target.id)
        toast.error(t('toast.gone'))
        onGone()
      } else {
        showProblem(apiProblem)
      }
    } finally {
      saving.current = false
    }
  }

  async function latestOrGone(): Promise<RunDto | null> {
    if (!base) return null
    try {
      return await fetchRun(queryClient, base.id)
    } catch (caught) {
      const apiProblem = parseProblem(caught)
      if (apiProblem.kind === 'notFound') {
        await invalidate(base.id)
        toast.error(t('toast.gone'))
        onGone()
      } else {
        showProblem(apiProblem)
      }
      return null
    }
  }

  async function reloadLatest() {
    setResolving(true)
    const latest = await latestOrGone()
    if (latest) {
      setBase(latest)
      reset(toFormValues(latest))
      setConflict(false)
      toast.info(t('toast.reloaded'))
    }
    setResolving(false)
  }

  async function saveMineAnyway() {
    setResolving(true)
    const latest = await latestOrGone()
    if (latest) {
      setBase(latest)
      // Same validation as a normal submit, but against the latest `version`.
      await handleSubmit((values) => submit(values, latest))()
    }
    setResolving(false)
  }

  const busy = formState.isSubmitting || resolving
  const field = 'h-11 sm:h-9'

  return (
    <form
      noValidate
      aria-label={t(run ? 'form.editTitle' : 'form.addTitle')}
      onSubmit={(event) => void handleSubmit((values) => submit(values, base))(event)}
      className="flex flex-col gap-6"
    >
      {problem ? (
        <div
          ref={summaryRef}
          role="alert"
          tabIndex={-1}
          className="rounded-md border border-destructive/60 p-3 text-sm"
        >
          <p className="font-medium text-destructive">{problem.message}</p>
          {problem.details.length > 0 ? (
            <ul className="mt-1 list-disc pl-5">
              {problem.details.map((detail) => (
                <li key={detail}>{detail}</li>
              ))}
            </ul>
          ) : null}
        </div>
      ) : null}
      {prefill ? (
        <output
          className="block rounded-md border border-primary/40 bg-primary/5 p-3 text-sm"
          data-testid="prefill-notice"
        >
          {t('form.prefillNotice', { file: prefill.source })}
        </output>
      ) : null}
      {conflict ? (
        <div role="alert" className="flex flex-col gap-3 rounded-md border border-destructive/60 p-3 text-sm">
          <p className="font-medium text-destructive">{tErrors('problem.concurrency')}</p>
          <p>{t('conflict.explanation')}</p>
          <div className="flex flex-wrap gap-2">
            <Button
              type="button"
              variant="outline"
              className={field}
              disabled={busy}
              onClick={() => void reloadLatest()}
            >
              {t('conflict.reload')}
            </Button>
            <Button type="button" className={field} disabled={busy} onClick={() => void saveMineAnyway()}>
              {t('conflict.overwrite')}
            </Button>
          </div>
        </div>
      ) : null}

      <FieldSet className="gap-4">
        <FieldLegend>{t('form.when')}</FieldLegend>
        <div className="grid gap-4 sm:grid-cols-2">
          <FormField label={t('fields.date')} error={message(errors.date?.message)}>
            {(controlProps) => (
              <Input
                {...controlProps}
                type="date"
                min="1950-01-01"
                max="2100-12-31"
                className={field}
                {...register('date')}
              />
            )}
          </FormField>
          <FormField label={t('fields.startTime')} error={message(errors.startTime?.message)}>
            {(controlProps) => <Input {...controlProps} type="time" className={field} {...register('startTime')} />}
          </FormField>
        </div>
        <TimeZoneField
          baseZone={base?.timeZoneId ?? profileZone}
          value={zoneChoice}
          onChange={(zone) => setValue('timeZoneId', zone, { shouldDirty: true })}
          error={message(errors.timeZoneId?.message)}
        />
      </FieldSet>

      <FieldSet className="gap-4">
        <FieldLegend>{t('form.what')}</FieldLegend>
        <div role="radiogroup" aria-label={t('fields.status')} className="flex flex-wrap gap-2">
          {(['Planned', 'Completed'] as const).map((value) => (
            <label
              key={value}
              className="flex min-h-11 cursor-pointer items-center gap-2 rounded-md border px-3 has-checked:border-primary has-checked:bg-primary/5 has-focus-visible:ring-[3px] has-focus-visible:ring-ring/50 sm:min-h-9"
            >
              <input
                type="radio"
                name="status"
                value={value}
                checked={status === value}
                onChange={() => changeStatus(value)}
                className="size-4"
              />
              {t(value === 'Completed' ? 'status.completed' : 'status.planned')}
            </label>
          ))}
        </div>
        <div className="grid gap-4 sm:grid-cols-2">
          <FormField label={t('fields.type')} error={message(errors.runTypeId?.message)}>
            {(controlProps) => (
              <NativeSelect
                {...controlProps}
                className={field}
                value={runTypeId}
                onChange={(event) => changeType(Number(event.target.value))}
              >
                {(['Race', 'Training', 'Other'] as const).map((category) => (
                  <optgroup key={category} label={t(`form.typeGroup.${category}`)}>
                    {types
                      .filter((type) => type.category === category)
                      .map((type) => (
                        <NativeSelectOption key={type.id} value={type.id}>
                          {tType(runTypeLabelKey(codes.get(type.id)))}
                        </NativeSelectOption>
                      ))}
                  </optgroup>
                ))}
              </NativeSelect>
            )}
          </FormField>
          <FormField label={t('fields.title')} error={message(errors.title?.message)}>
            {(controlProps) => <Input {...controlProps} maxLength={120} className={field} {...register('title')} />}
          </FormField>
        </div>
      </FieldSet>

      {rules.isRestDay ? null : (
        <FieldSet className="gap-4">
          <FieldLegend>{t('form.numbers')}</FieldLegend>
          <div className="grid gap-4 sm:grid-cols-2">
            <FormField label={t('fields.distance')} error={message(errors.distanceKm?.message)}>
              {(controlProps) => (
                <Input {...controlProps} inputMode="decimal" className={field} {...register('distanceKm')} />
              )}
            </FormField>
            <FormField
              label={t('fields.duration')}
              hint={t('fields.durationHint')}
              error={message(errors.duration?.message)}
            >
              {(controlProps) => (
                <Input
                  {...controlProps}
                  inputMode="numeric"
                  placeholder={t('fields.durationPlaceholder')}
                  className={field}
                  {...register('duration')}
                />
              )}
            </FormField>
            <p className="text-sm sm:col-span-2">
              <span className="text-muted-foreground">{t('fields.pace')}: </span>
              {/* Updates on every keystroke but is not a live region: announcing each digit would be noise. */}
              <span data-testid="pace">{pace ?? t('fields.paceUnknown')}</span>
              {/* The announcement settles first, then speaks once (an <output> is a polite status region). */}
              <output className="sr-only" data-testid="pace-announcement">
                {announcedPace ? `${t('fields.pace')}: ${announcedPace}` : ''}
              </output>
            </p>
            <FormField label={t('fields.elevationGain')} error={message(errors.elevationGain?.message)}>
              {(controlProps) => (
                <Input {...controlProps} inputMode="decimal" className={field} {...register('elevationGain')} />
              )}
            </FormField>
            <FormField label={t('fields.elevationLoss')} error={message(errors.elevationLoss?.message)}>
              {(controlProps) => (
                <Input {...controlProps} inputMode="decimal" className={field} {...register('elevationLoss')} />
              )}
            </FormField>
            <FormField label={t('fields.avgHeartRate')} error={message(errors.avgHeartRate?.message)}>
              {(controlProps) => (
                <Input {...controlProps} inputMode="numeric" className={field} {...register('avgHeartRate')} />
              )}
            </FormField>
            <FormField label={t('fields.maxHeartRate')} error={message(errors.maxHeartRate?.message)}>
              {(controlProps) => (
                <Input {...controlProps} inputMode="numeric" className={field} {...register('maxHeartRate')} />
              )}
            </FormField>
            <FormField label={t('fields.rpe')} hint={t('fields.rpeHint')} error={message(errors.rpe?.message)}>
              {(controlProps) => <Input {...controlProps} inputMode="numeric" className={field} {...register('rpe')} />}
            </FormField>
          </div>
        </FieldSet>
      )}

      <Controller
        control={control}
        name="location"
        render={({ field: place }) => (
          <LocationField
            value={place.value}
            onChange={place.onChange}
            nameError={message(errors.location?.name?.message)}
          />
        )}
      />

      <FieldSet className="gap-4">
        <FieldLegend>{t('form.details')}</FieldLegend>
        <div className="grid gap-4 sm:grid-cols-2">
          <FormField label={t('fields.surface')} error={message(errors.surface?.message)}>
            {(controlProps) => (
              <NativeSelect {...controlProps} className={field} {...register('surface')}>
                <NativeSelectOption value="">{t('fields.surfaceNone')}</NativeSelectOption>
                {SURFACES.map((surface) => (
                  <NativeSelectOption key={surface} value={surface}>
                    {t(`surface.${surface}`)}
                  </NativeSelectOption>
                ))}
              </NativeSelect>
            )}
          </FormField>
          <FormField label={t('fields.gear')} error={message(errors.gear?.message)}>
            {(controlProps) => <Input {...controlProps} maxLength={100} className={field} {...register('gear')} />}
          </FormField>
          <FormField label={t('fields.weather')} error={message(errors.weather?.message)} className="sm:col-span-2">
            {(controlProps) => <Input {...controlProps} maxLength={200} className={field} {...register('weather')} />}
          </FormField>
          <FormField label={t('fields.notes')} error={message(errors.notes?.message)} className="sm:col-span-2">
            {(controlProps) => <Textarea {...controlProps} rows={4} maxLength={4000} {...register('notes')} />}
          </FormField>
        </div>
      </FieldSet>

      {race ? <RaceFields register={register} errors={errors} completed={completed} message={message} /> : null}

      {completed && !rules.isRestDay ? (
        <FieldSet className="gap-4">
          <FieldLegend>{t('form.rating')}</FieldLegend>
          <Controller
            control={control}
            name="rating.stars"
            render={({ field: stars }) => (
              <RatingField
                value={stars.value}
                onChange={stars.onChange}
                error={message(errors.rating?.stars?.message)}
              />
            )}
          />
          <FormField label={t('rating.comment')} error={message(errors.rating?.comment?.message)}>
            {(controlProps) => <Textarea {...controlProps} rows={3} maxLength={500} {...register('rating.comment')} />}
          </FormField>
        </FieldSet>
      ) : null}

      <div className="flex flex-wrap justify-end gap-2">
        <Button type="button" variant="outline" className={field} onClick={onCancel}>
          {t('actions.cancel')}
        </Button>
        {!run && onSavedAndAddGpx && !rules.isRestDay ? (
          <Button
            type="button"
            variant="outline"
            className={field}
            disabled={busy}
            onClick={() => void handleSubmit((values) => submit(values, base, true))()}
          >
            {t('actions.saveAndAddGpx')}
          </Button>
        ) : null}
        <Button type="submit" className={field} disabled={busy}>
          {busy ? t('actions.saving') : t('actions.save')}
        </Button>
      </div>
    </form>
  )
}
