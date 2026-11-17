import { beforeEach, describe, expect, it } from 'vitest'
import i18n from '@/i18n'
import '@/i18n/lazyBundles'
import { setLanguage } from './language'

/** Polish plural forms (one: 1, few: 2-4 / 22-24, many: 0, 5-21, 25+) for every `count` string of the eager namespaces. */
describe('Polish plurals', () => {
  beforeEach(() => setLanguage('pl'))

  const counts = [0, 1, 2, 5, 12, 22, 25]

  it.each([
    ['calendar:cell.runs', ['treningów', 'trening', 'treningi', 'treningów', 'treningów', 'treningi', 'treningów']],
    [
      'calendar:cell.suggestions',
      [
        'sugerowanych treningów',
        'sugerowany trening',
        'sugerowane treningi',
        'sugerowanych treningów',
        'sugerowanych treningów',
        'sugerowane treningi',
        'sugerowanych treningów',
      ],
    ],
    [
      'calendar:cell.proposals',
      [
        'propozycji startu',
        'propozycja startu',
        'propozycje startu',
        'propozycji startu',
        'propozycji startu',
        'propozycje startu',
        'propozycji startu',
      ],
    ],
    ['runs:rating.starsOption', ['gwiazdek', 'gwiazdka', 'gwiazdki', 'gwiazdek', 'gwiazdek', 'gwiazdki', 'gwiazdek']],
    ['maps:search.found', ['miejsc', 'miejsce', 'miejsca', 'miejsc', 'miejsc', 'miejsca', 'miejsc']],
    [
      'insights:planned.summary',
      [
        'zaplanowanych treningów',
        'zaplanowany trening',
        'zaplanowane treningi',
        'zaplanowanych treningów',
        'zaplanowanych treningów',
        'zaplanowane treningi',
        'zaplanowanych treningów',
      ],
    ],
    [
      'insights:planned.upcoming',
      [
        'nadchodzących treningów',
        'nadchodzący trening',
        'nadchodzące treningi',
        'nadchodzących treningów',
        'nadchodzących treningów',
        'nadchodzące treningi',
        'nadchodzących treningów',
      ],
    ],
    [
      'insights:planned.missed',
      [
        'niewykonanych treningów',
        'niewykonany trening',
        'niewykonane treningi',
        'niewykonanych treningów',
        'niewykonanych treningów',
        'niewykonane treningi',
        'niewykonanych treningów',
      ],
    ],
    [
      'insights:quality.noDuration',
      ['treningów', 'trening', 'treningi', 'treningów', 'treningów', 'treningi', 'treningów'],
    ],
    [
      'insights:quality.noDistance',
      ['treningów', 'trening', 'treningi', 'treningów', 'treningów', 'treningi', 'treningów'],
    ],
    [
      'insights:quality.estimated',
      ['treningów', 'treningu', 'treningów', 'treningów', 'treningów', 'treningów', 'treningów'],
    ],
    [
      'insights:pace.excluded',
      [
        'spokojnych biegów',
        'spokojny bieg',
        'spokojne biegi',
        'spokojnych biegów',
        'spokojnych biegów',
        'spokojne biegi',
        'spokojnych biegów',
      ],
    ],
    ['insights:summary.unit.days', ['dni', 'dzień', 'dni', 'dni', 'dni', 'dni', 'dni']],
  ])('%s agrees with the count', (key, nouns) => {
    const rendered = counts.map((count) =>
      (i18n.t as (key: string, options: object) => string)(key, {
        count,
        date: 'D',
        runs: 'R',
        suggestions: 'S',
        proposals: 'P',
      }),
    )
    counts.forEach((count, i) => expect(rendered[i], `${key} for ${count}`).toContain(`${count} ${nouns[i]}`))
  })
})

describe('Polish plurals (races and goals)', () => {
  beforeEach(() => setLanguage('pl'))

  const t = i18n.t as (key: string, options: object) => string
  const counts = [0, 1, 2, 5, 12, 22, 25]
  const all = (key: string, options: (count: number) => object) =>
    counts.map((count) => t(key, { count, ...options(count) }))

  it('races: look-ahead days and the signup deadline', () => {
    expect(all('races:interests.when.horizonOption', () => ({}))).toEqual([
      '0 dni',
      '1 dzień',
      '2 dni',
      '5 dni',
      '12 dni',
      '22 dni',
      '25 dni',
    ])
    expect(all('races:details.deadline.closesIn', () => ({ date: 'D' }))).toEqual([
      'D (zamknięcie za 0 dni)',
      'D (zamknięcie za 1 dzień)',
      'D (zamknięcie za 2 dni)',
      'D (zamknięcie za 5 dni)',
      'D (zamknięcie za 12 dni)',
      'D (zamknięcie za 22 dni)',
      'D (zamknięcie za 25 dni)',
    ])
  })

  it('goals: days to go, plan state, skipped suggestions and the weeks warning', () => {
    expect(all('goals:list.daysAway', () => ({}))).toEqual([
      'Pozostało 0 dni',
      'Pozostał 1 dzień',
      'Pozostały 2 dni',
      'Pozostało 5 dni',
      'Pozostało 12 dni',
      'Pozostały 22 dni',
      'Pozostało 25 dni',
    ])
    expect(all('goals:list.planState', () => ({ date: 'D' }))).toEqual([
      'Plan: 0 sugestii, wygenerowano: D',
      'Plan: 1 sugestia, wygenerowano: D',
      'Plan: 2 sugestie, wygenerowano: D',
      'Plan: 5 sugestii, wygenerowano: D',
      'Plan: 12 sugestii, wygenerowano: D',
      'Plan: 22 sugestie, wygenerowano: D',
      'Plan: 25 sugestii, wygenerowano: D',
    ])
    expect(t('goals:plan.dropped', { count: 1 })).toBe('Pominięto 1 sugestię, bo w tym dniu masz już trening.')
    expect(t('goals:plan.dropped', { count: 3 })).toBe('Pominięto 3 sugestie, bo w tych dniach masz już treningi.')
    expect(t('goals:plan.dropped', { count: 7 })).toBe('Pominięto 7 sugestii, bo w tych dniach masz już treningi.')
    expect(all('goals:warnings.tooLittleTime', (count) => ({ value: count }))).toEqual([
      'Do zwykle zalecanego czasu przygotowań do tego dystansu brakuje 0 tygodni.',
      'Do zwykle zalecanego czasu przygotowań do tego dystansu brakuje 1 tygodnia.',
      'Do zwykle zalecanego czasu przygotowań do tego dystansu brakuje 2 tygodni.',
      'Do zwykle zalecanego czasu przygotowań do tego dystansu brakuje 5 tygodni.',
      'Do zwykle zalecanego czasu przygotowań do tego dystansu brakuje 12 tygodni.',
      'Do zwykle zalecanego czasu przygotowań do tego dystansu brakuje 22 tygodni.',
      'Do zwykle zalecanego czasu przygotowań do tego dystansu brakuje 25 tygodni.',
    ])
  })
})
