import { ChevronDown, ChevronLeft, ChevronRight } from 'lucide-react'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Button } from '@/components/ui/button'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import { isInRange, MAX_YEAR, MIN_YEAR, type DiaryMonth } from '@/features/diary/diaryMonth'
import { monthLabel, monthName } from '@/lib/dates/plainDate'

/** Popover with a year stepper and a 12-month button grid; picking a month navigates to it. */
export function MonthYearPicker({ month, onSelect }: { month: DiaryMonth; onSelect: (month: DiaryMonth) => void }) {
  const { t, i18n } = useTranslation('calendar')
  const locale = i18n.language
  const [open, setOpen] = useState(false)
  const [year, setYear] = useState(month.year)

  return (
    <Popover
      open={open}
      onOpenChange={(next) => {
        if (next) setYear(month.year)
        setOpen(next)
      }}
    >
      <PopoverTrigger asChild>
        <Button
          type="button"
          variant="outline"
          className="h-11 sm:h-9"
          aria-label={t('picker.trigger', { month: monthLabel(month.year, month.month, locale) })}
        >
          {monthLabel(month.year, month.month, locale)}
          <ChevronDown aria-hidden="true" />
        </Button>
      </PopoverTrigger>
      <PopoverContent className="w-72" aria-label={t('picker.title')}>
        <div className="mb-3 flex items-center justify-between">
          <Button
            type="button"
            variant="ghost"
            size="icon"
            className="size-11 sm:size-9"
            aria-label={t('picker.previousYear')}
            disabled={year <= MIN_YEAR}
            onClick={() => setYear(year - 1)}
          >
            <ChevronLeft aria-hidden="true" />
          </Button>
          <output className="font-medium">{t('picker.year', { year })}</output>
          <Button
            type="button"
            variant="ghost"
            size="icon"
            className="size-11 sm:size-9"
            aria-label={t('picker.nextYear')}
            disabled={year >= MAX_YEAR}
            onClick={() => setYear(year + 1)}
          >
            <ChevronRight aria-hidden="true" />
          </Button>
        </div>
        <fieldset className="grid grid-cols-3 gap-2">
          <legend className="sr-only">{t('picker.months')}</legend>
          {Array.from({ length: 12 }, (_, i) => i + 1).map((m) => {
            const current = year === month.year && m === month.month
            return (
              <Button
                key={m}
                type="button"
                variant={current ? 'default' : 'outline'}
                className="h-11 sm:h-9"
                aria-label={monthLabel(year, m, locale)}
                aria-current={current ? 'date' : undefined}
                disabled={!isInRange({ year, month: m })}
                onClick={() => {
                  setOpen(false)
                  onSelect({ year, month: m })
                }}
              >
                {monthName(m, locale, 'short')}
              </Button>
            )
          })}
        </fieldset>
      </PopoverContent>
    </Popover>
  )
}
