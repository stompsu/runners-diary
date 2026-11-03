import { describe, expect, it } from 'vitest'
import { targetForKey } from './gridNavigation'

const nov = { year: 2026, month: 11 }

describe('targetForKey', () => {
  it.each([
    ['ArrowLeft', '2026-11-14', '2026-11-13'],
    ['ArrowRight', '2026-11-14', '2026-11-15'],
    ['ArrowUp', '2026-11-14', '2026-11-07'],
    ['ArrowDown', '2026-11-14', '2026-11-21'],
    ['Home', '2026-11-14', '2026-11-09'],
    ['End', '2026-11-14', '2026-11-15'],
    ['Home', '2026-11-09', '2026-11-09'],
    ['End', '2026-11-15', '2026-11-15'],
  ])('%s from %s goes to %s', (key, from, to) => {
    expect(targetForKey(key, from, nov)).toBe(to)
  })

  it('moves across month and year edges', () => {
    expect(targetForKey('ArrowRight', '2026-12-31', { year: 2026, month: 12 })).toBe('2027-01-01')
    expect(targetForKey('ArrowLeft', '2026-11-01', nov)).toBe('2026-10-31')
  })

  it('PageUp/PageDown keep the day number of the active day, relative to the displayed month', () => {
    expect(targetForKey('PageDown', '2026-11-14', nov)).toBe('2026-12-14')
    expect(targetForKey('PageUp', '2026-11-14', nov)).toBe('2026-10-14')
    // A dimmed day of the previous month still moves relative to the displayed month.
    expect(targetForKey('PageDown', '2026-10-28', nov)).toBe('2026-12-28')
  })

  it('clamps the day to the target month length', () => {
    expect(targetForKey('PageUp', '2026-03-31', { year: 2026, month: 3 })).toBe('2026-02-28')
    expect(targetForKey('PageDown', '2024-01-31', { year: 2024, month: 1 })).toBe('2024-02-29')
  })

  it('stops at the supported range 1950-2100', () => {
    expect(targetForKey('PageUp', '1950-01-15', { year: 1950, month: 1 })).toBeNull()
    expect(targetForKey('ArrowLeft', '1950-01-01', { year: 1950, month: 1 })).toBeNull()
    expect(targetForKey('PageDown', '2100-12-15', { year: 2100, month: 12 })).toBeNull()
    expect(targetForKey('ArrowRight', '2100-12-31', { year: 2100, month: 12 })).toBeNull()
  })

  it('ignores other keys', () => {
    expect(targetForKey('a', '2026-11-14', nov)).toBeNull()
    expect(targetForKey('Enter', '2026-11-14', nov)).toBeNull()
  })
})
