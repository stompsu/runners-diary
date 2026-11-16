import { useTranslation } from 'react-i18next'
import { PRESET_IDS, type PresetId } from './goalSchema'

/** Preset chips (5K ... Custom) as a labelled radio group: arrow keys move, one tab stop, visible focus. */
export function GoalPresetPicker({ value, onChange }: { value: PresetId; onChange: (preset: PresetId) => void }) {
  const { t } = useTranslation('goals')
  return (
    <div role="radiogroup" aria-label={t('presets.label')} className="flex flex-wrap gap-2">
      {PRESET_IDS.map((id) => (
        <label
          key={id}
          className="flex min-h-11 cursor-pointer items-center gap-2 rounded-md border px-3 text-sm has-checked:border-primary has-checked:bg-primary/5 has-focus-visible:ring-[3px] has-focus-visible:ring-ring/50 sm:min-h-9"
        >
          <input
            type="radio"
            name="preset"
            value={id}
            checked={value === id}
            onChange={() => onChange(id)}
            className="size-4"
          />
          {t(`presets.${id}`)}
        </label>
      ))}
    </div>
  )
}
