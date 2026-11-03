import createFetchClient, { type Middleware } from 'openapi-fetch'
import createClient from 'openapi-react-query'
import { bearerMatchesSession, getAccessToken, getSession, signOut } from '@/auth/session'
import { parseRetryAfter } from '@/lib/api/problem'
import type { paths } from './generated/schema'

/**
 * The only way the app talks to the API (CLAUDE.md). Requests always go to the page's own origin
 * (`/api/...`), so the same build works behind the Vite proxy and behind the production reverse proxy.
 * The origin is spelled out because `Request` needs an absolute URL outside browsers (tests).
 */
export const fetchClient = createFetchClient<paths>({ baseUrl: window.location.origin })

/** Calls that never carry a token (ADR 0014 §6). */
const ANONYMOUS_ENDPOINTS: ReadonlyArray<{ method: string; path: string }> = [
  { method: 'POST', path: '/api/guest-sessions' },
  { method: 'GET', path: '/api/client-config' },
  { method: 'GET', path: '/api/health' },
]

export function isAnonymousEndpoint(method: string, url: string): boolean {
  const { pathname } = new URL(url, window.location.origin)
  return ANONYMOUS_ENDPOINTS.some((e) => e.method === method.toUpperCase() && e.path === pathname)
}

/**
 * Auth + error-shape middleware, the only place that touches tokens.
 * - onRequest: bearer token from the session store (guest token or MSAL silent token).
 * - onResponse: a 401 on an authenticated request ends the session (the guard sends the user to /signin and
 *   Providers clears the query cache); failed responses get one error shape: a problem+json object, or `{ status }`
 *   for HTML/empty bodies (proxy 502/504), plus `retryAfterSeconds` from the `Retry-After` header.
 */
export const authMiddleware: Middleware = {
  async onRequest({ request }) {
    if (isAnonymousEndpoint(request.method, request.url)) return undefined
    const token = await getAccessToken()
    if (token) request.headers.set('Authorization', `Bearer ${token}`)
    return request
  },

  async onResponse({ request, response }) {
    if (response.status === 401 && bearerMatchesSession(request.headers.get('Authorization'))) {
      const session = getSession()
      if (session.kind === 'guest') signOut('expired')
      else if (session.kind === 'entra') signOut('sessionEnded')
    }
    if (response.ok || response.status < 400) return undefined
    return normaliseError(response)
  },
}

async function normaliseError(response: Response): Promise<Response> {
  const retryAfterSeconds = parseRetryAfter(response.headers.get('Retry-After'))
  const text = await response.text()
  let body: Record<string, unknown> = {}
  try {
    const parsed: unknown = JSON.parse(text)
    if (typeof parsed === 'object' && parsed !== null && !Array.isArray(parsed))
      body = parsed as Record<string, unknown>
  } catch {
    // not JSON (HTML from a proxy, empty body): synthetic problem below
  }
  if (typeof body.status !== 'number') body = { ...body, status: response.status }
  if (retryAfterSeconds !== null) body = { ...body, retryAfterSeconds }

  const headers = new Headers(response.headers)
  if (!headers.get('Content-Type')?.includes('json')) headers.set('Content-Type', 'application/problem+json')
  headers.delete('Content-Length')
  return new Response(JSON.stringify(body), { status: response.status, statusText: response.statusText, headers })
}

fetchClient.use(authMiddleware)

export const $api = createClient(fetchClient)
