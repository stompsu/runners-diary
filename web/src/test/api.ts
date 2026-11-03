import { vi } from 'vitest'

export function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

/** Answers one path with `respond()` (a Response, or a thrown error); everything else still goes to the fake API. */
export function overrideEndpoint(path: string, respond: () => Response | Promise<Response>): void {
  const fallback = vi.mocked(fetch).getMockImplementation()
  vi.mocked(fetch).mockImplementation((input, init) => {
    const request = input instanceof Request ? input : new Request(input, init)
    if (new URL(request.url).pathname === path) return Promise.resolve().then(respond)
    return fallback ? fallback(input, init) : Promise.reject(new Error('no fetch fallback'))
  })
}
