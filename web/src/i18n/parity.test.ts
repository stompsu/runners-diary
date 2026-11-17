import { describe, expect, it } from 'vitest'
import { RELEASED_LANGUAGES } from './language'

/**
 * ADR 0042 §9: the `en` and `pl` namespaces must match. Strict for every `pl` file that exists; once `pl` is in
 * RELEASED_LANGUAGES, all namespaces must exist.
 */
type Json = { [key: string]: Json | string }

const load = (glob: Record<string, unknown>): Map<string, Json> =>
  new Map(Object.entries(glob).map(([path, json]) => [/\/([^/]+)\.json$/.exec(path)![1]!, json as Json]))

const en = load(import.meta.glob('./en/*.json', { eager: true, import: 'default' }))
const pl = load(import.meta.glob('./pl/*.json', { eager: true, import: 'default' }))

/**
 * Keys (`ns:path`, `*` = any segment) whose Polish value may equal the English one: brand, names of languages, units,
 * abbreviations. Add an entry deliberately, with the reason.
 */
const SAME_AS_ENGLISH: readonly RegExp[] = [
  /^common:app\.name$/, // brand
  /^common:language\.names\./, // each language in its own name
  /^[a-z]+:.*\.(acwr|rpe|ultra|fartlek)$/i, // terms used as is in Polish
]

const PLURAL_EN = ['one', 'other']
const PLURAL_PL = ['one', 'few', 'many', 'other']

function flatten(json: Json, prefix = ''): Map<string, string> {
  const out = new Map<string, string>()
  for (const [key, value] of Object.entries(json)) {
    const path = prefix ? `${prefix}.${key}` : key
    if (typeof value === 'string') out.set(path, value)
    else for (const [p, v] of flatten(value, path)) out.set(p, v)
  }
  return out
}

/** `a.b_one`, `a.b_other` -> `a.b` with the set of forms present. */
function groupPlurals(flat: Map<string, string>): Map<string, { forms: string[]; values: string[] }> {
  const groups = new Map<string, { forms: string[]; values: string[] }>()
  for (const [path, value] of flat) {
    const m = /^(.*)_(zero|one|two|few|many|other)$/.exec(path)
    const base = m ? m[1]! : path
    const group = groups.get(base) ?? { forms: [], values: [] }
    group.forms.push(m ? m[2]! : '')
    group.values.push(value)
    groups.set(base, group)
  }
  return groups
}

const variables = (text: string): string[] =>
  [...text.matchAll(/\{\{\s*-?\s*([\w.]+)[^}]*\}\}/g)].map((m) => m[1]!).sort()
const uniqueVariables = (values: string[]): string[] => [...new Set(values.flatMap(variables))].sort()

describe('translation files', () => {
  it('has the 11 English namespaces', () => {
    expect([...en.keys()].sort()).toEqual([
      'auth',
      'calendar',
      'common',
      'errors',
      'goals',
      'insights',
      'maps',
      'races',
      'runTypes',
      'runs',
      'tracks',
    ])
  })

  it('has a pl file only for an en namespace, and all of them once pl is released', () => {
    for (const ns of pl.keys()) expect(en.has(ns), `pl/${ns}.json has no en counterpart`).toBe(true)
    const required = RELEASED_LANGUAGES.includes('pl') ? [...en.keys()] : [...pl.keys()]
    expect([...pl.keys()].sort()).toEqual(required.sort())
  })

  it('has at least pl/common.json (step A)', () => {
    expect(pl.has('common')).toBe(true)
  })

  describe.each([...pl.keys()])('pl/%s.json matches en', (ns) => {
    const enGroups = groupPlurals(flatten(en.get(ns)!))
    const plGroups = groupPlurals(flatten(pl.get(ns)!))

    it('has the same keys (plural groups normalised)', () => {
      expect([...plGroups.keys()].sort()).toEqual([...enGroups.keys()].sort())
    })

    it('uses the same interpolation variables for every key', () => {
      const mismatches: string[] = []
      for (const [base, group] of enGroups) {
        const other = plGroups.get(base)
        if (!other) continue
        const expected = uniqueVariables(group.values)
        // A plural form may leave out `count` ("Jeden bieg"), as English does for "1 run": compare per key, not per form.
        const actual = uniqueVariables(other.values)
        if (JSON.stringify(expected) !== JSON.stringify(actual)) {
          mismatches.push(`${ns}:${base} en {${expected}} pl {${actual}}`)
        }
      }
      expect(mismatches).toEqual([])
    })

    it('uses _one/_few/_many/_other wherever en has _one/_other, and no other form', () => {
      const problems: string[] = []
      for (const [base, group] of enGroups) {
        const other = plGroups.get(base)
        if (!other) continue
        const enForms = group.forms.filter(Boolean).sort()
        const plForms = other.forms.filter(Boolean).sort()
        if (enForms.length === 0) {
          if (plForms.length > 0) problems.push(`${ns}:${base} is plural in pl only`)
        } else if (JSON.stringify(enForms) === JSON.stringify([...PLURAL_EN].sort())) {
          if (JSON.stringify(plForms) !== JSON.stringify([...PLURAL_PL].sort()))
            problems.push(`${ns}:${base} needs ${PLURAL_PL.join('/')}, has ${plForms.join('/')}`)
        } else if (JSON.stringify(enForms) !== JSON.stringify(plForms)) {
          problems.push(`${ns}:${base} plural forms differ (en ${enForms}, pl ${plForms})`)
        }
      }
      expect(problems).toEqual([])
    })

    it('has no empty values', () => {
      const empty = [...flatten(pl.get(ns)!)].filter(([, value]) => value.trim() === '').map(([path]) => path)
      expect(empty).toEqual([])
    })

    it('has no value identical to English outside the allowlist', () => {
      const enFlat = flatten(en.get(ns)!)
      const same = [...flatten(pl.get(ns)!)]
        .filter(([path, value]) => enFlat.get(path) === value)
        .map(([path]) => `${ns}:${path}`)
        .filter((key) => !SAME_AS_ENGLISH.some((pattern) => pattern.test(key)))
      expect(same).toEqual([])
    })
  })
})

describe('the parity checks themselves', () => {
  it('flag a missing Polish plural form', () => {
    const groups = groupPlurals(flatten({ runs_one: 'x', runs_other: 'y' }))
    expect(groups.get('runs')!.forms.sort()).toEqual(PLURAL_EN.sort())
  })

  it('read interpolation variables with options and nesting spaces', () => {
    expect(variables('{{count}} z {{ total }} {{- name}} {{date, datetime}}')).toEqual([
      'count',
      'date',
      'name',
      'total',
    ])
  })
})
