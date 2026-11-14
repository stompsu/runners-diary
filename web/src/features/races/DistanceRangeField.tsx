import { useTranslation } from 'react-i18next'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { FormField } from '@/features/runs/FormField'
import { DISTANCE_PRESETS } from './distancePresets'
import '@/i18n/lazyBundles'

export interface DistanceRange {
  min: string
  max: string
}

/** Min / max distance in km with one-click presets; a preset is pressed while both fields equal it. */
export function DistanceRangeField({
  value,
  onChange,
  minError,
  maxError,
}: {
  value: DistanceRange
  onChange: (next: DistanceRange) => void
  minError?: string
  maxError?: string
}) {
  const { t } = useTranslation('races')
  const field = 'h-11 sm:h-9'
  return (
    <div className="flex flex-col gap-4">
      <fieldset className="flex flex-wrap gap-2">
        <legend className="sr-only">{t('interests.distance.presets')}</legend>
        {DISTANCE_PRESETS.map((preset) => {
          const pressed = value.min.trim() === preset.min && value.max.trim() === preset.max
          return (
            <Button
              key={preset.key}
              type="button"
              variant={pressed ? 'default' : 'outline'}
              aria-pressed={pressed}
              className={field}
              onClick={() => onChange({ min: preset.min, max: preset.max })}
            >
              {t(`interests.distance.preset.${preset.key}`)}
            </Button>
          )
        })}
      </fieldset>
      <div className="grid gap-4 sm:grid-cols-2">
        <FormField label={t('interests.distance.min')} error={minError}>
          {(control) => (
            <Input
              {...control}
              inputMode="decimal"
              className={field}
              value={value.min}
              onChange={(event) => onChange({ min: event.target.value, max: value.max })}
            />
          )}
        </FormField>
        <FormField label={t('interests.distance.max')} error={maxError}>
          {(control) => (
            <Input
              {...control}
              inputMode="decimal"
              className={field}
              value={value.max}
              onChange={(event) => onChange({ min: value.min, max: event.target.value })}
            />
          )}
        </FormField>
      </div>
    </div>
  )
}
