/**
 * `returnTo` is user-controlled (query string), so it is only ever accepted as a same-origin *path* (ADR 0014 §7).
 * Everything else falls back to `/`.
 */
const FALLBACK = '/'

/** Control characters (incl. tab/newline, which browsers strip from URLs) and backslashes (treated as `/`). */
function hasForbiddenChar(value: string): boolean {
  for (const char of value) {
    const code = char.charCodeAt(0)
    if (code <= 0x1f || code === 0x7f || char === '\\') return true
  }
  return false
}

function isSafePath(path: string): boolean {
  if (!path.startsWith('/')) return false
  if (path.startsWith('//')) return false
  return !hasForbiddenChar(path)
}

/** Returns a safe in-app path (path + query + hash) or `/`. Never returns `/signin` (it would loop). */
export function sanitizeReturnTo(raw: string | null | undefined, origin: string = window.location.origin): string {
  if (typeof raw !== 'string' || raw.length === 0 || raw.length > 2048) return FALLBACK
  if (!isSafePath(raw)) return FALLBACK

  // Encoded variants: `/%5Cevil`, `/%2F/evil`, `/%09/evil`... must not decode into something unsafe either.
  let decoded = raw
  for (let i = 0; i < 3; i++) {
    let next: string
    try {
      next = decodeURIComponent(decoded)
    } catch {
      return FALLBACK
    }
    if (next === decoded) break
    decoded = next
    if (!isSafePath(decoded)) return FALLBACK
  }

  // Final check with the platform's URL parser: it must stay on this origin and keep the path we expect.
  let url: URL
  try {
    url = new URL(raw, origin)
  } catch {
    return FALLBACK
  }
  if (url.origin !== origin) return FALLBACK
  if (url.pathname === '/signin' || url.pathname.startsWith('/signin/') || url.pathname === '/redirect.html')
    return FALLBACK
  return url.pathname + url.search + url.hash
}

export function signInPath(options: { returnTo?: string | null; reason?: string | null } = {}): string {
  const params = new URLSearchParams()
  const returnTo = sanitizeReturnTo(options.returnTo)
  if (returnTo !== FALLBACK) params.set('returnTo', returnTo)
  if (options.reason) params.set('reason', options.reason)
  const query = params.toString()
  return query ? `/signin?${query}` : '/signin'
}
