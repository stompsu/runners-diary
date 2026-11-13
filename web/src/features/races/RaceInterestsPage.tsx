import { zodResolver } from '@hookform/resolvers/zod'
import { useEffect, useMemo, useRef, useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { useClientConfig } from '@/auth/useClientConfig'
import { MapPrivacyNote } from '@/components/map/MapPrivacyNote'
import { MapView } from '@/components/map/MapView'
import { Button } from '@/components/ui/button'
import { FieldError, FieldLegend, FieldSet } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { NativeSelect, NativeSelectOption } from '@/components/ui/native-select'
import { Skeleton } from '@/components/ui/skeleton'
import { Switch } from '@/components/ui/switch'
import { QueryError } from '@/features/errors/QueryError'
import { FormField } from '@/features/runs/FormField'
import { LocationField } from '@/features/runs/LocationField'
import { safeHttpUrl } from '@/features/runs/safeUrl'
import { writeProblemMessage } from '@/features/runs/runProblemMessage'
import type { LocationFormValue } from '@/features/runs/runFormTypes'
import { parseProblem } from '@/lib/api/problem'
import { kmToMeters, parseKm } from '@/lib/format/units'
import { DistanceRangeField } from './DistanceRangeField'
import { KindPicker } from './KindPicker'
import { RadiusField } from './RadiusField'
import { RegionPicker } from './RegionPicker'
import { mapRaceInterestsErrors, toFormValues, toWriteRequest } from './raceInterestsMapping'
import { HORIZON_PRESETS, RADIUS_KM, raceInterestsSchema, type RaceInterestsFormValues } from './raceInterestsSchema'
import { useRaceInterests, useUpdateRaceInterests } from './raceQueries'
import { sourceName } from './raceFormat'
import type { RaceInterestsDto } from './raceTypes'
import '@/i18n/lazyBundles'

/** Settings -> Race interests (`/settings/races`): which upcoming races to propose (ADR 0037 §1). */
export function RaceInterestsPage() {
  const { t } = useTranslation('races')
  const { t: tCommon } = useTranslation()
  const config = useClientConfig()
  const enabled = config.data?.races.enabled === true
  const interests = useRaceInterests(enabled)

  return (
    <div className="flex max-w-3xl flex-col gap-6">
      <header>
        <p className="text-sm text-muted-foreground">{tCommon('nav.settings')}</p>
        <h1 className="text-2xl font-semibold">{t('interests.title')}</h1>
        <p className="mt-1 text-muted-foreground">{t('interests.intro')}</p>
      </header>
      {config.isError ? (
        <QueryError error={config.error} onRetry={() => void config.refetch()} />
      ) : !config.data ? (
        <Skeleton className="h-64 w-full" />
      ) : !enabled ? (
        <output className="block rounded-md border p-4 text-sm">{t('interests.unavailable')}</output>
      ) : interests.isError ? (
        <QueryError error={interests.error} onRetry={() => void interests.refetch()} />
      ) : interests.data ? (
        <RaceInterestsForm saved={interests.data} />
      ) : (
        <Skeleton className="h-64 w-full" />
      )}
    </div>
  )
}

interface FormProblem {
  message: string
  details: string[]
}

function RaceInterestsForm({ saved }: { saved: RaceInterestsDto }) {
  const { t } = useTranslation('races')
  const { t: tErrors } = useTranslation('errors')
  const config = useClientConfig()
  const update = useUpdateRaceInterests()
  const [problem, setProblem] = useState<FormProblem | null>(null)
  const summaryRef = useRef<HTMLDivElement>(null)
  const saving = useRef(false)
  const formRef = useRef<HTMLFormElement>(null)

  const form = useForm<RaceInterestsFormValues>({
    resolver: zodResolver(raceInterestsSchema),
    defaultValues: toFormValues(saved),
  })
  const { control, register, handleSubmit, setValue, setError, reset, formState } = form
  const { errors, isSubmitted, isDirty, isSubmitting } = formState
  const kinds = useWatch({ control, name: 'kinds' })
  const regionCodes = useWatch({ control, name: 'regionCodes' })
  const center = useWatch({ control, name: 'center' })
  const radiusKm = useWatch({ control, name: 'radiusKm' })
  const minDistanceKm = useWatch({ control, name: 'minDistanceKm' })
  const maxDistanceKm = useWatch({ control, name: 'maxDistanceKm' })
  const horizonDays = useWatch({ control, name: 'horizonDays' })
  const showInCalendar = useWatch({ control, name: 'showInCalendar' })

  /** ADR 0018: after a failed save the first invalid control (in page order) takes the focus; pickers focus their first box. */
  function focusFirstInvalid() {
    setTimeout(() => {
      const target = formRef.current?.querySelector<HTMLElement>('[aria-invalid="true"], [data-invalid-group="true"]')
      const focusable = target?.matches('input, select, textarea, button')
        ? target
        : target?.querySelector<HTMLElement>('button, input')
      focusable?.focus()
    }, 0)
  }

  useEffect(() => {
    if (problem) summaryRef.current?.focus()
  }, [problem])

  /** Our zod messages are `validation.*` keys; a server message is fixed English text shown as it is (ADR 0018 §3). */
  const message = (text: string | undefined): string | undefined =>
    text?.startsWith('validation.') ? t(`interests.${text}` as 'interests.validation.kindsRequired') : text

  const touch = { shouldDirty: true, shouldValidate: isSubmitted }

  function changePlace(next: LocationFormValue | null) {
    setValue('center', next, touch)
    // A place needs a radius (the server takes both or neither): suggest one, and drop it with the place.
    if (!next) setValue('radiusKm', '', touch)
    else if (radiusKm.trim() === '') setValue('radiusKm', String(RADIUS_KM.default), touch)
  }

  const radiusMeters = (() => {
    const km = parseKm(radiusKm)
    if (km === null) return null
    const metres = kmToMeters(km)
    return metres >= RADIUS_KM.min * 1000 && metres <= RADIUS_KM.max * 1000 ? metres : null
  })()
  const latitude = center?.latitude
  const longitude = center?.longitude
  const area = useMemo(
    () =>
      latitude !== undefined && longitude !== undefined && radiusMeters !== null
        ? { center: { latitude, longitude }, radiusMeters }
        : null,
    [latitude, longitude, radiusMeters],
  )

  function showProblem(caught: unknown) {
    const apiProblem = parseProblem(caught)
    if (apiProblem.kind !== 'validation') {
      setProblem({ message: writeProblemMessage(apiProblem, tErrors), details: [] })
      return
    }
    const mapped = mapRaceInterestsErrors(apiProblem.fieldErrors)
    const names = Object.keys(mapped.fields)
    names.forEach((name) => setError(name as 'kinds', { type: 'server', message: mapped.fields[name] }))
    if (mapped.summary.length === 0) focusFirstInvalid()
    // Everything mapped onto fields: the fields speak for themselves; anything else goes to a summary with the focus.
    setProblem(
      mapped.summary.length > 0 || names.length === 0
        ? { message: tErrors('problem.validation'), details: mapped.summary }
        : null,
    )
  }

  async function submit(values: RaceInterestsFormValues) {
    if (saving.current) return
    saving.current = true
    setProblem(null)
    try {
      const response = await update.mutateAsync({ body: toWriteRequest(values) })
      reset(toFormValues(response))
      toast.success(t('interests.saved'))
    } catch (caught) {
      showProblem(caught)
    } finally {
      saving.current = false
    }
  }

  const centerError = message(
    errors.center?.message ?? errors.center?.latitude?.message ?? errors.center?.longitude?.message,
  )
  const field = 'h-11 sm:h-9'
  const horizons = [...new Set<number>([...HORIZON_PRESETS, horizonDays])].sort((a, b) => a - b)
  const maps = config.data?.maps
  const sources = config.data?.races.sources ?? []

  return (
    <form
      ref={formRef}
      noValidate
      aria-label={t('interests.saveForm')}
      onSubmit={(event) => void handleSubmit(submit, focusFirstInvalid)(event)}
      className="flex flex-col gap-8"
    >
      {saved.configured ? null : (
        <output className="block rounded-md border border-primary/40 bg-primary/5 p-3 text-sm">
          {t('interests.notConfigured')}
        </output>
      )}
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

      <FieldSet className="gap-4">
        <FieldLegend>{t('interests.what.legend')}</FieldLegend>
        <KindPicker
          value={kinds}
          onChange={(next) => setValue('kinds', next, touch)}
          error={message(errors.kinds?.message)}
        />
      </FieldSet>

      <FieldSet className="gap-5">
        <FieldLegend>{t('interests.where.legend')}</FieldLegend>
        <p className="text-sm text-muted-foreground">{t('interests.where.hint')}</p>
        <RegionPicker
          value={regionCodes}
          onChange={(next) => setValue('regionCodes', next, touch)}
          error={message(errors.regionCodes?.message)}
        />
        <FieldSet className="gap-4" data-invalid-group={centerError ? 'true' : undefined}>
          <FieldLegend variant="label">{t('interests.where.nearPlace')}</FieldLegend>
          <p className="text-sm text-muted-foreground">{t('interests.where.placeHint')}</p>
          <LocationField value={center} onChange={changePlace} nameError={message(errors.center?.name?.message)} />
          {centerError ? <FieldError>{centerError}</FieldError> : null}
          <RadiusField
            value={radiusKm}
            onChange={(next) => setValue('radiusKm', next, touch)}
            hasPlace={center !== null}
            error={message(errors.radiusKm?.message)}
          />
          {area ? (
            <>
              <MapView
                ariaLabel={t('interests.where.mapLabel')}
                area={area}
                provider={maps?.provider}
                styleUrl={maps?.mapLibreStyleUrl}
                azureMapsDomain={maps?.azureMapsDomain}
                className="h-[max(220px,30vh)] w-full"
              />
              <MapPrivacyNote maps={maps} />
            </>
          ) : null}
        </FieldSet>
      </FieldSet>

      <FieldSet className="gap-4">
        <FieldLegend>{t('interests.distance.legend')}</FieldLegend>
        <p className="text-sm text-muted-foreground">{t('interests.distance.hint')}</p>
        <DistanceRangeField
          value={{ min: minDistanceKm, max: maxDistanceKm }}
          onChange={(next) => {
            setValue('minDistanceKm', next.min, touch)
            setValue('maxDistanceKm', next.max, touch)
          }}
          minError={message(errors.minDistanceKm?.message)}
          maxError={message(errors.maxDistanceKm?.message)}
        />
      </FieldSet>

      <FieldSet className="gap-4">
        <FieldLegend>{t('interests.when.legend')}</FieldLegend>
        <FormField label={t('interests.when.horizon')} error={message(errors.horizonDays?.message)}>
          {(controlProps) => (
            <NativeSelect
              {...controlProps}
              className={field}
              value={horizonDays}
              onChange={(event) => setValue('horizonDays', Number(event.target.value), touch)}
            >
              {horizons.map((days) => (
                <NativeSelectOption key={days} value={days}>
                  {t('interests.when.horizonOption', { count: days })}
                </NativeSelectOption>
              ))}
            </NativeSelect>
          )}
        </FormField>
      </FieldSet>

      <FieldSet className="gap-4">
        <FieldLegend>{t('interests.keywords.legend')}</FieldLegend>
        <FormField
          label={t('interests.keywords.label')}
          hint={t('interests.keywords.hint')}
          error={message(errors.keywords?.message)}
        >
          {(controlProps) => <Input {...controlProps} className={field} autoComplete="off" {...register('keywords')} />}
        </FormField>
      </FieldSet>

      <FieldSet className="gap-4">
        <FieldLegend>{t('interests.show.legend')}</FieldLegend>
        <div className="flex items-start gap-3">
          <Switch
            id="race-show-in-calendar"
            checked={showInCalendar}
            aria-describedby="race-show-in-calendar-hint"
            onCheckedChange={(next) => setValue('showInCalendar', next, touch)}
          />
          <div className="flex flex-col gap-1">
            <Label htmlFor="race-show-in-calendar">{t('interests.show.label')}</Label>
            <p id="race-show-in-calendar-hint" className="text-sm text-muted-foreground">
              {t('interests.show.hint')}
            </p>
          </div>
        </div>
      </FieldSet>

      <FieldSet className="gap-3">
        <FieldLegend>{t('interests.sources.legend')}</FieldLegend>
        {sources.length > 0 ? (
          <ul className="list-disc pl-5 text-sm">
            {sources.map((source) => {
              const href = safeHttpUrl(source.url)
              const name = sourceName(sources, source.id)
              return (
                <li key={source.id}>
                  {href ? (
                    <a
                      href={href}
                      target="_blank"
                      rel="noopener noreferrer"
                      referrerPolicy="no-referrer"
                      className="underline underline-offset-2"
                    >
                      {name}
                      <span className="sr-only"> {t('interests.sources.opensNewTab')}</span>
                    </a>
                  ) : (
                    name
                  )}
                </li>
              )
            })}
          </ul>
        ) : null}
        <p className="text-sm text-muted-foreground">{t('attribution.disclaimer')}</p>
      </FieldSet>

      <div className="flex flex-wrap gap-2">
        <Button type="submit" className={field} disabled={isSubmitting}>
          {t(isSubmitting ? 'interests.saving' : 'interests.save')}
        </Button>
        <Button
          type="button"
          variant="outline"
          className={field}
          disabled={!isDirty || isSubmitting}
          onClick={() => {
            reset(toFormValues(saved))
            setProblem(null)
          }}
        >
          {t('interests.discard')}
        </Button>
      </div>
    </form>
  )
}
