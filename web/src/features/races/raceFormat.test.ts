import { describe, expect, it } from 'vitest'
import { formatRaceDistances, formatRacePrice, sourceName } from './raceFormat'

describe('formatRaceDistances', () => {
  it('lists distances in km with at most one decimal', () => {
    expect(formatRaceDistances([10_000, 21_097.5], 'en')).toBe('10 / 21.1 km')
    expect(formatRaceDistances([42_195], 'en')).toBe('42.2 km')
    expect(formatRaceDistances([5000], 'en')).toBe('5 km')
    expect(formatRaceDistances([100_000], 'en')).toBe('100 km')
    expect(formatRaceDistances([], 'en')).toBe('')
  })

  it('uses the locale decimal separator', () => {
    expect(formatRaceDistances([21_097.5], 'pl')).toBe('21,1 km')
  })
})

describe('formatRacePrice', () => {
  it('drops zero decimals and keeps two otherwise', () => {
    expect(formatRacePrice(60, 'PLN', 'en')).toBe('PLN 60')
    expect(formatRacePrice(59.9, 'PLN', 'en')).toBe('PLN 59.90')
    expect(formatRacePrice(0, 'PLN', 'en')).toBe('PLN 0')
  })

  it('falls back to plain text for an invalid currency code', () => {
    expect(formatRacePrice(15, 'not-a-currency', 'en')).toBe('15 not-a-currency')
  })
})

describe('sourceName', () => {
  const sources = [{ id: 'running-life', name: 'running.life', url: 'https://running.life/' }]
  it('resolves a known source and tolerates an unknown id', () => {
    expect(sourceName(sources, 'running-life')).toBe('running.life')
    expect(sourceName(sources, 'future-source')).toBe('future-source')
    expect(sourceName(undefined, 'running-life')).toBe('running-life')
  })
})
