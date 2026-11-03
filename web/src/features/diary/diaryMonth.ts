export interface DiaryMonth {
  year: number
  /** 1–12 */
  month: number
}

const MIN_YEAR = 1900
const MAX_YEAR = 2999

/** Parses the /diary/:year/:month route params; returns null for anything that isn't a real month. */
export function parseDiaryMonth(year: string | undefined, month: string | undefined): DiaryMonth | null {
  if (!year || !month || !/^\d{4}$/.test(year) || !/^\d{1,2}$/.test(month)) return null
  const y = Number(year)
  const m = Number(month)
  if (y < MIN_YEAR || y > MAX_YEAR || m < 1 || m > 12) return null
  return { year: y, month: m }
}

export function diaryPath({ year, month }: DiaryMonth): string {
  return `/diary/${year}/${String(month).padStart(2, '0')}`
}

export function currentDiaryMonth(now: Date = new Date()): DiaryMonth {
  return { year: now.getFullYear(), month: now.getMonth() + 1 }
}
