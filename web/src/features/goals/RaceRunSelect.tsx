import { useMemo } from 'react'
import { useTranslation } from 'react-i18next'
import { $api } from '@/api/client'
import { NativeSelect, NativeSelectOption } from '@/components/ui/native-select'
import { FormField, type ControlProps } from '@/features/runs/FormField'
import { addDays, dayLabel, isValid } from '@/lib/dates/plainDate'
import type { ReactNode } from 'react'

/**
 * Lists the caller's planned race runs around the target date (the list endpoint spans at most 92 days) and lets the
 * goal link one; choosing a run moves the target date to the run's date (the server requires them to match).
 */
export function RaceRunSelect({
  targetDate,
  value,
  error,
  onChange,
}: {
  targetDate: string
  value: string
  error?: string
  onChange: (runId: string, date: string | null) => void
}) {
  const { t, i18n } = useTranslation('goals')
  const centre = isValid(targetDate) ? targetDate : null
  const runs = $api.useQuery(
    'get',
    '/api/runs',
    { params: { query: { from: addDays(centre ?? '2026-01-01', -45), to: addDays(centre ?? '2026-01-01', 46) } } },
    { enabled: centre !== null },
  )
  const types = $api.useQuery('get', '/api/run-types', {}, { staleTime: Infinity })
  const options = useMemo(() => {
    const raceIds = new Set((types.data ?? []).filter((type) => type.category === 'Race').map((type) => type.id))
    return (runs.data?.items ?? []).filter((run) => run.status === 'Planned' && raceIds.has(run.runTypeId))
  }, [runs.data, types.data])

  const render = (control: ControlProps): ReactNode => (
    <NativeSelect
      {...control}
      className="h-11 sm:h-9"
      value={value}
      onChange={(event) => {
        const run = options.find((option) => option.id === event.target.value)
        onChange(event.target.value, run?.date ?? null)
      }}
    >
      <NativeSelectOption value="">{t('form.fields.raceRunNone')}</NativeSelectOption>
      {options.map((run) => (
        <NativeSelectOption key={run.id} value={run.id}>
          {t('form.raceRunLabel', { name: run.race?.name ?? run.title ?? '', date: dayLabel(run.date, i18n.language) })}
        </NativeSelectOption>
      ))}
      {value !== '' && !options.some((run) => run.id === value) ? (
        <NativeSelectOption value={value}>{t('form.fields.raceRun')}</NativeSelectOption>
      ) : null}
    </NativeSelect>
  )

  return (
    <FormField label={t('form.fields.raceRun')} hint={t('form.raceRunHint')} error={error}>
      {render}
    </FormField>
  )
}
