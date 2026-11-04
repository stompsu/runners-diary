import { beforeEach, describe, expect, it } from 'vitest'
import {
  coordinateText,
  formatCoordinates,
  parseCoordinate,
  readLastPick,
  saveLastPick,
  validLatitude,
  validLongitude,
} from './locationCoordinates'

describe('parseCoordinate', () => {
  it.each([
    ['50.2945', 50.2945],
    ['50,2945', 50.2945],
    [' -18.5 ', -18.5],
    ['0', 0],
    ['-0.000001', -0.000001],
  ])('%j -> %j', (input, expected) => expect(parseCoordinate(input)).toBe(expected))

  it.each(['', ' ', '50N', '5e1', '1.2.3', '--1', '+5', 'NaN', 'Infinity', '50 18'])(
    '%j is not a coordinate',
    (input) => expect(parseCoordinate(input)).toBeNull(),
  )
})

describe('ranges', () => {
  it('accepts the poles and the antimeridian, rejects beyond and non-numbers', () => {
    expect([validLatitude(90), validLatitude(-90), validLatitude(90.0001), validLatitude(Number.NaN)]).toEqual([
      true,
      true,
      false,
      false,
    ])
    expect([validLongitude(180), validLongitude(-180), validLongitude(180.5), validLongitude('1')]).toEqual([
      true,
      true,
      false,
      false,
    ])
  })
})

describe('formatting', () => {
  it('shows 6 decimals at most in fields and 5 in text', () => {
    expect(coordinateText(50.294862123)).toBe('50.294862')
    expect(coordinateText(50)).toBe('50')
    expect(formatCoordinates(50.2945, 18.6714)).toBe('50.29450, 18.67140')
  })
})

describe('last pick', () => {
  beforeEach(() => sessionStorage.clear())

  it('remembers the last position in this tab and ignores garbage', () => {
    expect(readLastPick()).toBeNull()
    saveLastPick({ latitude: 49.5, longitude: 19.5 })
    expect(readLastPick()).toEqual({ latitude: 49.5, longitude: 19.5 })
    sessionStorage.setItem('rd.lastMapPick', '{"latitude":99,"longitude":1}')
    expect(readLastPick()).toBeNull()
    sessionStorage.setItem('rd.lastMapPick', 'not json')
    expect(readLastPick()).toBeNull()
  })
})
