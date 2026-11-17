import i18n from 'i18next'

/** UI languages the code knows (bare ISO 639-1 tags, ADR 0042 §1). `en` is the fallback for every missing key. */
export const SUPPORTED_LANGUAGES = ['en', 'pl'] as const
export type Language = (typeof SUPPORTED_LANGUAGES)[number]

/** Languages offered by detection and the switcher (ADR 0042 §8). `pl` was released with its translations (M10 B5). */
export const RELEASED_LANGUAGES: readonly Language[] = ['en', 'pl']

/**
 * Whether the language is shown to the user: the released ones, plus every supported one in dev and tests (so a
 * language still being translated can be tried before it is released; today the two lists are equal).
 */
export function releasedLanguages(dev: boolean = import.meta.env.DEV): readonly Language[] {
  return dev ? SUPPORTED_LANGUAGES : RELEASED_LANGUAGES
}

/** Languages the AI summary and plan can be requested in. A UI language outside the list sends `en`. */
export const AI_LANGUAGES: readonly Language[] = ['en', 'pl']

/** The `lang` to send to the AI endpoints for a UI language. */
export function aiLanguage(lng: string): Language {
  const language = toLanguage(lng)
  return language !== null && AI_LANGUAGES.includes(language) ? language : 'en'
}

export const LANGUAGE_STORAGE_KEY = 'rd.lang'

export function toLanguage(value: string | null | undefined): Language | null {
  return SUPPORTED_LANGUAGES.find((language) => language === value) ?? null
}

/** `rd.lang`, then the first of `navigator.languages` whose primary subtag is released, then `en` (ADR 0042 §2). */
export function resolveInitialLanguage(
  stored: string | null,
  navigatorLanguages: readonly string[],
  released: readonly Language[] = releasedLanguages(),
): Language {
  const fromStorage = toLanguage(stored)
  if (fromStorage && released.includes(fromStorage)) return fromStorage
  for (const tag of navigatorLanguages) {
    const primary = toLanguage(tag.toLowerCase().split('-')[0])
    if (primary && released.includes(primary)) return primary
  }
  return 'en'
}

export function readStoredLanguage(): string | null {
  try {
    return window.localStorage.getItem(LANGUAGE_STORAGE_KEY)
  } catch {
    return null
  }
}

function writeStoredLanguage(language: Language): void {
  try {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, language)
  } catch {
    // Private mode / blocked storage: the choice still applies for this page load.
  }
}

/**
 * Polish eager namespaces are one lazy chunk (ADR 0042 §4): English users never download it. Which files exist is
 * decided by the glob in ./i18n-pl-eager.ts, so adding `pl/<ns>.json` needs no change here.
 */
export async function ensureLanguage(language: Language): Promise<void> {
  if (language === 'pl' && !i18n.hasResourceBundle('pl', 'common')) {
    const { polishEager } = await import('./i18n-pl-eager')
    for (const [ns, bundle] of Object.entries(polishEager)) i18n.addResourceBundle('pl', ns, bundle, true, true)
  }
}

async function applyLanguage(language: Language): Promise<void> {
  await ensureLanguage(language)
  await i18n.changeLanguage(language)
  document.documentElement.lang = language
}

/** Resolves and applies the language; `main.tsx` awaits it before the first render, so there is no flash. */
export async function initLanguage(): Promise<void> {
  const languages = typeof navigator === 'undefined' ? [] : navigator.languages
  await applyLanguage(resolveInitialLanguage(readStoredLanguage(), languages))
}

/**
 * An explicit choice: loads the bundle, re-renders without reload, updates `<html lang>` and `rd.lang`. Saving it to
 * the profile is the caller's job (`useChangeLanguage`), because this module must not depend on the API client.
 */
export async function setLanguage(language: Language): Promise<void> {
  await applyLanguage(language)
  writeStoredLanguage(language)
}

let explicitChoice = false

/** Whether the user picked a language in this page load: that choice beats the profile read (it may be older). */
export function hasExplicitLanguageChoice(): boolean {
  return explicitChoice
}

export function markExplicitLanguageChoice(made: boolean): void {
  explicitChoice = made
}

/** The signed-in profile wins over `rd.lang` (ADR 0042 §2). Null, unknown or unreleased values change nothing. */
export async function applyProfileLanguage(profile: string | null): Promise<void> {
  const language = toLanguage(profile)
  if (!language || !releasedLanguages().includes(language)) return
  if (language === i18n.language) {
    writeStoredLanguage(language)
    return
  }
  await setLanguage(language)
}
