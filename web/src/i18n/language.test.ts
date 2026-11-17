import { afterEach, describe, expect, it, vi } from 'vitest'
import i18n from '@/i18n'
import {
  aiLanguage,
  applyProfileLanguage,
  initLanguage,
  LANGUAGE_STORAGE_KEY,
  releasedLanguages,
  resolveInitialLanguage,
  setLanguage,
} from './language'

const BOTH = ['en', 'pl'] as const
const EN_ONLY = ['en'] as const

describe('resolveInitialLanguage (ADR 0042 §2)', () => {
  it.each([
    ['a valid rd.lang wins over the browser', 'pl', ['en-GB'], 'pl'],
    ['a valid rd.lang of en wins over a Polish browser', 'en', ['pl-PL'], 'en'],
    ['an invalid rd.lang is ignored', 'de', ['pl'], 'pl'],
    ['no rd.lang, pl-PL browser', null, ['pl-PL'], 'pl'],
    ['the first released match counts', null, ['de', 'pl'], 'pl'],
    ['the first match decides, even when later ones differ', null, ['en-GB', 'pl'], 'en'],
    ['case is ignored', null, ['PL-pl'], 'pl'],
    ['en-GB', null, ['en-GB'], 'en'],
    ['an empty list falls back to en', null, [], 'en'],
    ['unsupported languages fall back to en', null, ['de-DE', 'fr'], 'en'],
  ])('%s', (_name, stored, navigatorLanguages, expected) => {
    expect(resolveInitialLanguage(stored, navigatorLanguages, BOTH)).toBe(expected)
  })

  it('ignores an unreleased language in rd.lang and in the browser list', () => {
    expect(resolveInitialLanguage('pl', ['pl-PL'], EN_ONLY)).toBe('en')
    expect(resolveInitialLanguage(null, ['pl', 'en'], EN_ONLY)).toBe('en')
  })

  it('releases pl in production builds too (M10 B5), and AI texts stay English until Part C', () => {
    expect(releasedLanguages(false)).toEqual(['en', 'pl'])
    expect(releasedLanguages(true)).toEqual(['en', 'pl'])
  })
})

describe('applying a language', () => {
  afterEach(() => vi.restoreAllMocks())

  it('initLanguage reads rd.lang before the browser and sets <html lang> and the Polish strings', async () => {
    localStorage.setItem(LANGUAGE_STORAGE_KEY, 'pl')

    await initLanguage()

    expect(i18n.language).toBe('pl')
    expect(document.documentElement.lang).toBe('pl')
    expect(i18n.t('nav.diary')).toBe('Dziennik')
  })

  it('initLanguage uses navigator.languages without rd.lang', async () => {
    vi.spyOn(navigator, 'languages', 'get').mockReturnValue(['de-DE', 'pl-PL'])

    await initLanguage()

    expect(i18n.language).toBe('pl')
    // A detected language is not a choice: nothing is written until the user picks one.
    expect(localStorage.getItem(LANGUAGE_STORAGE_KEY)).toBeNull()
  })

  it('initLanguage stays English for an English browser, without loading Polish', async () => {
    vi.spyOn(navigator, 'languages', 'get').mockReturnValue(['en-GB'])
    const add = vi.spyOn(i18n, 'addResourceBundle')

    await initLanguage()

    expect(i18n.language).toBe('en')
    expect(document.documentElement.lang).toBe('en')
    expect(add).not.toHaveBeenCalledWith(
      'pl',
      expect.anything(),
      expect.anything(),
      expect.anything(),
      expect.anything(),
    )
  })

  it('setLanguage switches without reload, updates <html lang> and stores the choice', async () => {
    await setLanguage('pl')
    expect([i18n.language, document.documentElement.lang, localStorage.getItem(LANGUAGE_STORAGE_KEY)]).toEqual([
      'pl',
      'pl',
      'pl',
    ])

    await setLanguage('en')
    expect([i18n.language, document.documentElement.lang, localStorage.getItem(LANGUAGE_STORAGE_KEY)]).toEqual([
      'en',
      'en',
      'en',
    ])
  })

  it('survives unavailable localStorage (reads and writes)', async () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new DOMException('denied', 'SecurityError')
    })
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new DOMException('denied', 'SecurityError')
    })

    await initLanguage()
    await setLanguage('pl')

    expect(i18n.language).toBe('pl')
  })

  it('the profile language wins over rd.lang and updates it', async () => {
    localStorage.setItem(LANGUAGE_STORAGE_KEY, 'en')

    await applyProfileLanguage('pl')

    expect([i18n.language, localStorage.getItem(LANGUAGE_STORAGE_KEY)]).toEqual(['pl', 'pl'])
  })

  it('a null, unknown or equal profile language changes nothing', async () => {
    await applyProfileLanguage(null)
    await applyProfileLanguage('de')
    expect(i18n.language).toBe('en')

    await applyProfileLanguage('en')
    expect([i18n.language, localStorage.getItem(LANGUAGE_STORAGE_KEY)]).toEqual(['en', 'en'])
  })
})

describe('aiLanguage', () => {
  it('sends en until Part C releases Polish AI texts', () => {
    expect(aiLanguage('pl')).toBe('en')
    expect(aiLanguage('en')).toBe('en')
    expect(aiLanguage('xx')).toBe('en')
  })
})
