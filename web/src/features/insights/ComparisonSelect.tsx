import { useId } from 'react'
import { Label } from '@/components/ui/label'
import { NativeSelect, NativeSelectOption } from '@/components/ui/native-select'
import { COMPARE_MODES } from './insightsRange'
import type { CompareMode } from './insightsTypes'
import { useInsightsT } from './useInsightsT'

export function ComparisonSelect({ value, onChange }: { value: CompareMode; onChange: (value: CompareMode) => void }) {
  const { t } = useInsightsT()
  const id = useId()

  return (
    <div className="flex flex-col gap-1 sm:flex-row sm:items-center sm:gap-2">
      <Label htmlFor={id}>{t('compare.label')}</Label>
      <div className="sm:w-56">
        <NativeSelect
          id={id}
          value={value}
          onChange={(event) => onChange(event.target.value as CompareMode)}
          className="h-11 sm:h-9"
        >
          {COMPARE_MODES.map((mode) => (
            <NativeSelectOption key={mode} value={mode}>
              {t(`compare.${mode}`)}
            </NativeSelectOption>
          ))}
        </NativeSelect>
      </div>
    </div>
  )
}
