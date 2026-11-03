import { describe, expect, it } from 'vitest'
import { RUN_TYPES } from '@/test/fakeApi'
import { familyOf, FAMILY_ICON, isKnownRunType, runTypeLabelKey, type RunFamily } from './runTypePresentation'

describe('run type presentation', () => {
  it('maps every catalogue code to a family and a label', () => {
    for (const { code } of RUN_TYPES) {
      expect([code, isKnownRunType(code)]).toEqual([code, true])
      expect(familyOf(code)).not.toBe('unknown')
      expect(runTypeLabelKey(code)).toBe(code)
    }
  })

  it('puts all six race types in the race family (trophy marker)', () => {
    const races = ['roadRace', 'trailRace', 'ultra', 'trackRace', 'crossCountry', 'virtualRace']
    expect(races.map(familyOf)).toEqual(Array(6).fill('race'))
  })

  it('falls back to a neutral family and the "Other" label for codes it does not know', () => {
    expect(familyOf('hoverboard')).toBe('unknown')
    expect(familyOf(undefined)).toBe('unknown')
    expect(runTypeLabelKey('hoverboard')).toBe('unknown')
    expect(runTypeLabelKey('toString')).toBe('unknown')
  })

  it('gives each family its own icon, so colour is never the only cue', () => {
    const families: RunFamily[] = ['race', 'easy', 'long', 'quality', 'trail', 'treadmill', 'cross', 'rest', 'unknown']
    expect(new Set(families.map((f) => FAMILY_ICON[f])).size).toBe(families.length)
  })
})
