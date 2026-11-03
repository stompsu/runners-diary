import { describe, expect, it } from 'vitest'
import i18n from '@/i18n'
import { RUN_TYPES } from '@/test/fakeApi'

describe('i18n', () => {
  it('has a label for every run type code in the catalogue, and a fallback', () => {
    expect(RUN_TYPES).toHaveLength(20)
    for (const { code } of RUN_TYPES) {
      expect([code, i18n.exists(`runTypes:${code}`)]).toEqual([code, true])
    }
    expect(i18n.exists('runTypes:unknown')).toBe(true)
  })

  it('resolves keys from every namespace', () => {
    expect(i18n.t('app.name')).toBe('Runners Diary')
    expect(i18n.t('signIn.tryAsGuest', { ns: 'auth' })).toBe('Try as guest')
    expect(i18n.t('notFoundTitle', { ns: 'errors' })).toBe('Page not found')
    expect(i18n.t('problem.rateLimitedWait', { ns: 'errors', seconds: 12 })).toContain('12')
  })
})
