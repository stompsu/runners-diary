import { useTranslation } from 'react-i18next'
import { Input } from '@/components/ui/input'
import { FormField } from '@/features/runs/FormField'
import { RADIUS_KM } from './raceInterestsSchema'
import '@/i18n/lazyBundles'

const clampedKm = (text: string): number => {
  const km = Number(text.trim().replace(',', '.'))
  if (!Number.isFinite(km) || text.trim() === '') return RADIUS_KM.default
  return Math.min(RADIUS_KM.max, Math.max(RADIUS_KM.min, km))
}

/** Radius around the chosen place: a number input (exact) and a range input (quick); disabled until a place is chosen. */
export function RadiusField({
  value,
  onChange,
  hasPlace,
  error,
}: {
  value: string
  onChange: (next: string) => void
  hasPlace: boolean
  error?: string
}) {
  const { t } = useTranslation('races')
  return (
    <FormField
      label={t('interests.where.radius')}
      hint={t(hasPlace ? 'interests.where.radiusHint' : 'interests.where.radiusNeedsPlace')}
      error={error}
    >
      {(control) => (
        <div className="flex items-center gap-3">
          <Input
            {...control}
            type="number"
            inputMode="decimal"
            min={RADIUS_KM.min}
            max={RADIUS_KM.max}
            step="any"
            disabled={!hasPlace}
            className="h-11 w-28 sm:h-9"
            value={value}
            onChange={(event) => onChange(event.target.value)}
          />
          <input
            type="range"
            aria-label={t('interests.where.radiusSlider')}
            min={RADIUS_KM.min}
            max={RADIUS_KM.max}
            step={5}
            disabled={!hasPlace}
            className="h-11 min-w-0 flex-1 accent-primary sm:h-9"
            value={clampedKm(value)}
            onChange={(event) => onChange(event.target.value)}
          />
        </div>
      )}
    </FormField>
  )
}
