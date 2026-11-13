import { useTranslation } from 'react-i18next'
import { FieldError } from '@/components/ui/field'
import { CheckRow } from './CheckRow'
import { RACE_KINDS, type RaceKind } from './raceTypes'
import '@/i18n/lazyBundles'

/** Kinds of race to suggest (at least one). Ultra explains that it also covers anything longer than a marathon. */
export function KindPicker({
  value,
  onChange,
  error,
}: {
  value: readonly RaceKind[]
  onChange: (next: RaceKind[]) => void
  error?: string
}) {
  const { t } = useTranslation('races')
  return (
    <div className="flex flex-col gap-2" data-invalid-group={error ? 'true' : undefined}>
      <div className="grid gap-2 sm:grid-cols-2 lg:grid-cols-3">
        {RACE_KINDS.map((kind) => (
          <CheckRow
            key={kind}
            label={t(`kinds.${kind}`)}
            hint={kind === 'Ultra' ? t('interests.what.ultraHint') : undefined}
            checked={value.includes(kind)}
            onChange={(checked) => onChange(RACE_KINDS.filter((k) => (k === kind ? checked : value.includes(k))))}
          />
        ))}
      </div>
      <p className="text-sm text-muted-foreground">{t('interests.what.kindsHint')}</p>
      {error ? <FieldError>{error}</FieldError> : null}
    </div>
  )
}
