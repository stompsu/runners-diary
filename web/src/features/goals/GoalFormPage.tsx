import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo, useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate, useParams } from 'react-router'
import { toast } from 'sonner'
import { useMe } from '@/auth/useMe'
import { Button } from '@/components/ui/button'
import { FieldLegend, FieldSet } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { NativeSelect, NativeSelectOption } from '@/components/ui/native-select'
import { Skeleton } from '@/components/ui/skeleton'
import { Textarea } from '@/components/ui/textarea'
import { QueryError } from '@/features/errors/QueryError'
import { FormField } from '@/features/runs/FormField'
import { SURFACES } from '@/features/runs/runFormTypes'
import { mapFieldErrors, parseProblem } from '@/lib/api/problem'
import { todayFromMe } from '@/lib/dates/plainDate'
import { formatDuration } from '@/lib/format/duration'
import { formatKmInput } from '@/lib/format/units'
import { GoalPresetPicker } from './GoalPresetPicker'
import { RaceRunSelect } from './RaceRunSelect'
import { RealisticTargetHint } from './RealisticTargetHint'
import { goalProblem } from './goalProblems'
import { useCreateGoal, useGoal, useInvalidateGoals, useUpdateGoal } from './goalQueries'
import {
  defaultGoalValues,
  GOAL_FIELD_KEY_MAP,
  GOAL_FORM_FIELDS,
  goalSchema,
  parseGoalNumbers,
  PRESETS,
  toGoalFormValues,
  toGoalRequest,
  type GoalFormValues,
  type PresetId,
} from './goalSchema'
import { GOAL_STATUSES, GOAL_TYPES, type EstimateGoalRequest, type GoalDto } from './goalTypes'
import '@/i18n/lazyBundles'

const VALIDATION_KEYS = new Set([
  'titleRequired',
  'titleTooLong',
  'dateRequired',
  'dateTooSoon',
  'dateTooFar',
  'distance',
  'elevation',
  'targetTime',
  'targetTimeRequired',
  'targetPace',
  'eventRequired',
  'eventTooLong',
  'notesTooLong',
])

/** `/goals/new` and `/goals/:goalId/edit`. */
export function GoalFormPage() {
  const { goalId } = useParams()
  return goalId ? <EditGoal goalId={goalId} /> : <GoalFormLoader />
}

function EditGoal({ goalId }: { goalId: string }) {
  const goal = useGoal(goalId)
  if (goal.isError) return <QueryError error={goal.error} onRetry={() => void goal.refetch()} />
  if (!goal.data) return <Skeleton className="h-96 w-full" />
  return <GoalFormLoader goal={goal.data} onReload={() => void goal.refetch()} />
}

function GoalFormLoader({ goal, onReload }: { goal?: GoalDto; onReload?: () => void }) {
  const me = useMe()
  if (me.isError) return <QueryError error={me.error} onRetry={() => void me.refetch()} />
  if (!me.data) return <Skeleton className="h-96 w-full" />
  return <GoalForm today={todayFromMe(me.data)} goal={goal} onReload={onReload} />
}

