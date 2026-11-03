import { addDays, daysInMonth, format, monthOf, parse, startOfIsoWeek, type PlainDate } from '@/lib/dates/plainDate'
import { isInRange, shiftMonth, type DiaryMonth } from '@/features/diary/diaryMonth'

export const NAVIGATION_KEYS = ['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'Home', 'End', 'PageUp', 'PageDown']

/**
 * Where a grid navigation key moves the focus (ADR 0016): arrows by a day or a week, Home/End to Monday/Sunday of the
 * week, PageUp/PageDown to the displayed month ±1 with the same day number (clamped). Null when the key is not a
 * navigation key or the target is outside the supported 1950-2100 range. The target may lie outside the displayed grid.
 */
export function targetForKey(key: string, active: PlainDate, displayed: DiaryMonth): PlainDate | null {
  const target = rawTarget(key, active, displayed)
  return target !== null && isInRange(monthOf(target)) ? target : null
}

function rawTarget(key: string, active: PlainDate, displayed: DiaryMonth): PlainDate | null {
  switch (key) {
    case 'ArrowLeft':
      return addDays(active, -1)
    case 'ArrowRight':
      return addDays(active, 1)
    case 'ArrowUp':
      return addDays(active, -7)
    case 'ArrowDown':
      return addDays(active, 7)
    case 'Home':
      return startOfIsoWeek(active)
    case 'End':
      return addDays(startOfIsoWeek(active), 6)
    case 'PageUp':
    case 'PageDown': {
      const next = shiftMonth(displayed, key === 'PageUp' ? -1 : 1)
      const parts = parse(active)
      if (!next || !parts) return null
      return format({ ...next, day: Math.min(parts.day, daysInMonth(next.year, next.month)) })
    }
    default:
      return null
  }
}
