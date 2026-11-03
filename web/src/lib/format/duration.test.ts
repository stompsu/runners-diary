import { describe, expect, it } from 'vitest'
import { formatDuration, parseDuration } from './duration'

describe('formatDuration', () => {
  it.each([
    [0, '0:00:00'],
    [59, '0:00:59'],
    [60, '0:01:00'],
    [2640, '0:44:00'],
    [3725, '1:02:05'],
    [36000, '10:00:00'],
    [3599.6, '1:00:00'],
  ])('%s s -> %s', (seconds, expected) => expect(formatDuration(seconds)).toBe(expected))
})

describe('parseDuration', () => {
  it.each([
    ['1:02:05', 3725],
    ['0:45:10', 2710],
    ['45:10', 2710],
    ['5:00', 300],
    ['95:00', 5700],
    [' 1:00:00 ', 3600],
    ['0:00', 0],
  ])('accepts %j', (input, expected) => expect(parseDuration(input)).toBe(expected))

  it.each(['1:75', '1:60:00', '1:00:75', '', '45', 'a:bc', '1:2:3:4', '-1:00', '1.5:00', '1:-5', '1000:00'])(
    'rejects %j',
    (input) => expect(parseDuration(input)).toBeNull(),
  )

  it('round-trips with formatDuration', () => {
    for (const s of [1, 59, 60, 3599, 3600, 45296]) expect(parseDuration(formatDuration(s))).toBe(s)
  })
})
