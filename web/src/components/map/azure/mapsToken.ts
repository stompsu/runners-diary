import { fetchClient } from '@/api/client'

/** Renew this long before the server-side expiry, so a token never dies mid-tile-request. */
const EXPIRY_MARGIN_MS = 5 * 60 * 1000

let cached: { token: string; expiresAtMs: number } | null = null
let inFlight: Promise<string> | null = null

/**
 * The short-lived Azure Maps SAS token from `GET /api/maps/token` (ADR 0022 §5): cached in memory only (never storage) until shortly
 * before it expires, one request at a time. Rejects on any failure, which makes the caller fall back to MapLibre.
 */
export function getMapsToken(now: () => number = Date.now): Promise<string> {
  if (cached && cached.expiresAtMs - now() > EXPIRY_MARGIN_MS) return Promise.resolve(cached.token)
  inFlight ??= fetchToken(now).finally(() => {
    inFlight = null
  })
  return inFlight
}

async function fetchToken(now: () => number): Promise<string> {
  const { data, error } = await fetchClient.GET('/api/maps/token')
  if (error || !data?.token) throw new Error('The map token could not be loaded.')
  const expiresAtMs = Date.parse(data.expiresAt)
  cached = { token: data.token, expiresAtMs: Number.isFinite(expiresAtMs) ? expiresAtMs : now() + EXPIRY_MARGIN_MS * 2 }
  return data.token
}

/** Test helper. */
export function resetMapsTokenCache() {
  cached = null
  inFlight = null
}
