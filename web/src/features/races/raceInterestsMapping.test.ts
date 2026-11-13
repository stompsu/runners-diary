import { describe, expect, it } from 'vitest'
import { mapRaceInterestsErrors, toFormValues, toWriteRequest } from './raceInterestsMapping'
import type { RaceInterestsDto } from './raceTypes'

const dto: RaceInterestsDto = {
  configured: true,
  kinds: ['Mountain', 'Road'],
  regionCodes: ['PL-24', 'PL-02'],
  center: { latitude: 50.2945, longitude: 18.6714, name: 'Gliwice' },
  radiusMeters: 60_000,
  minDistanceMeters: 5000,
  maxDistanceMeters: 42_195,
  horizonDays: 120,
  keywords: 'night',
  showInCalendar: true,
  updatedAt: '2026-11-03T10:00:00Z',
}

describe('race interests mapping', () => {
  it('shows SI values in km and keeps nulls as empty fields', () => {
    expect(toFormValues(dto)).toMatchObject({
      radiusKm: '60',
      minDistanceKm: '5',
      maxDistanceKm: '42.195',
      keywords: 'night',
      center: { latitude: 50.2945, longitude: 18.6714, name: 'Gliwice' },
    })
    expect(
      toFormValues({
        ...dto,
        center: null,
        radiusMeters: null,
        minDistanceMeters: null,
        maxDistanceMeters: null,
        keywords: null,
      }),
    ).toMatchObject({ center: null, radiusKm: '', minDistanceKm: '', maxDistanceKm: '', keywords: '' })
  })

  it('writes metres, canonical order, normalised keywords and null for empty values', () => {
    expect(toWriteRequest({ ...toFormValues(dto), keywords: '  night   gliwice ' })).toEqual({
      kinds: ['Road', 'Mountain'],
      regionCodes: ['PL-02', 'PL-24'],
      center: { latitude: 50.2945, longitude: 18.6714, name: 'Gliwice' },
      radiusMeters: 60_000,
      minDistanceMeters: 5000,
      maxDistanceMeters: 42_195,
      horizonDays: 120,
      keywords: 'night gliwice',
      showInCalendar: true,
    })
  })

  it('sends no radius without a place, no name when blank, and no empty limits', () => {
    const request = toWriteRequest({
      ...toFormValues(dto),
      center: { latitude: 1, longitude: 2, name: '  ' },
      radiusKm: '12,5',
      minDistanceKm: '',
      maxDistanceKm: ' ',
      keywords: ' ',
    })
    expect(request).toMatchObject({
      center: { latitude: 1, longitude: 2, name: null },
      radiusMeters: 12_500,
      minDistanceMeters: null,
      maxDistanceMeters: null,
      keywords: null,
    })
    expect(toWriteRequest({ ...toFormValues(dto), center: null, radiusKm: '50' }).radiusMeters).toBeNull()
  })

  it('maps server error keys onto display-unit fields and the place section', () => {
    const { fields, summary } = mapRaceInterestsErrors({
      kinds: ['k'],
      regionCodes: ['r'],
      center: ['c'],
      'center.latitude': ['lat'],
      centerName: ['n'],
      radiusMeters: ['rad'],
      minDistanceMeters: ['min'],
      maxDistanceMeters: ['max'],
      horizonDays: ['h'],
      keywords: ['kw'],
      showInCalendar: ['s'],
      body: ['b'],
    })
    expect(fields).toEqual({
      kinds: 'k',
      regionCodes: 'r',
      center: 'c lat',
      'center.name': 'n',
      radiusKm: 'rad',
      minDistanceKm: 'min',
      maxDistanceKm: 'max',
      horizonDays: 'h',
      keywords: 'kw',
    })
    expect(summary).toEqual(['s', 'b'])
  })
})
