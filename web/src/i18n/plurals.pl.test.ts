import { beforeEach, describe, expect, it } from 'vitest'
import i18n from '@/i18n'
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
