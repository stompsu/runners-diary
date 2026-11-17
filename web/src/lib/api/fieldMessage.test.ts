import { describe, expect, it } from 'vitest'
import { setLanguage } from '@/i18n/language'
import { genericFieldMessage } from './fieldMessage'

describe('genericFieldMessage', () => {
  it('is undefined in English, so the server text is shown as before', () => {
    expect(genericFieldMessage()).toBeUndefined()
  })

  it('is the Polish message in a Polish UI, never the English server text', async () => {
    await setLanguage('pl')
    expect(genericFieldMessage()).toBe('Ta wartość jest nieprawidłowa.')
  })
})
