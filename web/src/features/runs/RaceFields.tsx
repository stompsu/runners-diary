import type { FieldErrors, UseFormRegister } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import { Input } from '@/components/ui/input'
import { NativeSelect, NativeSelectOption } from '@/components/ui/native-select'
import { FormField } from './FormField'
import { RACE_CATEGORIES, type RunFormValues } from './runFormTypes'

/** The race section: shown only for race run types; result and places only for completed runs (ADR 0018 §2). */
export function RaceFields({
  register,
  errors,
  completed,
  message,
}: {
  register: UseFormRegister<RunFormValues>
  errors: FieldErrors<RunFormValues>
  completed: boolean
  message: (text: string | undefined) => string | undefined
}) {
  const { t } = useTranslation('runs')
  const race = errors.race
  const input = 'h-11 sm:h-9'

  return (
    <fieldset className="grid gap-4 rounded-lg border p-4 sm:grid-cols-2">
      <legend className="px-2 text-sm font-semibold">{t('race.legend')}</legend>
      <FormField label={t('race.name')} error={message(race?.name?.message)} className="sm:col-span-2">
        {(control) => <Input {...control} className={input} maxLength={150} {...register('race.name')} />}
      </FormField>
      <FormField label={t('race.distanceCategory')} error={message(race?.distanceCategory?.message)}>
        {(control) => (
          <NativeSelect {...control} className={input} {...register('race.distanceCategory')}>
            {RACE_CATEGORIES.map((category) => (
              <NativeSelectOption key={category} value={category}>
                {t(`race.categories.${category}`)}
              </NativeSelectOption>
            ))}
          </NativeSelect>
        )}
      </FormField>
      <FormField
        label={t('race.officialDistance')}
        hint={t('race.officialDistanceHint')}
        error={message(race?.officialDistanceKm?.message)}
      >
        {(control) => (
          <Input {...control} inputMode="decimal" className={input} {...register('race.officialDistanceKm')} />
        )}
      </FormField>
      <FormField label={t('race.bib')} error={message(race?.bibNumber?.message)}>
        {(control) => <Input {...control} className={input} maxLength={20} {...register('race.bibNumber')} />}
      </FormField>
      <FormField label={t('race.categoryName')} error={message(race?.categoryName?.message)}>
        {(control) => <Input {...control} className={input} maxLength={50} {...register('race.categoryName')} />}
      </FormField>
      <FormField
        label={t('race.targetTime')}
        hint={t('fields.durationHint')}
        error={message(race?.targetTime?.message)}
      >
        {(control) => (
          <Input
            {...control}
            inputMode="numeric"
            className={input}
            placeholder={t('fields.durationPlaceholder')}
            {...register('race.targetTime')}
          />
        )}
      </FormField>
      {completed ? (
        <>
          <FormField
            label={t('race.resultTime')}
            hint={t('fields.durationHint')}
            error={message(race?.resultTime?.message)}
          >
            {(control) => (
              <Input
                {...control}
                inputMode="numeric"
                className={input}
                placeholder={t('fields.durationPlaceholder')}
                {...register('race.resultTime')}
              />
            )}
          </FormField>
          <FormField label={t('race.overallPlace')} error={message(race?.overallPlace?.message)}>
            {(control) => (
              <Input {...control} inputMode="numeric" className={input} {...register('race.overallPlace')} />
            )}
          </FormField>
          <FormField label={t('race.categoryPlace')} error={message(race?.categoryPlace?.message)}>
            {(control) => (
              <Input {...control} inputMode="numeric" className={input} {...register('race.categoryPlace')} />
            )}
          </FormField>
        </>
      ) : null}
      <FormField label={t('race.resultsUrl')} error={message(race?.resultsUrl?.message)} className="sm:col-span-2">
        {(control) => (
          <Input {...control} type="url" inputMode="url" className={input} {...register('race.resultsUrl')} />
        )}
      </FormField>
    </fieldset>
  )
}
