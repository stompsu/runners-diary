/* oxlint-disable jsx-a11y/click-events-have-key-events, jsx-a11y/no-redundant-roles --
   Keys are handled once on the grid (MonthGrid.onKeyDown). The explicit gridcell role is redundant in browsers
   (td inside role=grid) but keeps AT and test-library role mappings unambiguous. */
import { useTranslation } from 'react-i18next'
import { cn } from '@/lib/utils'
import { dayLabel, parse, type PlainDate } from '@/lib/dates/plainDate'
import { formatDistance, metersToKm } from '@/lib/format/units'
import { RunBubble, RunDot } from './RunBubble'
import { runTypeLabelKey } from './runTypePresentation'
import type { Run } from './types'

const MAX_BUBBLES = 3

interface DayCellProps {
  date: PlainDate
  inMonth: boolean
  isToday: boolean
  isTabStop: boolean
  isSelected: boolean
  runs: Run[]
  codes: ReadonlyMap<number, string>
  onActivate: (date: PlainDate) => void
  onOpenDay: (date: PlainDate) => void
  onOpenRun: (runId: string) => void
}

/**
 * One day: a gridcell and a single focus stop (roving tabindex). Its accessible name carries the full date and every
 * run, e.g. "Saturday, November 14, 2026, 2 runs: Long run, Completed, 21.1 km; Strides and drills, Planned".
 */
export function DayCell({
  date,
  inMonth,
  isToday,
  isTabStop,
  isSelected,
  runs,
  codes,
  onActivate,
  onOpenDay,
  onOpenRun,
}: DayCellProps) {
  const { t, i18n } = useTranslation('calendar')
  const { t: tType } = useTranslation('runTypes')
  const locale = i18n.language
  const day = parse(date)?.day

  const summaries = runs.map((run) => {
    const type = tType(runTypeLabelKey(codes.get(run.runTypeId)))
    const status = t(`status.${run.status === 'Completed' ? 'completed' : 'planned'}`)
    return run.distanceMeters
      ? t('cell.runWithDistance', { type, status, distance: formatDistance(run.distanceMeters, locale) })
      : t('cell.run', { type, status })
  })
  const label =
    runs.length === 0
      ? t('cell.empty', { date: dayLabel(date, locale) })
      : t('cell.runs', { count: runs.length, date: dayLabel(date, locale), runs: summaries.join('; ') })

  const totalMeters = runs.reduce((sum, run) => sum + (run.distanceMeters ?? 0), 0)

  return (
    // Keys are handled once on the grid (MonthGrid.onKeyDown); the cell is the roving focus stop.
    <td
      role="gridcell"
      aria-current={isToday ? 'date' : undefined}
      data-date={date}
      data-in-month={inMonth}
      data-selected={isSelected || undefined}
      tabIndex={isTabStop ? 0 : -1}
      onFocus={() => onActivate(date)}
      onClick={() => onOpenDay(date)}
      className={cn(
        'h-14 min-w-0 cursor-pointer overflow-hidden border p-0.5 align-top outline-none focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-primary sm:h-24 sm:p-1',
        inMonth ? 'bg-background' : 'bg-muted/60 text-muted-foreground',
        isSelected && 'ring-2 ring-primary ring-inset',
      )}
    >
      {/* The name comes from content (not aria-label), so visible text never mismatches the accessible name. */}
      <span className="sr-only">{label}</span>
      <span
        aria-hidden="true"
        className={cn(
          'inline-flex size-6 items-center justify-center rounded-full text-xs',
          isToday && 'bg-primary font-bold text-primary-foreground',
        )}
      >
        {day}
      </span>
      <div aria-hidden="true" className="mt-0.5 hidden flex-col gap-0.5 sm:flex">
        {runs.slice(0, MAX_BUBBLES).map((run) => (
          <RunBubble key={run.id} run={run} code={codes.get(run.runTypeId)} onOpen={onOpenRun} />
        ))}
        {runs.length > MAX_BUBBLES ? (
          <span className="px-1 text-[11px] text-muted-foreground">
            {t('more', { count: runs.length - MAX_BUBBLES })}
          </span>
        ) : null}
      </div>
      {runs.length > 0 ? (
        <div aria-hidden="true" className="flex flex-col items-center gap-0.5 sm:hidden">
          <span className="flex flex-wrap justify-center gap-0.5">
            {runs.slice(0, MAX_BUBBLES + 1).map((run) => (
              <RunDot key={run.id} run={run} code={codes.get(run.runTypeId)} />
            ))}
          </span>
          {totalMeters > 0 ? (
            <span className="text-[10px] leading-3">{Number(metersToKm(totalMeters).toFixed(1))}</span>
          ) : null}
        </div>
      ) : null}
    </td>
  )
}
