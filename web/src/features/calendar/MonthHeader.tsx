import { ChevronDown, ChevronLeft, ChevronRight, List, Plus } from 'lucide-react'
import { lazy, Suspense, useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { Button } from '@/components/ui/button'
import { diaryPath, type DiaryMonth } from '@/features/diary/diaryMonth'
import type { PlainDate } from '@/lib/dates/plainDate'
import { monthLabel } from '@/lib/dates/plainDate'

// The picker's popover (Radix Popper + floating-ui) is ~12 kB gzip that nothing else in the initial load needs.
const MonthYearPicker = lazy(() => import('./MonthYearPicker').then((m) => ({ default: m.MonthYearPicker })))

interface MonthHeaderProps {
  month: DiaryMonth
  previous: DiaryMonth | null
  next: DiaryMonth | null
  /** Date the "Add run" form is prefilled with. */
  addDate: PlainDate
  onToday: () => void
  onSelectMonth: (month: DiaryMonth) => void
}

/** Title, prev/next, Today, month/year picker, month list and Add run. Stable across month changes (keeps focus). */
export function MonthHeader({ month, previous, next, addDate, onToday, onSelectMonth }: MonthHeaderProps) {
  const { t, i18n } = useTranslation('calendar')
  const { t: tRuns } = useTranslation('runs')
  const base = diaryPath(month)
  const iconButton = 'size-11 sm:size-9'
  const label = monthLabel(month.year, month.month, i18n.language)

  // Prev/next/Today/picker only change the URL: sighted users see the grid change, a screen-reader user hears nothing.
  // The status text is set only after a change (never on first render), so it is announced as news.
  const monthKey = `${month.year}-${month.month}`
  const [seenKey, setSeenKey] = useState(monthKey)
  const [announcement, setAnnouncement] = useState('')
  if (seenKey !== monthKey) {
    setSeenKey(monthKey)
    setAnnouncement(t('showingMonth', { month: label }))
  }

  return (
    <header className="mb-4 flex flex-col gap-3">
      <h1 id="diary-title" className="text-2xl font-semibold">
        {t('title', { month: label })}
      </h1>
      <output className="sr-only" data-testid="month-announcement">
        {announcement}
      </output>
      <div className="flex flex-wrap items-center gap-2">
        <div className="flex items-center gap-1">
          <NavButton target={previous} label={t('previousMonth')} className={iconButton}>
            <ChevronLeft aria-hidden="true" />
          </NavButton>
          <NavButton target={next} label={t('nextMonth')} className={iconButton}>
            <ChevronRight aria-hidden="true" />
          </NavButton>
        </div>
        <Button type="button" variant="outline" className="h-11 sm:h-9" onClick={onToday}>
          {t('today')}
        </Button>
        <Suspense
          fallback={
            <Button
              type="button"
              variant="outline"
              className="h-11 sm:h-9"
              aria-label={t('picker.trigger', { month: monthLabel(month.year, month.month, i18n.language) })}
            >
              {monthLabel(month.year, month.month, i18n.language)}
              <ChevronDown aria-hidden="true" />
            </Button>
          }
        >
          <MonthYearPicker month={month} onSelect={onSelectMonth} />
        </Suspense>
        <div className="ml-auto flex items-center gap-2">
          <Button asChild variant="outline" className="h-11 sm:h-9">
            <Link to={`${base}/list`}>
              <List aria-hidden="true" />
              {t('monthList')}
            </Link>
          </Button>
          <Button asChild className="h-11 sm:h-9">
            <Link to={`${base}/runs/new?date=${addDate}`}>
              <Plus aria-hidden="true" />
              {tRuns('actions.add')}
            </Link>
          </Button>
        </div>
      </div>
    </header>
  )
}

function NavButton({
  target,
  label,
  className,
  children,
}: {
  target: DiaryMonth | null
  label: string
  className: string
  children: ReactNode
}) {
  if (!target) {
    return (
      <Button type="button" variant="outline" size="icon" className={className} aria-label={label} disabled>
        {children}
      </Button>
    )
  }
  return (
    <Button asChild variant="outline" size="icon" className={className}>
      <Link to={diaryPath(target)} aria-label={label}>
        {children}
      </Link>
    </Button>
  )
}
