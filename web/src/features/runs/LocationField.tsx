import { lazy, Suspense, useEffect, useRef, useState } from 'react'
import { MapPin, X } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { useClientConfig } from '@/auth/useClientConfig'
import { Button } from '@/components/ui/button'
import { FieldLegend, FieldSet } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { FormField } from './FormField'
import { formatCoordinates } from './locationCoordinates'
import { PlaceSearch } from './PlaceSearch'
import type { LocationFormValue } from './runFormTypes'

// The picker (and with it the map view) loads on first use, not with the run form.
const LocationPickerDialog = lazy(() =>
  import('./LocationPickerDialog').then((m) => ({ default: m.LocationPickerDialog })),
)

export interface LocationFieldProps {
  value: LocationFormValue | null
  onChange: (next: LocationFormValue | null) => void
  nameError?: string
}

/**
 * The run's location in the form (ADR 0023 §4): coordinates come only from the map or a search (exact numbers, never
 * typed into a text box except in the picker's keyboard alternative); the name is free text.
 */
export function LocationField({ value, onChange, nameError }: LocationFieldProps) {
  const { t } = useTranslation('runs')
  const maps = useClientConfig().data?.maps
  const [picking, setPicking] = useState(false)
  const pickButton = useRef<HTMLButtonElement>(null)
  const nameInput = useRef<HTMLInputElement>(null)
  const focusName = useRef(false)

  useEffect(() => {
    if (focusName.current && value) {
      focusName.current = false
      nameInput.current?.focus()
    }
  }, [value])

  const action = 'h-11 sm:h-9'
  const pin = value ? { latitude: value.latitude, longitude: value.longitude } : null

  return (
    <FieldSet className="gap-4">
      <FieldLegend>{t('location.legend')}</FieldLegend>
      {value ? (
        <>
          <p className="text-sm" data-testid="location-coordinates">
            <span className="text-muted-foreground">{t('location.coordinates')}: </span>
            <span className="tabular-nums">{formatCoordinates(value.latitude, value.longitude)}</span>
          </p>
          <FormField label={t('location.name')} hint={t('location.nameHint')} error={nameError}>
            {(controlProps) => (
              <Input
                {...controlProps}
                ref={nameInput}
                value={value.name}
                maxLength={200}
                className="h-11 sm:h-9"
                onChange={(event) => onChange({ ...value, name: event.target.value })}
              />
            )}
          </FormField>
        </>
      ) : (
        <p className="text-sm text-muted-foreground">{t('location.none')}</p>
      )}
      <div className="flex flex-wrap gap-2">
        <Button ref={pickButton} type="button" variant="outline" className={action} onClick={() => setPicking(true)}>
          <MapPin aria-hidden="true" />
          {t(value ? 'location.change' : 'location.pick')}
        </Button>
        {value ? (
          <Button
            type="button"
            variant="outline"
            className={action}
            onClick={() => {
              onChange(null)
              pickButton.current?.focus()
            }}
          >
            <X aria-hidden="true" />
            {t('location.clear')}
          </Button>
        ) : null}
      </div>
      {maps?.geocodingEnabled ? (
        <PlaceSearch
          near={pin}
          onSelect={(place) => {
            focusName.current = true
            onChange({ latitude: place.latitude, longitude: place.longitude, name: place.name.slice(0, 200) })
          }}
        />
      ) : null}
      {picking ? (
        <Suspense fallback={null}>
          <LocationPickerDialog
            initial={pin}
            maps={maps}
            onClose={() => setPicking(false)}
            onConfirm={(position) => {
              onChange({ ...position, name: value?.name ?? '' })
              setPicking(false)
            }}
            returnFocusTo={() => pickButton.current}
          />
        </Suspense>
      ) : null}
    </FieldSet>
  )
}
