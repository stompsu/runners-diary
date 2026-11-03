/**
 * Injectable faults for the fake API (ADR 0019). A fault matches requests by method + path (string or RegExp) and
 * either delays them, answers with a canned response, or fails like a network error. `times` limits how often it fires.
 */
export interface FaultMatcher {
  method?: string
  path: string | RegExp
}

export type FaultAction =
  | { type: 'delay'; ms: number }
  | { type: 'status'; status: number; retryAfterSeconds?: number; problemType?: string }
  | { type: 'html'; status: number }
  | { type: 'network' }

export interface Fault {
  match: FaultMatcher
  action: FaultAction
  /** Remaining activations; undefined = unlimited. */
  times?: number
}

export function matches(fault: Fault, method: string, path: string): boolean {
  if (fault.match.method && fault.match.method.toUpperCase() !== method.toUpperCase()) return false
  return typeof fault.match.path === 'string' ? fault.match.path === path : fault.match.path.test(path)
}

export const faults = {
  delay: (match: FaultMatcher, ms: number, times?: number): Fault => ({ match, action: { type: 'delay', ms }, times }),
  rateLimited: (match: FaultMatcher, retryAfterSeconds = 60, times?: number): Fault => ({
    match,
    action: {
      type: 'status',
      status: 429,
      retryAfterSeconds,
      problemType: 'urn:runnersdiary:problem:rate-limited',
    },
    times,
  }),
  guestCapacity: (match: FaultMatcher, retryAfterSeconds = 30, times?: number): Fault => ({
    match,
    action: {
      type: 'status',
      status: 503,
      retryAfterSeconds,
      problemType: 'urn:runnersdiary:problem:guest-capacity',
    },
    times,
  }),
  status: (match: FaultMatcher, status: number, times?: number): Fault => ({
    match,
    action: { type: 'status', status },
    times,
  }),
  /** Proxy-style 502/504 with an HTML body (cold start behind the reverse proxy). */
  html: (match: FaultMatcher, status = 502, times?: number): Fault => ({
    match,
    action: { type: 'html', status },
    times,
  }),
  network: (match: FaultMatcher, times?: number): Fault => ({ match, action: { type: 'network' }, times }),
}
