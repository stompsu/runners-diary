import { describe, expect, it } from 'vitest'
import i18n from '@/i18n'
import { setLanguage } from '@/i18n/language'
import { genericFieldMessage } from './fieldMessage'

describe('genericFieldMessage', () => {
  it('is undefined in English, so the server text is shown as before', () => {
    expect(genericFieldMessage()).toBeUndefined()
  })

  it('is the localised key in another language (a key of errors:problem, not the English text)', async () => {
    await setLanguage('pl')
    // errors has no Polish file until Part B: the key itself is the message, never the English server text.
    expect(genericFieldMessage()).toBe('problem.fieldInvalid')
    i18n.addResourceBundle('pl', 'errors', { problem: { fieldInvalid: 'Ta wartość jest nieprawidłowa.' } }, true, true)
    try {
      expect(genericFieldMessage()).toBe('Ta wartość jest nieprawidłowa.')
    } finally {
      i18n.removeResourceBundle('pl', 'errors')
    }
  })
})
