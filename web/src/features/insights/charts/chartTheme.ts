import type { CSSProperties } from 'react'

/** Series colours come from the run-family tokens in index.css (they have light and dark variants). */
export const CHART_COLORS = {
  completed: 'var(--run-long)',
  planned: 'var(--run-long)',
  acute: 'var(--run-long)',
  chronic: 'var(--run-trail)',
  ratio: 'var(--run-long)',
  pace: 'var(--run-easy)',
  bandLow: 'var(--run-treadmill)',
  bandOptimal: 'var(--run-easy)',
  bandHigh: 'var(--run-race)',
  grid: 'var(--border)',
} as const

export const AXIS_TICK = { fontSize: 11, fill: 'currentColor' } as const
export const CHART_HEIGHT = 220
export const CHART_INITIAL = { width: 600, height: CHART_HEIGHT } as const
export const CHART_WRAPPER_STYLE: CSSProperties = { color: 'var(--muted-foreground)' }

export function compactNumber(value: number, locale: string): string {
  return new Intl.NumberFormat(locale, { notation: 'compact', maximumFractionDigits: 1 }).format(value)
}
