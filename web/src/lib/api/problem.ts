/**
 * Normalises every API failure into one discriminated union (ADR 0018 §3, ADR 0014 §6).
 * openapi-react-query throws the parsed error *body* (headers are lost), so all inputs are `unknown`:
 * a problem+json object, the synthetic `{ status }` object from the client middleware, a string/HTML body,
 * or a thrown `TypeError` (network failure). Pass the `Response` too when you have it, to read `Retry-After`.
 */

export const PROBLEM_TYPES = {
  concurrency: 'urn:runnersdiary:problem:concurrency-conflict',
  runLimit: 'urn:runnersdiary:problem:run-limit',
  rateLimited: 'urn:runnersdiary:problem:rate-limited',
  guestCapacity: 'urn:runnersdiary:problem:guest-capacity',
} as const

export type FieldErrors = Record<string, string[]>

export type ApiProblem =
  | { kind: 'validation'; status: 400; fieldErrors: FieldErrors }
  | { kind: 'concurrency'; status: 409 }
  | { kind: 'runLimit'; status: 409 }
  | { kind: 'rateLimited'; status: 429; retryAfterSeconds: number | null }
  | { kind: 'guestCapacity'; status: 503; retryAfterSeconds: number | null }
  | { kind: 'notFound'; status: 404 }
  | { kind: 'unauthorized'; status: 401 }
  | { kind: 'payloadTooLarge'; status: 413 }
  | { kind: 'unavailable'; status: number | null; retryAfterSeconds: number | null }
  | { kind: 'unknown'; status: number | null }

export type ApiProblemKind = ApiProblem['kind']

/** `Retry-After` as delay-seconds or an HTTP-date (relative to `now`); null when absent/invalid. */
export function parseRetryAfter(value: string | null | undefined, now: number = Date.now()): number | null {
  if (!value) return null
  const trimmed = value.trim()
  if (/^\d+$/.test(trimmed)) return Number(trimmed)
  if (/^-?\d+$/.test(trimmed)) return null
  const at = Date.parse(trimmed)
  if (Number.isNaN(at)) return null
  return Math.max(0, Math.ceil((at - now) / 1000))
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}

function readFieldErrors(value: unknown): FieldErrors {
  if (!isRecord(value)) return {}
  const result: FieldErrors = {}
  for (const [key, messages] of Object.entries(value)) {
    if (Array.isArray(messages)) result[key] = messages.filter((m): m is string => typeof m === 'string')
    else if (typeof messages === 'string') result[key] = [messages]
  }
  return result
}

/**
 * @param body the parsed error body (object, string, undefined, ...)
 * @param response optional, used for the status when the body has none and for `Retry-After`
 * @param now injectable clock for HTTP-date `Retry-After`
 */
export function parseProblem(body: unknown, response?: Pick<Response, 'status' | 'headers'>, now?: number): ApiProblem {
  if (body instanceof TypeError) return { kind: 'unavailable', status: null, retryAfterSeconds: null }

  const record = isRecord(body) ? body : {}
  const status = response?.status ?? (typeof record.status === 'number' ? record.status : null)
  const type = typeof record.type === 'string' ? record.type : null
  // Hook consumers only see the error body, so the client middleware embeds `Retry-After` as `retryAfterSeconds`.
  const embedded = typeof record.retryAfterSeconds === 'number' ? record.retryAfterSeconds : null
  const retryAfterSeconds = parseRetryAfter(response?.headers.get('Retry-After'), now) ?? embedded

  if (type === PROBLEM_TYPES.concurrency) return { kind: 'concurrency', status: 409 }
  if (type === PROBLEM_TYPES.runLimit) return { kind: 'runLimit', status: 409 }
  if (type === PROBLEM_TYPES.rateLimited) return { kind: 'rateLimited', status: 429, retryAfterSeconds }
  if (type === PROBLEM_TYPES.guestCapacity) return { kind: 'guestCapacity', status: 503, retryAfterSeconds }

  switch (status) {
    case 400:
      return { kind: 'validation', status: 400, fieldErrors: readFieldErrors(record.errors) }
    case 401:
      return { kind: 'unauthorized', status: 401 }
    case 404:
      return { kind: 'notFound', status: 404 }
    case 413:
      return { kind: 'payloadTooLarge', status: 413 }
    case 429:
      return { kind: 'rateLimited', status: 429, retryAfterSeconds }
    case 502:
    case 503:
    case 504:
      return { kind: 'unavailable', status, retryAfterSeconds }
    default:
      return { kind: 'unknown', status }
  }
}

/**
 * Field-error keys are camelCase JSON paths. `keyMap` renames wire keys to form fields, `isField` says whether the
 * result is a real form field. Everything else (including `body`) goes to the form-level summary.
 */
export function mapFieldErrors(
  fieldErrors: FieldErrors,
  options: { keyMap?: Readonly<Record<string, string>>; isField: (name: string) => boolean },
): { fields: Record<string, string>; summary: string[] } {
  const fields: Record<string, string> = {}
  const summary: string[] = []
  for (const [key, messages] of Object.entries(fieldErrors)) {
    const name = options.keyMap?.[key] ?? key
    const text = messages.join(' ')
    if (options.isField(name)) fields[name] = fields[name] ? `${fields[name]} ${text}` : text
    else summary.push(text)
  }
  return { fields, summary }
}

/** TanStack Query retry predicate: only network failures and 502/503/504 (ADR 0014; plan §3.3). */
export function isRetryable(error: unknown): boolean {
  if (error instanceof TypeError) return true
  if (isRecord(error) && typeof error.status === 'number') return [502, 503, 504].includes(error.status)
  return false
}
