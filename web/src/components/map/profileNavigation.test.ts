import { describe, expect, it } from 'vitest'
import type { ProfileSample } from '@/features/tracks/trackGeoJson'
import { indexAtDistance, nextProfileIndex } from './profileNavigation'

const profile: ProfileSample[] = [0, 10, 20, 40, 100].map((d) => [d, 200, 18, 50])

describe('indexAtDistance', () => {
  it('finds the nearest sample', () => {
    expect(indexAtDistance(profile, 0)).toBe(0)
    expect(indexAtDistance(profile, 4)).toBe(0)
    expect(indexAtDistance(profile, 6)).toBe(1)
    expect(indexAtDistance(profile, 29)).toBe(2)
    expect(indexAtDistance(profile, 31)).toBe(3)
    expect(indexAtDistance(profile, 69)).toBe(3)
    expect(indexAtDistance(profile, 71)).toBe(4)
  })

  it('clamps outside the track and copes with an empty profile', () => {
    expect(indexAtDistance(profile, -50)).toBe(0)
    expect(indexAtDistance(profile, 5000)).toBe(4)
    expect(indexAtDistance([], 10)).toBe(0)
  })
})

describe('nextProfileIndex', () => {
  it('steps one sample with the arrows and stays inside the track', () => {
    expect(nextProfileIndex('ArrowRight', 3, 100)).toBe(4)
    expect(nextProfileIndex('ArrowUp', 3, 100)).toBe(4)
    expect(nextProfileIndex('ArrowLeft', 3, 100)).toBe(2)
    expect(nextProfileIndex('ArrowDown', 3, 100)).toBe(2)
    expect(nextProfileIndex('ArrowRight', 99, 100)).toBe(99)
    expect(nextProfileIndex('ArrowLeft', 0, 100)).toBe(0)
  })

  it('starts at the beginning (or the end) when nothing is selected yet', () => {
    expect(nextProfileIndex('ArrowRight', null, 100)).toBe(0)
    expect(nextProfileIndex('ArrowLeft', null, 100)).toBe(99)
  })

  it('jumps a tenth with Page keys and to the ends with Home/End', () => {
    expect(nextProfileIndex('PageUp', 5, 101)).toBe(15)
    expect(nextProfileIndex('PageDown', 5, 101)).toBe(0)
    expect(nextProfileIndex('PageUp', 95, 101)).toBe(100)
    expect(nextProfileIndex('Home', 50, 101)).toBe(0)
    expect(nextProfileIndex('End', 50, 101)).toBe(100)
  })

  it('ignores other keys and empty profiles', () => {
    expect(nextProfileIndex('a', 3, 100)).toBeNull()
    expect(nextProfileIndex('Tab', 3, 100)).toBeNull()
    expect(nextProfileIndex('ArrowRight', 0, 0)).toBeNull()
  })
})
