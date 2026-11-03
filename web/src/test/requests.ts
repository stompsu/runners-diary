import { vi } from 'vitest'
import { getFakeApi } from './fake'

export interface RecordedRequest {
  method: string
  path: string
  /** Parsed JSON body, if any. */
  body?: Record<string, unknown>
}

/** Wraps the current fetch stub so tests can inspect what the UI sent (method, path, JSON body). */
export function recordRequests(): RecordedRequest[] {
  const recorded: RecordedRequest[] = []
  const inner = vi.mocked(fetch).getMockImplementation()
  vi.mocked(fetch).mockImplementation(async (input, init) => {
    const request = input instanceof Request ? input : new Request(input, init)
    const text = ['GET', 'HEAD', 'DELETE'].includes(request.method) ? '' : await request.clone().text()
    recorded.push({
      method: request.method,
      path: new URL(request.url).pathname,
      body: text ? (JSON.parse(text) as Record<string, unknown>) : undefined,
    })
    return inner ? inner(input, init) : getFakeApi().handle(request)
  })
  return recorded
}

/** Answers the next `method path` request with a canned problem response; everything else goes on to the fake. */
export function respondOnce(
  method: string,
  path: string | RegExp,
  status: number,
  body: Record<string, unknown>,
): void {
  const inner = vi.mocked(fetch).getMockImplementation()
  let used = false
  vi.mocked(fetch).mockImplementation((input, init) => {
    const request = input instanceof Request ? input : new Request(input, init)
    const requestPath = new URL(request.url).pathname
    const matched = typeof path === 'string' ? requestPath === path : path.test(requestPath)
    if (!used && matched && request.method === method) {
      used = true
      return Promise.resolve(
        new Response(JSON.stringify({ status, ...body }), {
          status,
          headers: { 'Content-Type': 'application/problem+json' },
        }),
      )
    }
    return inner ? inner(input, init) : getFakeApi().handle(request)
  })
}

/** The runs the fake holds for the signed-in guest (token from the stored guest session). */
export function storedRuns() {
  const session = JSON.parse(localStorage.getItem('rd.guestSession') ?? 'null') as { accessToken: string } | null
  if (!session) throw new Error('No guest session')
  return getFakeApi().runsOf(session.accessToken)
}

/** Changes a run behind the UI's back (another tab / device), bumping its version. */
export async function editElsewhere(id: string, patch: Record<string, unknown>): Promise<void> {
  const session = JSON.parse(localStorage.getItem('rd.guestSession')!) as { accessToken: string }
  const headers = { Authorization: `Bearer ${session.accessToken}`, 'Content-Type': 'application/json' }
  const fake = getFakeApi()
  const current = (await (
    await fake.handle(new Request(`http://localhost/api/runs/${id}`, { headers }))
  ).json()) as Record<string, unknown>
  const { id: _id, startUtc: _u, tracks: _t, createdAt: _c, updatedAt: _a, ...writable } = current
  const response = await fake.handle(
    new Request(`http://localhost/api/runs/${id}`, {
      method: 'PUT',
      headers,
      body: JSON.stringify({ ...writable, ...patch }),
    }),
  )
  if (!response.ok) throw new Error(`editElsewhere failed: ${response.status}`)
}

/** Deletes a run behind the UI's back. */
export async function deleteElsewhere(id: string): Promise<void> {
  const session = JSON.parse(localStorage.getItem('rd.guestSession')!) as { accessToken: string }
  const response = await getFakeApi().handle(
    new Request(`http://localhost/api/runs/${id}`, {
      method: 'DELETE',
      headers: { Authorization: `Bearer ${session.accessToken}` },
    }),
  )
  if (response.status !== 204) throw new Error(`deleteElsewhere failed: ${response.status}`)
}
