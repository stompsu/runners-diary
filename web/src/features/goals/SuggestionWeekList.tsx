import { useTranslation } from 'react-i18next'
import { dayLabel } from '@/lib/dates/plainDate'
import { groupByIsoWeek } from './goalFormat'
import { SuggestionCard } from './SuggestionCard'
import type { TrainingSuggestion } from './goalTypes'
import '@/i18n/lazyBundles'

/** The suggestions of a goal grouped by ISO week (week label and the phase of its sessions). */
export function SuggestionWeekList({ items }: { items: readonly TrainingSuggestion[] }) {
  const { t, i18n } = useTranslation('goals')
  const weeks = groupByIsoWeek(items)
  return (
    <div className="flex flex-col gap-5">
      {weeks.map((week) => {
        const id = `week-${week.start}`
        return (
          <section key={week.start} aria-labelledby={id} data-week={week.start}>
            <h3 id={id} className="mb-2 text-sm font-semibold">
              {t('plan.weekLabel', { week: week.isoWeek, start: dayLabel(week.start, i18n.language) })}
            </h3>
            <ul className="flex flex-col gap-3">
              {week.items.map((item) => (
                <SuggestionCard key={item.id} suggestion={item} showDate />
              ))}
            </ul>
          </section>
        )
      })}
    </div>
  )
}
