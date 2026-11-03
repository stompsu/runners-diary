/** Guest token persistence (ADR 0014 §4): localStorage `rd.guestSession`, discarded once expired. */
export const GUEST_STORAGE_KEY = 'rd.guestSession'

export interface StoredGuestSession {
  accessToken: string
  /** epoch ms */
  expiresAt: number
  userId: string
}

function storage(): Storage | null {
  try {
    return window.localStorage
  } catch {
    return null
  }
}

export function clearGuestSession(): void {
  try {
    storage()?.removeItem(GUEST_STORAGE_KEY)
  } catch {
    // storage unavailable: nothing to clear
  }
}

export function writeGuestSession(session: StoredGuestSession): void {
  try {
    storage()?.setItem(GUEST_STORAGE_KEY, JSON.stringify(session))
  } catch {
    // Private mode / quota: the session still works in memory until reload.
  }
}

/** Returns the stored session, or null when absent, malformed or expired (an expired entry is removed). */
export function readGuestSession(now: number = Date.now()): StoredGuestSession | null {
  let raw: string | null = null
  try {
    raw = storage()?.getItem(GUEST_STORAGE_KEY) ?? null
  } catch {
    return null
  }
  if (!raw) return null
  try {
    const value: unknown = JSON.parse(raw)
    if (
      typeof value === 'object' &&
      value !== null &&
      'accessToken' in value &&
      'expiresAt' in value &&
      'userId' in value &&
      typeof value.accessToken === 'string' &&
      value.accessToken.length > 0 &&
      typeof value.expiresAt === 'number' &&
      Number.isFinite(value.expiresAt) &&
      typeof value.userId === 'string' &&
      value.expiresAt > now
    ) {
      return { accessToken: value.accessToken, expiresAt: value.expiresAt, userId: value.userId }
    }
  } catch {
    // fall through: malformed JSON is discarded
  }
  clearGuestSession()
  return null
}
