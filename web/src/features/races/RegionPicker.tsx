import { useTranslation } from 'react-i18next'
import { Button } from '@/components/ui/button'
import { FieldError, FieldLegend, FieldSet } from '@/components/ui/field'
import { CheckRow } from './CheckRow'
import { POLISH_REGION_CODES, type PolishRegionCode } from './polishRegions'
import '@/i18n/lazyBundles'

/** The 16 voivodeships: English name with the Polish one in brackets, plus Select all / Clear. */
export function RegionPicker({
  value,
  onChange,
  error,
}: {
  value: readonly string[]
  onChange: (next: PolishRegionCode[]) => void
  error?: string
}) {
  const { t } = useTranslation('races')
  const action = 'h-11 sm:h-9'
  return (
    <FieldSet className="gap-3" data-invalid-group={error ? 'true' : undefined}>
      <FieldLegend variant="label">{t('interests.where.regions')}</FieldLegend>
      <div className="flex flex-wrap items-center gap-2">
        <Button type="button" variant="outline" className={action} onClick={() => onChange([...POLISH_REGION_CODES])}>
          {t('interests.where.selectAll')}
        </Button>
        <Button type="button" variant="outline" className={action} onClick={() => onChange([])}>
          {t('interests.where.clear')}
        </Button>
        <output className="text-sm text-muted-foreground">
          {t('interests.where.selectedCount', { count: value.length, total: POLISH_REGION_CODES.length })}
        </output>
      </div>
      <div className="grid gap-2 sm:grid-cols-2 lg:grid-cols-3">
        {POLISH_REGION_CODES.map((code) => (
          <CheckRow
            key={code}
            label={t('interests.where.regionLabel', { en: t(`regions.${code}.en`), pl: t(`regions.${code}.pl`) })}
            checked={value.includes(code)}
            onChange={(checked) =>
              onChange(POLISH_REGION_CODES.filter((c) => (c === code ? checked : value.includes(c))))
            }
          />
        ))}
      </div>
      {error ? <FieldError>{error}</FieldError> : null}
    </FieldSet>
  )
}
