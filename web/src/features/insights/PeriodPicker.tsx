import { ChevronLeft, ChevronRight } from 'lucide-react'
import { useId, useState, type FormEvent } from 'react'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import type { PlainDate } from '@/lib/dates/plainDate'
import {
  MAX_DATE,
  MIN_DATE,
  changeView,
  customRange,
  shiftRange,
  validateCustomRange,
  type CustomRangeError,
  type InsightsRange,
  type InsightsView,
} from './insightsRange'
import { useInsightsT } from './useInsightsT'

const PRESETS: readonly Exclude<InsightsView, 'custom'>[] = ['week', 'month', '12w', 'ytd']

/**
 * Presets (this week / this month / last 12 weeks / year to date), a custom range, and earlier/later arrows.
 * It only reports the next range; the page writes it to the URL.
 */
export function PeriodPicker({
  range,
  today,
  onChange,
}: {
  range: InsightsRange
  today: PlainDate
  onChange: (range: InsightsRange) => void
}) {
  const { t } = useInsightsT()
  const earlier = shiftRange(range, -1, today)
  const later = shiftRange(range, 1, today)

  function pick(view: InsightsView) {
    if (view === 'custom') {
      const next = customRange(range.from, range.to, today, range.compare)
      if (next) onChange(next)
      return
    }
    onChange(changeView(range, view, today))
  }

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center gap-2">
        <fieldset className="m-0 flex min-w-0 flex-wrap gap-1 border-0 p-0">
          <legend className="sr-only">{t('period.groupLabel')}</legend>
          {[...PRESETS, 'custom' as const].map((view) => (
            <Button
              key={view}
              type="button"
              size="sm"
              variant={range.view === view ? 'default' : 'outline'}
              aria-pressed={range.view === view}
              className="min-h-11 sm:min-h-8"
              onClick={() => pick(view)}
            >
              {t(`period.views.${view}`)}
            </Button>
          ))}
        </fieldset>
        {range.view !== 'ytd' ? (
          <div className="flex gap-1">
            <Button
              type="button"
              size="icon"
              variant="outline"
              className="size-11 sm:size-8"
              aria-label={t('period.earlier')}
              disabled={earlier === null}
              onClick={() => earlier && onChange(earlier)}
            >
              <ChevronLeft aria-hidden="true" />
            </Button>
            <Button
              type="button"
              size="icon"
              variant="outline"
              className="size-11 sm:size-8"
              aria-label={t('period.later')}
              disabled={later === null}
              onClick={() => later && onChange(later)}
            >
              <ChevronRight aria-hidden="true" />
            </Button>
          </div>
        ) : null}
      </div>
      {range.view === 'custom' ? (
        <CustomRangeForm key={`${range.from}/${range.to}`} range={range} today={today} onChange={onChange} />
      ) : null}
    </div>
  )
}

function CustomRangeForm({
  range,
  today,
  onChange,
}: {
  range: InsightsRange
  today: PlainDate
  onChange: (range: InsightsRange) => void
}) {
  const { t } = useInsightsT()
  const id = useId()
  const [from, setFrom] = useState<string>(range.from)
  const [to, setTo] = useState<string>(range.to)
  const [error, setError] = useState<CustomRangeError | null>(null)
  const errorId = `${id}-error`

  function submit(event: FormEvent) {
    event.preventDefault()
    const problem = validateCustomRange(from, to)
    setError(problem)
    if (problem) return
    const next = customRange(from, to, today, range.compare)
    if (next) onChange(next)
  }

  return (
    <form
      onSubmit={submit}
      noValidate
      className="flex flex-wrap items-end gap-3"
      aria-label={t('period.custom.legend')}
    >
      <div className="flex flex-col gap-1">
        <Label htmlFor={`${id}-from`}>{t('period.custom.from')}</Label>
        <Input
          id={`${id}-from`}
          type="date"
          min={MIN_DATE}
          max={MAX_DATE}
          value={from}
          onChange={(event) => setFrom(event.target.value)}
          aria-invalid={error === 'invalidFrom' || error === 'outOfRange'}
          aria-describedby={error ? errorId : undefined}
          className="h-11 w-44 sm:h-9"
        />
      </div>
      <div className="flex flex-col gap-1">
        <Label htmlFor={`${id}-to`}>{t('period.custom.to')}</Label>
        <Input
          id={`${id}-to`}
          type="date"
          min={MIN_DATE}
          max={MAX_DATE}
          value={to}
          onChange={(event) => setTo(event.target.value)}
          aria-invalid={error === 'invalidTo' || error === 'toBeforeFrom' || error === 'tooLong'}
          aria-describedby={error ? errorId : undefined}
          className="h-11 w-44 sm:h-9"
        />
      </div>
      <Button type="submit" className="min-h-11 sm:min-h-9">
        {t('period.custom.apply')}
      </Button>
      {error ? (
        <p id={errorId} role="alert" className="w-full text-sm text-destructive">
          {t(`period.custom.errors.${error}`)}
        </p>
      ) : null}
    </form>
  )
}
