import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Button } from '@/components/ui/button'
import { NativeSelect, NativeSelectOption } from '@/components/ui/native-select'
import { FormField } from './FormField'

/** IANA ids in the Area/Location form the API accepts (api-contract.md); empty when the runtime cannot list zones. */
function listZones(): string[] {
  try {
    return Intl.supportedValuesOf('timeZone').filter((zone) => zone.includes('/'))
  } catch {
    return []
  }
}

/**
 * The run's zone is a quiet line until the user wants another one (travel): "Time zone: Europe/Warsaw  [Change]".
 * `value` is '' while unchanged (POST then uses the profile zone, PUT keeps the run's zone); `baseZone` is what that means.
 */
export function TimeZoneField({
  baseZone,
  value,
  onChange,
  error,
}: {
  baseZone: string
  value: string
  onChange: (zone: string) => void
  error?: string
}) {
  const { t } = useTranslation('runs')
  const [editing, setEditing] = useState(value !== '')
  const zones = useMemo(() => {
    const all = new Set(listZones())
    all.add(baseZone)
    if (value) all.add(value)
    return [...all].sort((a, b) => a.localeCompare(b))
  }, [baseZone, value])
  const shown = value || baseZone

  if (!editing) {
    return (
      <div className="flex flex-wrap items-center gap-2 text-sm">
        <span>
          {t('fields.timeZone')}: <span data-testid="zone-current">{baseZone}</span>
        </span>
        <Button type="button" variant="link" className="h-11 px-1 sm:h-9" onClick={() => setEditing(true)}>
          {t('actions.changeZone')}
        </Button>
      </div>
    )
  }

  return (
    <FormField label={t('fields.timeZone')} error={error}>
      {(control) => (
        <NativeSelect
          {...control}
          value={shown}
          onChange={(event) => onChange(event.target.value === baseZone ? '' : event.target.value)}
          className="h-11 sm:h-9"
        >
          {zones.map((zone) => (
            <NativeSelectOption key={zone} value={zone}>
              {zone}
            </NativeSelectOption>
          ))}
        </NativeSelect>
      )}
    </FormField>
  )
}
