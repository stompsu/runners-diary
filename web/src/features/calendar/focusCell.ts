import { isValid, type PlainDate } from '@/lib/dates/plainDate'

/**
 * Puts the keyboard focus back on a day of the (still mounted) grid when a panel or dialog closes (AC13).
 * Falls back to the grid's current tab stop when the day is not on screen or unknown.
 */
export function focusDayCell(date?: PlainDate | null): void {
  const byDate =
    date && isValid(date) ? document.querySelector<HTMLElement>(`[role="gridcell"][data-date="${date}"]`) : null
  const target = byDate ?? document.querySelector<HTMLElement>('[role="gridcell"][tabindex="0"]')
  target?.focus()
}
