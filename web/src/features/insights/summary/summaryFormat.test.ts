import { describe, expect, it } from 'vitest'
import { formatCreated, formatResetTime } from './summaryFormat'

// The stored instants are UTC; the card shows them in the user's zone (a run logged at 23:30 UTC is already tomorrow in
// Kiritimati). Runs under `test:tz` too, so the process zone must never leak into the output.
const LATE = '2026-11-04T23:30:00Z'

describe('formatCreated', () => {
  it('uses the user zone, not the process zone', () => {
    expect(formatCreated(LATE, 'en', 'Pacific/Kiritimati')).toBe('Nov 5, 2026')
    expect(formatCreated(LATE, 'en', 'America/Los_Angeles')).toBe('Nov 4, 2026')
    expect(formatCreated(LATE, 'en', 'UTC')).toBe('Nov 4, 2026')
  })

  it('falls back on UTC for an unknown zone and shows nothing for an unparseable instant', () => {
    expect(formatCreated(LATE, 'en', 'Mars/Olympus')).toBe('Nov 4, 2026')
    expect(formatCreated('not a date', 'en', 'UTC')).toBeNull()
  })
})

describe('formatResetTime', () => {
  it('shows the day and the local clock time of the reset in the user zone', () => {
    const la = formatResetTime(LATE, 'en', 'America/Los_Angeles')
    const kiritimati = formatResetTime(LATE, 'en', 'Pacific/Kiritimati')
    expect(la).toMatch(/^Nov 4,.*3:30\s?PM$/)
    expect(kiritimati).toMatch(/^Nov 5,.*1:30\s?PM$/)
  })

  it('does not throw for an unknown zone', () => {
    expect(formatResetTime(LATE, 'en', 'Mars/Olympus')).toMatch(/^Nov 4,.*11:30\s?PM$/)
  })
})
