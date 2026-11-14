/* oxlint-disable jsx-a11y/interactive-supports-focus, jsx-a11y/no-noninteractive-element-to-interactive-role --
   APG grid pattern: a <table role="grid"> whose cells (not the table) are the focus stops. */
import { useEffect, useMemo, useRef, useState, type KeyboardEvent } from 'react'
import { useTranslation } from 'react-i18next'
import type { DiaryMonth } from '@/features/diary/diaryMonth'
import {
  addDays,
  compare,
  diffDays,
  firstOfMonth,
  isoWeek,
  isValid,
  monthLabel,
  monthOf,
  weekdayLabels,
  type PlainDate,
} from '@/lib/dates/plainDate'
import type { RaceProposal } from '@/features/races/raceTypes'
import { DayCell } from './DayCell'
import { NAVIGATION_KEYS, targetForKey } from './gridNavigation'
import { WeekTotalsCell } from './WeekTotalsCell'
import type { CalendarMonth, Run } from './types'

const NO_PROPOSALS: readonly RaceProposal[] = []
const NO_PROPOSALS_OF_DAY: RaceProposal[] = []
const NO_OP = () => undefined

/** A pending request to put the keyboard focus on a day once the grid for `month` is on screen. */
export interface FocusRequest {
  date: PlainDate
  month: DiaryMonth
}

export interface MonthGridProps {
  data: CalendarMonth
  month: DiaryMonth
  /** `me.today` */
  today: PlainDate
  codes: ReadonlyMap<number, string>
  /** Race proposals to show (already without signed-up and dismissed ones); empty while they are off. */
  proposals?: readonly RaceProposal[]
  /** The day whose panel is open (URL), if any. */
  selectedDate?: PlainDate
  focusRequest: FocusRequest | null
  onFocusHandled: () => void
  /** Keyboard navigation left the grid: go to the month of `date` and focus it there. */
  onMoveToMonth: (date: PlainDate) => void
  onOpenDay: (date: PlainDate) => void
  onOpenRun: (runId: string) => void
  onOpenProposal?: (raceId: string) => void
}

/**
 * The month as an ARIA grid (WAI-ARIA APG grid pattern, ADR 0016): a header row, then one row per ISO week with a
 * `rowheader` (week totals) and seven `gridcell` days. Exactly one day is in the tab order (roving tabindex).
 * The API supplies the grid bounds and the totals; nothing is computed here beyond layout.
 */
export function MonthGrid({
  data,
  month,
  today,
  codes,
  proposals = NO_PROPOSALS,
  selectedDate,
  focusRequest,
  onFocusHandled,
  onMoveToMonth,
  onOpenDay,
  onOpenRun,
  onOpenProposal = NO_OP,
}: MonthGridProps) {
  const { t, i18n } = useTranslation('calendar')
  const locale = i18n.language
  const gridRef = useRef<HTMLTableElement>(null)
  const [active, setActive] = useState<PlainDate | null>(null)

  const { gridStart, gridEnd } = data
  const inGrid = (date: PlainDate | null | undefined): date is PlainDate =>
    !!date && compare(date, gridStart) >= 0 && compare(date, gridEnd) <= 0
  const validSelected = selectedDate && isValid(selectedDate) ? selectedDate : undefined
  const tabStop = [active, validSelected, today, firstOfMonth(month.year, month.month)].find(inGrid) ?? gridStart

  const runsByDate = useMemo(() => {
    const map = new Map<PlainDate, Run[]>()
    for (const run of data.runs) map.set(run.date, [...(map.get(run.date) ?? []), run])
    return map
  }, [data.runs])

  const proposalsByDate = useMemo(() => {
    const map = new Map<PlainDate, RaceProposal[]>()
    for (const proposal of proposals) map.set(proposal.date, [...(map.get(proposal.date) ?? []), proposal])
    return map
  }, [proposals])

  const weeks = useMemo(() => {
    const count = Math.round((diffDays(gridStart, gridEnd) + 1) / 7)
    return Array.from({ length: count }, (_unused, i) => {
      const start = addDays(gridStart, i * 7)
      return {
        start,
        isoWeek: isoWeek(start).isoWeek,
        days: Array.from({ length: 7 }, (_day, d) => addDays(start, d)),
        totals: data.weeks.find((w) => w.start === start),
      }
    })
  }, [data.weeks, gridStart, gridEnd])

  const shortDays = weekdayLabels(locale, 'short')
  const longDays = weekdayLabels(locale, 'long')

  function focusCell(date: PlainDate) {
    gridRef.current?.querySelector<HTMLElement>(`[data-date="${date}"]`)?.focus()
  }

  // Focus requested by Today / month changes from the keyboard; handled once the grid of that month is mounted.
  useEffect(() => {
    if (!focusRequest || focusRequest.month.year !== month.year || focusRequest.month.month !== month.month) return
    gridRef.current?.querySelector<HTMLElement>(`[data-date="${focusRequest.date}"]`)?.focus()
    onFocusHandled()
  }, [focusRequest, month.year, month.month, onFocusHandled])

  function onKeyDown(event: KeyboardEvent<HTMLTableElement>) {
    const cell = (event.target as HTMLElement).closest<HTMLElement>('[role="gridcell"]')
    const date = cell?.dataset.date
    if (!cell || !date || event.target !== cell || event.altKey || event.ctrlKey || event.metaKey) return

    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault()
      onOpenDay(date)
      return
    }
    if (!NAVIGATION_KEYS.includes(event.key)) return
    event.preventDefault()
    const target = targetForKey(event.key, date, month)
    if (!target) return
    // PageUp/PageDown always change the month, even when the target day is also visible as a dimmed cell.
    if (inGrid(target) && event.key !== 'PageUp' && event.key !== 'PageDown') focusCell(target)
    else onMoveToMonth(target)
  }

  return (
    // The grid handles keys for the focused cell; the cells are the focus stops (roving tabindex, APG grid pattern).
    <table
      ref={gridRef}
      role="grid"
      aria-label={t('grid.label', { month: monthLabel(month.year, month.month, locale) })}
      onKeyDown={onKeyDown}
      className="w-full table-fixed border-collapse"
    >
      <colgroup>
        <col className="w-11 sm:w-30" />
      </colgroup>
      <thead>
        <tr>
          <th scope="col" className="border bg-muted/30 p-1 text-left text-[10px] font-medium sm:text-xs">
            {t('weekColumn')}
          </th>
          {shortDays.map((short, i) => (
            <th
              key={short}
              scope="col"
              className="border bg-muted/30 p-1 text-center text-[10px] font-medium sm:text-xs"
            >
              <span aria-hidden="true">{short}</span>
              <span className="sr-only">{longDays[i]}</span>
            </th>
          ))}
        </tr>
      </thead>
      <tbody>
        {weeks.map((week) => (
          <tr key={week.start}>
            <WeekTotalsCell isoWeek={week.isoWeek} week={week.totals} />
            {week.days.map((date) => {
              const parts = monthOf(date)
              return (
                <DayCell
                  key={date}
                  date={date}
                  inMonth={parts.month === month.month && parts.year === month.year}
                  isToday={date === today}
                  isTabStop={date === tabStop}
                  isSelected={date === validSelected}
                  runs={runsByDate.get(date) ?? []}
                  proposals={proposalsByDate.get(date) ?? NO_PROPOSALS_OF_DAY}
                  codes={codes}
                  onActivate={setActive}
                  onOpenDay={onOpenDay}
                  onOpenRun={onOpenRun}
                  onOpenProposal={onOpenProposal}
                />
              )
            })}
          </tr>
        ))}
      </tbody>
    </table>
  )
}
