import { describe, expect, it } from 'vitest'
import { sanitizeReturnTo, signInPath } from './returnTo'

const ORIGIN = 'http://localhost:5173'
const clean = (raw: string | null | undefined) => sanitizeReturnTo(raw, ORIGIN)

describe('sanitizeReturnTo', () => {
  it.each([
    '/',
    '/diary',
    '/diary/2026/11',
    '/diary/2026/11?x=1',
    '/diary/2026/11/day/2026-11-14#top',
    '/insights?range=30d&q=a%20b',
  ])('accepts the same-origin path %s unchanged', (path) => {
    expect(clean(path)).toBe(path)
  })

  it.each([
    ['protocol-relative', '//evil.example'],
    ['protocol-relative with path', '//evil.example/diary'],
    ['slash-backslash', '/\\evil.example'],
    ['backslash anywhere', '/diary\\..\\evil'],
    ['absolute https', 'https://evil.example/'],
    ['absolute http', 'http://localhost:5173/diary'],
    ['javascript scheme', 'javascript:alert(1)'],
    ['data scheme', 'data:text/html,<script>alert(1)</script>'],
    ['relative path', 'diary/2026/11'],
    ['empty', ''],
    ['encoded backslash', '/%5Cevil.example'],
    ['encoded slash after slash', '/%2Fevil.example'],
    ['double-encoded slash', '/%252Fevil.example'],
    ['encoded tab', '/%09/evil.example'],
    ['encoded newline', '/%0A/evil.example'],
    ['raw tab', '/\t/evil.example'],
    ['raw newline', '/\n/evil.example'],
    ['malformed encoding', '/%E0%A4%A'],
    ['the sign-in page itself (redirect loop)', '/signin?returnTo=/diary'],
    ['the MSAL bridge page', '/redirect.html'],
  ])('rejects %s', (_label, raw) => {
    expect(clean(raw)).toBe('/')
  })

  it('rejects null, undefined and absurdly long values', () => {
    expect(clean(null)).toBe('/')
    expect(clean(undefined)).toBe('/')
    expect(clean(`/${'a'.repeat(3000)}`)).toBe('/')
  })
})

describe('signInPath', () => {
  it('omits returnTo for the root and unsafe values', () => {
    expect(signInPath()).toBe('/signin')
    expect(signInPath({ returnTo: '/' })).toBe('/signin')
    expect(signInPath({ returnTo: '//evil.example' })).toBe('/signin')
  })

  it('encodes returnTo and adds the reason', () => {
    expect(signInPath({ returnTo: '/diary/2026/11?x=1', reason: 'expired' })).toBe(
      '/signin?returnTo=%2Fdiary%2F2026%2F11%3Fx%3D1&reason=expired',
    )
  })
})
