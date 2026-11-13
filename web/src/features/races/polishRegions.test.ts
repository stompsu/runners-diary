import { describe, expect, it } from 'vitest'
import en from '@/i18n/en/races.json'
import { raceInterestsSchema } from './raceInterestsSchema'
import { POLISH_REGION_CODES } from './polishRegions'

describe('POLISH_REGION_CODES', () => {
  it('is the 16 ISO 3166-2:PL codes PL-02 ... PL-32', () => {
    expect(POLISH_REGION_CODES).toEqual(
      Array.from({ length: 16 }, (_, i) => `PL-${String(2 + 2 * i).padStart(2, '0')}`),
    )
  })

  it('has an English and a Polish name for every code, and no extra names', () => {
    expect(Object.keys(en.regions).sort()).toEqual([...POLISH_REGION_CODES].sort())
    for (const names of Object.values(en.regions)) {
      expect(names.en).not.toBe('')
      expect(names.pl).not.toBe('')
    }
  })

  it('is exactly what the zod schema accepts', () => {
    const base = {
      kinds: ['Road'],
      center: null,
      radiusKm: '',
      minDistanceKm: '',
      maxDistanceKm: '',
      horizonDays: 120,
      keywords: '',
      showInCalendar: true,
    }
    for (const code of POLISH_REGION_CODES) {
      expect(raceInterestsSchema.safeParse({ ...base, regionCodes: [code] }).success).toBe(true)
    }
    for (const code of ['PL-00', 'PL-01', 'PL-33', 'PL-24 ', 'pl-24', 'DE-BY']) {
      expect(raceInterestsSchema.safeParse({ ...base, regionCodes: [code] }).success).toBe(false)
    }
  })
})
