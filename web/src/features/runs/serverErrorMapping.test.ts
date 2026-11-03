import { describe, expect, it } from 'vitest'
import { mapRunFieldErrors } from './serverErrorMapping'

describe('mapRunFieldErrors', () => {
  it('maps SI wire keys onto display-unit form fields', () => {
    const { fields, summary } = mapRunFieldErrors({
      distanceMeters: ['d'],
      durationSeconds: ['t'],
      elevationGainMeters: ['g'],
      elevationLossMeters: ['l'],
      'race.officialDistanceMeters': ['o'],
      'race.targetTimeSeconds': ['tt'],
      'race.resultTimeSeconds': ['rt'],
    })
    expect(fields).toEqual({
      distanceKm: 'd',
      duration: 't',
      elevationGain: 'g',
      elevationLoss: 'l',
      'race.officialDistanceKm': 'o',
      'race.targetTime': 'tt',
      'race.resultTime': 'rt',
    })
    expect(summary).toEqual([])
  })

  it('keeps identity keys, and sends body and unknown keys to the summary', () => {
    const { fields, summary } = mapRunFieldErrors({
      title: ['x'],
      'rating.stars': ['s'],
      date: ['dd'],
      body: ['The request body is invalid.'],
      'location.latitude': ['lat'],
      race: ['Race details are only allowed for race run types.'],
    })
    expect(fields).toEqual({ title: 'x', 'rating.stars': 's', date: 'dd' })
    expect(summary).toEqual([
      'The request body is invalid.',
      'lat',
      'Race details are only allowed for race run types.',
    ])
  })
})
