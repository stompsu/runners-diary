import { monthOf, todayFromMe } from '@/lib/dates/plainDate'

export interface DiaryMonth {
  year: number
  /** 1–12 */
  month: number
}

/** The API accepts 1950-01-01 … 2100-12-31 (api-contract.md). */
export const MIN_YEAR = 1950
export const MAX_YEAR = 2100

export function isInRange({ year, month }: DiaryMonth): boolean {
  return Number.isInteger(year) && year >= MIN_YEAR && year <= MAX_YEAR && month >= 1 && month <= 12
}

/** Parses the /diary/:year/:month route params; returns null for anything that isn't a real, in-range month. */
export function parseDiaryMonth(year: string | undefined, month: string | undefined): DiaryMonth | null {
  if (!year || !month || !/^\d{4}$/.test(year) || !/^\d{1,2}$/.test(month)) return null
  const parsed = { year: Number(year), month: Number(month) }
  return isInRange(parsed) ? parsed : null
}

export function diaryPath({ year, month }: DiaryMonth): string {
  return `/diary/${year}/${String(month).padStart(2, '0')}`
}

/** `me.today` (the user's zone, ADR 0008), never the browser clock. */
export function currentDiaryMonth(me: { today: string }): DiaryMonth {
  return monthOf(todayFromMe(me))
}

/** The month `delta` months away, or null when it falls outside the supported range. */
export function shiftMonth({ year, month }: DiaryMonth, delta: number): DiaryMonth | null {
  const index = year * 12 + (month - 1) + delta
  const shifted = { year: Math.floor(index / 12), month: (index % 12) + 1 }
  return isInRange(shifted) ? shifted : null
}
