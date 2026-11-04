import { useId, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ResponsiveDialog } from '@/components/ResponsiveDialog'
import { MapPrivacyNote } from '@/components/map/MapPrivacyNote'
import { MapView } from '@/components/map/MapView'
import type { LatLon, MapsConfig } from '@/components/map/types'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import {
  coordinateText,
  parseCoordinate,
  readLastPick,
  saveLastPick,
  validLatitude,
  validLongitude,
} from './locationCoordinates'
import '@/i18n/lazyBundles'

export interface LocationPickerDialogProps {
  /** The current pin, if the run has a location. */
  initial: LatLon | null
  maps?: MapsConfig
  onConfirm: (position: LatLon) => void
  onClose: () => void
  returnFocusTo?: () => HTMLElement | null | undefined
}

/**
 * Pick a place on the map (click / tap) or, as the keyboard and screen-reader alternative, by typing latitude and
 * longitude. The pin and the two fields always show the same position. The view opens at the current pin, else at the
 * last place picked in this tab, else at the application default. It is used inside the run form, so there is no
 * `<form>` here (a submit would bubble through the portal to the run form); Enter in a field confirms instead.
 */
export function LocationPickerDialog({ initial, maps, onConfirm, onClose, returnFocusTo }: LocationPickerDialogProps) {
  const { t } = useTranslation('maps')
  const id = useId()
  const [latText, setLatText] = useState(initial ? coordinateText(initial.latitude) : '')
  const [lonText, setLonText] = useState(initial ? coordinateText(initial.longitude) : '')
  const [center] = useState(() => initial ?? readLastPick() ?? undefined)

  const latitude = parseCoordinate(latText)
  const longitude = parseCoordinate(lonText)
  const latError = latText.trim() !== '' && !validLatitude(latitude) ? t('picker.latitudeError') : undefined
  const lonError = lonText.trim() !== '' && !validLongitude(longitude) ? t('picker.longitudeError') : undefined
  const position: LatLon | null = validLatitude(latitude) && validLongitude(longitude) ? { latitude, longitude } : null

  function confirm() {
    if (!position) return
    saveLastPick(position)
    onConfirm(position)
  }

  const onEnter = (event: React.KeyboardEvent) => {
    if (event.key !== 'Enter') return
    event.preventDefault()
    confirm()
  }

  return (
    <ResponsiveDialog
      title={t('picker.title')}
      description={t('picker.description')}
      onClose={onClose}
      size="lg"
      panel="location-picker"
      returnFocusTo={returnFocusTo}
      footer={
        <>
          <Button type="button" variant="outline" className="h-11 sm:h-9" onClick={onClose}>
            {t('picker.cancel')}
          </Button>
          <Button type="button" className="h-11 sm:h-9" disabled={!position} onClick={confirm}>
            {t('picker.confirm')}
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-4">
        <MapView
          ariaLabel={t('picker.mapLabel')}
          marker={position}
          center={center}
          fitTo="marker"
          provider={maps?.provider}
          styleUrl={maps?.mapLibreStyleUrl}
          azureMapsDomain={maps?.azureMapsDomain}
          onPick={(picked) => {
            setLatText(coordinateText(picked.latitude))
            setLonText(coordinateText(picked.longitude))
          }}
          className="h-[max(240px,40vh)] w-full"
        />
        <MapPrivacyNote maps={maps} />
        <div className="grid gap-4 sm:grid-cols-2">
          <div className="flex flex-col gap-2">
            <Label htmlFor={`${id}-lat`}>{t('picker.latitude')}</Label>
            <Input
              id={`${id}-lat`}
              inputMode="decimal"
              autoComplete="off"
              value={latText}
              aria-invalid={latError ? true : undefined}
              aria-describedby={latError ? `${id}-lat-error` : undefined}
              className="h-11 sm:h-9"
              onChange={(event) => setLatText(event.target.value)}
              onKeyDown={onEnter}
            />
            {latError ? (
              <p id={`${id}-lat-error`} className="text-sm text-destructive">
                {latError}
              </p>
            ) : null}
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor={`${id}-lon`}>{t('picker.longitude')}</Label>
            <Input
              id={`${id}-lon`}
              inputMode="decimal"
              autoComplete="off"
              value={lonText}
              aria-invalid={lonError ? true : undefined}
              aria-describedby={lonError ? `${id}-lon-error` : undefined}
              className="h-11 sm:h-9"
              onChange={(event) => setLonText(event.target.value)}
              onKeyDown={onEnter}
            />
            {lonError ? (
              <p id={`${id}-lon-error`} className="text-sm text-destructive">
                {lonError}
              </p>
            ) : null}
          </div>
        </div>
        <p className="text-sm text-muted-foreground">{t('picker.instructions')}</p>
        <output className="block text-sm font-medium">
          {position
            ? t('picker.selected', { latitude: position.latitude, longitude: position.longitude })
            : t('picker.none')}
        </output>
      </div>
    </ResponsiveDialog>
  )
}
