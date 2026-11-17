import i18n from '@/i18n'

/**
 * Test-only missing-key detection (ADR 0042 §9). With `fallbackLng` on, i18next silently shows English for a missing
 * Polish key, so tests turn the fallback off: a missing key renders as its key and is recorded here. `test/setup.ts`
 * fails the test afterwards. English is complete by construction (typed keys), so this mostly guards `pl`.
 */
export const missingKeys: string[] = []

export function installMissingKeyDetection(): void {
  i18n.options.fallbackLng = false as never
  i18n.options.saveMissing = true
  i18n.options.missingKeyHandler = (languages, namespace, key) => {
    // A namespace with no Polish file yet is Part B work still to come, not a missing key (the parity test requires
    // all of them once `pl` is released); from the moment `pl/<ns>.json` exists, every gap in it fails the test.
    if (languages.every((language) => language !== 'en') && !i18n.hasResourceBundle('pl', namespace)) return
    missingKeys.push(`${languages.join(',')}:${namespace}:${key}`)
  }
}

/** For a test that deliberately renders a gap on purpose. */
export function ignoreMissingKeys(): void {
  missingKeys.length = 0
}
