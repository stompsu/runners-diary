import { describe, expect, it } from 'vitest'
import {
  formatDistance,
  formatElevation,
  formatElevationDelta,
  formatKmInput,
  formatPace,
  kmToMeters,
  metersToKm,
  paceSecondsPerKm,
  parseKm,
} from './units'

describe('distance', () => {
  it('converts between metres and km', () => {
    expect(metersToKm(12345)).toBe(12.345)
    expect(kmToMeters(12.345)).toBe(12345)
    expect(kmToMeters(0.0004)).toBe(0)
    expect(kmToMeters(21.0975)).toBe(21098)
    expect(kmToMeters(5.1)).toBe(5100)
  })

  it('formats km with up to two decimals', () => {
    expect(formatDistance(12345, 'en')).toBe('12.35 km')
    expect(formatDistance(10000, 'en')).toBe('10 km')
    expect(formatDistance(21097, 'en')).toBe('21.1 km')
    expect(formatDistance(0, 'en')).toBe('0 km')
    expect(formatDistance(1234567, 'en')).toBe('1,234.57 km')
  })

  it('formats the editable km value without locale', () => {
    expect(formatKmInput(10000)).toBe('10')
    expect(formatKmInput(21097)).toBe('21.097')
    expect(formatKmInput(5100)).toBe('5.1')
  })

  it('parses user input with comma or dot', () => {
    expect(parseKm('12,5')).toBe(12.5)
    expect(parseKm('12.5')).toBe(12.5)
    expect(parseKm(' 10 ')).toBe(10)
    for (const bad of ['', '0', '-1', 'abc', '1,2,3', '1e3', '.5', '5.']) expect(parseKm(bad)).toBeNull()
  })
})

describe('elevation', () => {
  it('formats metres', () => {
    expect(formatElevation(120, 'en')).toBe('120 m')
    expect(formatElevation(1234.6, 'en')).toBe('1,235 m')
    expect(formatElevation(0, 'en')).toBe('0 m')
  })
})

describe('pace', () => {
  it('formats min/km', () => {
    expect(formatPace(3000, 10000)).toBe('5:00 /km')
    expect(formatPace(2640, 8000)).toBe('5:30 /km')
    expect(formatPace(5880, 21097)).toBe('4:39 /km')
    expect(formatPace(3600, 1000)).toBe('60:00 /km')
  })

  it('rounds up to the next minute instead of showing 60 seconds', () => {
    // 359.6 s/km rounds to 360 -> 6:00, not 5:60.
    expect(formatPace(359.6, 1000)).toBe('6:00 /km')
    expect(formatPace(299.5, 1000)).toBe('5:00 /km')
  })

  it('is null without distance or duration', () => {
    expect(formatPace(null, 1000)).toBeNull()
    expect(formatPace(300, null)).toBeNull()
    expect(formatPace(300, 0)).toBeNull()
    expect(formatPace(0, 1000)).toBeNull()
    expect(formatPace(undefined, undefined)).toBeNull()
    expect(paceSecondsPerKm(300, -5)).toBeNull()
  })
})

describe('formatElevationDelta', () => {
  it('signs gains and losses and leaves zero unsigned', () => {
    expect(formatElevationDelta(3.2, 'en')).toBe('+3 m')
    expect(formatElevationDelta(-2.4, 'en')).toBe('-2 m')
    expect(formatElevationDelta(0, 'en')).toBe('0 m')
  })
})
