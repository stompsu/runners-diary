import { addDays, isoWeek, startOfIsoWeek, type PlainDate } from '@/lib/dates/plainDate'
import type { GoalDto, TrainingSuggestion } from './goalTypes'

export interface SuggestionWeek {
  /** The Monday of the ISO week. */
  start: PlainDate
  isoWeek: number
  items: TrainingSuggestion[]
}

/** Groups suggestions (already by date) into Monday-first ISO weeks for the goal page. */
export function groupByIsoWeek(items: readonly TrainingSuggestion[]): SuggestionWeek[] {
  const weeks = new Map<PlainDate, SuggestionWeek>()
  for (const item of items) {
    const start = startOfIsoWeek(item.date)
    const week = weeks.get(start) ?? { start, isoWeek: isoWeek(start).isoWeek, items: [] }
    week.items.push(item)
    weeks.set(start, week)
  }
  return [...weeks.values()].sort((a, b) => a.start.localeCompare(b.start))
}

/** Active goals first is the server's order; the page splits the list into the three statuses. */
export function splitByStatus(goals: readonly GoalDto[]) {
  return {
    Active: goals.filter((g) => g.status === 'Active'),
    Achieved: goals.filter((g) => g.status === 'Achieved'),
    Archived: goals.filter((g) => g.status === 'Archived'),
  }
}

/** The other Active goal that holds pending suggestions (generating would replace them), if any. */
export function otherGoalWithPending(goals: readonly GoalDto[], goalId: string): GoalDto | undefined {
  return goals.find((g) => g.id !== goalId && (g.plan?.pendingCount ?? 0) > 0)
}

export const weekEnd = (start: PlainDate) => addDays(start, 6)

/** "5:10" from seconds per km (the unit "/km" is appended by the caller's format string). */
export function paceText(secondsPerKm: number): string {
  const total = Math.round(secondsPerKm)
  return `${Math.floor(total / 60)}:${String(total % 60).padStart(2, '0')} /km`
}