function GoalForm({ today, goal, onReload }: { today: string; goal?: GoalDto; onReload?: () => void }) {
  const { t } = useTranslation('goals')
  const navigate = useNavigate()
  const create = useCreateGoal()
  const update = useUpdateGoal()
  const invalidate = useInvalidateGoals()
  const [formError, setFormError] = useState<string | null>(null)
  const [conflict, setConflict] = useState(false)
  const schema = useMemo(() => goalSchema(today, goal?.targetDate), [today, goal?.targetDate])
  const defaults = useMemo(() => (goal ? toGoalFormValues(goal) : defaultGoalValues(today)), [goal, today])
  const {
    register,
    control,
    handleSubmit,
    setValue,
    getValues,
    setError,
    reset,
    formState: { errors, isSubmitting },
  } = useForm<GoalFormValues>({ resolver: zodResolver(schema), defaultValues: defaults })
  const values = useWatch({ control })
  const type = values.type ?? defaults.type
  const field = 'h-11 sm:h-9'
  const message = (text: string | undefined) =>
    text ? (VALIDATION_KEYS.has(text) ? t(`form.validation.${text as 'titleRequired'}`) : text) : undefined

  // The estimate needs a valid distance and date; a half-typed target time must not block it.
  const estimateInput = useMemo<EstimateGoalRequest | null>(() => {
    if (type === 'Distance') return null
    const numbers = parseGoalNumbers({
      distanceKm: values.distanceKm ?? '',
      elevationGain: values.elevationGain ?? '',
      targetTime: values.targetTime ?? '',
    })
    const date = values.targetDate ?? ''
    if (
      numbers.distanceMeters === null ||
      numbers.distanceMeters < 1000 ||
      numbers.distanceMeters > 250_000 ||
      !/^\d{4}-\d{2}-\d{2}$/.test(date) ||
      date < today ||
      ((values.elevationGain ?? '').trim() !== '' &&
        (numbers.elevationGainMeters === null ||
          numbers.elevationGainMeters < 0 ||
          numbers.elevationGainMeters > 10_000))
    ) {
      return null
    }
    return {
      type,
      distanceMeters: numbers.distanceMeters,
      elevationGainMeters: numbers.elevationGainMeters,
      surface: values.surface ? values.surface : null,
      targetDate: date,
      targetDurationSeconds: numbers.targetDurationSeconds,
    }
  }, [type, values.distanceKm, values.elevationGain, values.targetTime, values.targetDate, values.surface, today])

  function pickPreset(id: PresetId) {
    const previous = getValues('preset')
    setValue('preset', id)
    if (id === 'custom') return
    const preset = PRESETS[id]
    setValue('distanceKm', formatKmInput(preset.distanceMeters), { shouldValidate: true })
    setValue('elevationGain', preset.elevationGainMeters == null ? '' : String(preset.elevationGainMeters))
    setValue('surface', preset.surface ?? '')
    // The title follows the preset until the user has typed their own.
    const current = getValues('title')
    const previousTitle = previous === 'custom' ? '' : t(`presets.${previous}`)
    if (current.trim() === '' || current === previousTitle) setValue('title', t(`presets.${id}`))
  }

  async function submit(form: GoalFormValues) {
    setFormError(null)
    setConflict(false)
    try {
      const saved = goal
        ? await update.mutateAsync({ params: { path: { goalId: goal.id } }, body: toGoalRequest(form, goal.version) })
        : await create.mutateAsync({ body: toGoalRequest(form) })
      await invalidate()
      toast.success(t(goal ? 'form.saved' : 'form.created'))
      void navigate(`/goals/${saved.id}`, { replace: true })
    } catch (caught) {
      const problem = goalProblem(caught)
      if (problem.kind === 'concurrency') setConflict(true)
      else if (problem.kind === 'goalLimit') setFormError(t('form.problem.goalLimit'))
      else if (problem.kind === 'notFound') setFormError(t('form.problem.gone'))
      else {
        const api = parseProblem(caught)
        if (api.kind === 'validation') {
          const mapped = mapFieldErrors(api.fieldErrors, {
            keyMap: GOAL_FIELD_KEY_MAP,
            isField: (name) => GOAL_FORM_FIELDS.has(name),
          })
          for (const [name, text] of Object.entries(mapped.fields))
            setError(name as keyof GoalFormValues, { type: 'server', message: text })
          if (mapped.summary.length > 0 || Object.keys(mapped.fields).length === 0)
            setFormError(mapped.summary.join(' ') || t('errors.unknown'))
        } else setFormError(t('errors.unknown'))
      }
    }
  }

  return (
    <form
      aria-labelledby="goal-form-title"
      noValidate
      onSubmit={(event) => void handleSubmit(submit)(event)}
      className="flex max-w-3xl flex-col gap-6"
    >
      <header>
        <Link to={goal ? `/goals/${goal.id}` : '/goals'} className="text-sm underline">
          {t('form.back')}
        </Link>
        <h1 id="goal-form-title" className="mt-1 text-2xl font-semibold">
          {t(goal ? 'form.editTitle' : 'form.createTitle')}
        </h1>
      </header>

      <FieldSet className="gap-4">
        <FieldLegend variant="label">{t('presets.label')}</FieldLegend>
        <GoalPresetPicker value={useWatch({ control, name: 'preset' })} onChange={pickPreset} />
      </FieldSet>

      <FieldSet className="gap-4">
        <FieldLegend variant="label">{t('form.type')}</FieldLegend>
        <div role="radiogroup" aria-label={t('form.type')} className="flex flex-wrap gap-2">
          {GOAL_TYPES.map((value) => (
            <label
              key={value}
              className="flex min-h-11 cursor-pointer items-center gap-2 rounded-md border px-3 text-sm has-checked:border-primary has-checked:bg-primary/5 has-focus-visible:ring-[3px] has-focus-visible:ring-ring/50 sm:min-h-9"
            >
              <input type="radio" value={value} className="size-4" {...register('type')} />
              {t(`types.${value}`)}
            </label>
          ))}
        </div>
        <p className="text-sm text-muted-foreground">{t(`form.typeHint.${type}`)}</p>
      </FieldSet>

      <div className="grid gap-4 sm:grid-cols-2">
        <FormField label={t('form.fields.title')} error={message(errors.title?.message)}>
          {(ctl) => <Input {...ctl} maxLength={120} className={field} {...register('title')} />}
        </FormField>
        <FormField label={t('form.fields.targetDate')} error={message(errors.targetDate?.message)}>
          {(ctl) => <Input {...ctl} type="date" className={field} {...register('targetDate')} />}
        </FormField>
        <FormField label={t('form.fields.distance')} error={message(errors.distanceKm?.message)}>
          {(ctl) => (
            <Input
              {...ctl}
              inputMode="decimal"
              className={field}
              {...register('distanceKm', { onChange: () => setValue('preset', 'custom') })}
            />
          )}
        </FormField>
        <FormField label={t('form.fields.elevation')} error={message(errors.elevationGain?.message)}>
          {(ctl) => <Input {...ctl} inputMode="numeric" className={field} {...register('elevationGain')} />}
        </FormField>
        <FormField label={t('form.fields.surface')} error={message(errors.surface?.message)}>
          {(ctl) => (
            <NativeSelect {...ctl} className={field} {...register('surface')}>
              <NativeSelectOption value="">{t('form.fields.surfaceNone')}</NativeSelectOption>
              {SURFACES.map((surface) => (
                <NativeSelectOption key={surface} value={surface}>
                  {surface}
                </NativeSelectOption>
              ))}
            </NativeSelect>
          )}
        </FormField>
        {type === 'Race' ? (
          <FormField label={t('form.fields.eventName')} error={message(errors.eventName?.message)}>
            {(ctl) => <Input {...ctl} maxLength={150} className={field} {...register('eventName')} />}
          </FormField>
        ) : null}
        {goal ? (
          <FormField label={t('form.fields.status')} error={message(errors.status?.message)}>
            {(ctl) => (
              <NativeSelect {...ctl} className={field} {...register('status')}>
                {GOAL_STATUSES.map((status) => (
                  <NativeSelectOption key={status} value={status}>
                    {t(`status.${status}`)}
                  </NativeSelectOption>
                ))}
              </NativeSelect>
            )}
          </FormField>
        ) : null}
      </div>

      {type === 'Distance' ? null : (
        <FieldSet className="gap-3">
          <FormField
            label={t(type === 'Performance' ? 'form.fields.targetTime' : 'form.fields.targetTimeOptional')}
            error={message(errors.targetTime?.message)}
          >
            {(ctl) => (
              <Input
                {...ctl}
                inputMode="numeric"
                placeholder={t('form.fields.targetTimePlaceholder')}
                className={field}
                {...register('targetTime')}
              />
            )}
          </FormField>
          <RealisticTargetHint
            input={estimateInput}
            onUse={(seconds) =>
              setValue('targetTime', formatDuration(seconds), { shouldValidate: true, shouldDirty: true })
            }
          />
        </FieldSet>
      )}

      {type === 'Race' ? (
        <RaceRunSelect
          targetDate={values.targetDate ?? ''}
          value={values.raceRunId ?? ''}
          error={message(errors.raceRunId?.message)}
          onChange={(runId, date) => {
            setValue('raceRunId', runId, { shouldDirty: true })
            if (date) setValue('targetDate', date, { shouldDirty: true, shouldValidate: true })
          }}
        />
      ) : null}

      <FormField label={t('form.fields.notes')} error={message(errors.notes?.message)}>
        {(ctl) => <Textarea {...ctl} rows={3} maxLength={1000} {...register('notes')} />}
      </FormField>

      {conflict ? (
        <div
          role="alert"
          className="flex flex-col items-start gap-2 rounded-md border border-destructive/50 p-3 text-sm"
        >
          <p>{t('form.problem.concurrency')}</p>
          <Button
            type="button"
            variant="outline"
            className="h-11 sm:h-9"
            onClick={() => {
              onReload?.()
              if (goal) reset(toGoalFormValues(goal))
              setConflict(false)
            }}
          >
            {t('form.reload')}
          </Button>
        </div>
      ) : null}
      {formError ? (
        <p role="alert" className="text-sm text-destructive">
          {formError}
        </p>
      ) : null}

      <div className="flex flex-wrap gap-2">
        <Button type="submit" className="h-11 sm:h-9" disabled={isSubmitting}>
          {t(isSubmitting ? 'form.saving' : 'form.save')}
        </Button>
        <Button asChild type="button" variant="outline" className="h-11 sm:h-9">
          <Link to={goal ? `/goals/${goal.id}` : '/goals'}>{t('form.cancel')}</Link>
        </Button>
      </div>
    </form>
  )
}
