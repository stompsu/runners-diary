import { describe, expect, it } from 'vitest'
import { safeHttpUrl } from './safeUrl'

describe('safeHttpUrl', () => {
  it('accepts absolute http and https links', () => {
    expect(safeHttpUrl('https://example.org/results?id=1')).toBe('https://example.org/results?id=1')
    expect(safeHttpUrl('http://example.org')).toBe('http://example.org/')
  })

  it.each([
    'javascript:alert(1)',
    'data:text/html,hi',
    'ftp://example.org',
    '/relative',
    'example.org',
    '',
    null,
    undefined,
  ])('drops %j', (value) => expect(safeHttpUrl(value)).toBeNull())
})
