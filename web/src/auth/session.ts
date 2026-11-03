import { useSyncExternalStore } from 'react'
import { clearGuestSession, GUEST_STORAGE_KEY, readGuestSession, writeGuestSession } from './guestSession'

/**
 * The single auth session store (ADR 0014 §5): who is signed in and how to get a bearer token for the API.
 * A tiny external store read with `useSyncExternalStore`; components never see tokens.
 */
export type SignOutReason = 'expired' | 'sessionEnded' | 'signedOut'
export const SIGN_OUT_REASONS: readonly SignOutReason[] = ['expired', 'sessionEnded', 'signedOut']

export function isSignOutReason(value: unknown): value is SignOutReason {
  return typeof value === 'string' && (SIGN_OUT_REASONS as readonly string[]).includes(value)
}

export type Session =
  /** Entra sign-in is being restored or completed (redirect response); the guard waits instead of bouncing. */
  | { kind: 'restoring' }
  | { kind: 'anonymous'; reason: SignOutReason | null }
  | { kind: 'guest'; userId: string; expiresAt: number }
  | { kind: 'entra'; name: string | null }

/** What the lazily loaded MSAL module provides (kept free of MSAL types so this module stays in the main chunk). */
export interface EntraController {
  /** Silent token for the API scope; null when the session cannot be renewed silently. Network failures throw. */
  acquireToken(): Promise<string | null>
  /** Starts the redirect flow; resolves only if the redirect could not be started. */
  login(): Promise<void>
  /** Clears the MSAL cache and signs out at the IdP (navigates away). */
  logout(): Promise<void>
}

export interface GuestSessionInput {
  accessToken: string
  /** ISO date-time or epoch ms (epoch ms is taken as already on the client clock). */
  expiresAt: string | number
  userId: string
  /** The response's `Date` header; lets the expiry be corrected for a skewed client clock. */
  serverDate?: string | null
}

/**
 * The guest expiry on the *client* clock. A server timestamp compared with a skewed `Date.now()` would expire the
 * session too early or too late, so the remaining lifetime is measured on the server clock (`expiresAt - serverNow`)
 * and applied to the client's. Without a usable `Date` header the raw timestamp is used. NaN when `expiresAt` is
 * malformed.
 */
export function clientSideExpiry(
  expiresAt: string | number,
  serverDate?: string | null,
  clientNow = Date.now(),
): number {
  if (typeof expiresAt === 'number') return Number.isFinite(expiresAt) ? expiresAt : Number.NaN
  const expires = Date.parse(expiresAt)
  if (!Number.isFinite(expires)) return Number.NaN
  const serverNow = serverDate ? Date.parse(serverDate) : Number.NaN
  return Number.isFinite(serverNow) ? clientNow + (expires - serverNow) : expires
}

/** sessionStorage hint that an Entra session exists or a sign-in redirect is under way. */
export const ENTRA_HINT_KEY = 'rd.entra'

interface State {
  session: Session
  entraReady: boolean
}

let guestToken: string | null = null
let controller: EntraController | null = null
let state: State = initialState()
const listeners = new Set<() => void>()
const signOutListeners = new Set<() => void>()

function entraHint(): boolean {
  try {
    return window.sessionStorage.getItem(ENTRA_HINT_KEY) !== null
  } catch {
    return false
  }
}

function setEntraHint(value: 'pending' | 'active' | null): void {
  try {
    if (value === null) window.sessionStorage.removeItem(ENTRA_HINT_KEY)
    else window.sessionStorage.setItem(ENTRA_HINT_KEY, value)
  } catch {
    // sessionStorage unavailable: the session just won't survive a reload
  }
}

function initialState(): State {
  const stored = readGuestSession()
  if (stored) {
    guestToken = stored.accessToken
    return { session: { kind: 'guest', userId: stored.userId, expiresAt: stored.expiresAt }, entraReady: false }
  }
  guestToken = null
  return { session: entraHint() ? { kind: 'restoring' } : { kind: 'anonymous', reason: null }, entraReady: false }
}

/** Bumped on every session change; lets late async work (an in-flight mutation) tell it belongs to an older session. */
let epoch = 0
export function getSessionEpoch(): number {
  return epoch
}

function update(next: Partial<State>): void {
  if (next.session) epoch++
  state = { ...state, ...next }
  listeners.forEach((listener) => listener())
}

export function subscribeSession(listener: () => void): () => void {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

/** Called after every sign-out (explicit, 401 or expiry); Providers uses it to clear the TanStack Query cache. */
export function onSignOut(listener: () => void): () => void {
  signOutListeners.add(listener)
  return () => signOutListeners.delete(listener)
}

export function getSession(): Session {
  return state.session
}

export function useSession(): Session {
  return useSyncExternalStore(subscribeSession, getSession, getSession)
}

export function useEntraReady(): boolean {
  return useSyncExternalStore(
    subscribeSession,
    () => state.entraReady,
    () => state.entraReady,
  )
}

// ---------- transitions ----------

export function startGuestSession(input: GuestSessionInput): void {
  const expiresAt = clientSideExpiry(input.expiresAt, input.serverDate)
  if (!Number.isFinite(expiresAt) || expiresAt <= Date.now()) {
    // Malformed or already past: never hold a session that cannot be valid.
    signOut('expired')
    return
  }
  writeGuestSession({ accessToken: input.accessToken, expiresAt, userId: input.userId })
  guestToken = input.accessToken
  setEntraHint(null)
  update({ session: { kind: 'guest', userId: input.userId, expiresAt } })
}

export function startEntraSession(name: string | null): void {
  clearGuestSession()
  guestToken = null
  setEntraHint('active')
  update({ session: { kind: 'entra', name } })
}

/** Local sign-out: forget the session, then tell listeners (cache clearing). Safe to call when already signed out. */
export function signOut(reason: SignOutReason): void {
  const was = state.session.kind
  clearGuestSession()
  guestToken = null
  setEntraHint(null)
  update({ session: { kind: 'anonymous', reason } })
  if (was !== 'anonymous') signOutListeners.forEach((listener) => listener())
}

/** A restore attempt found nothing (no config, no account): fall back to anonymous without a message. */
export function finishRestoring(reason: SignOutReason | null = null): void {
  if (state.session.kind !== 'restoring') return
  setEntraHint(null)
  update({ session: { kind: 'anonymous', reason } })
}

/** User-initiated sign-out of an Entra account: local first, then the IdP session (navigates away). */
export async function signOutEntra(): Promise<void> {
  const active = controller
  signOut('signedOut')
  if (active) {
    try {
      await active.logout()
    } catch (error) {
      console.error('Entra sign-out failed; the local session is already cleared', error)
    }
  }
}

// ---------- other tabs ----------

/**
 * The guest session lives in localStorage, so other tabs can end or replace it. `storage` events fire only in the
 * *other* tabs: a removal there signs this tab out too; a different token replaces this tab's session (and drops its
 * caches, which belong to the previous guest).
 */
function onStorage(event: StorageEvent): void {
  if (event.key !== null && event.key !== GUEST_STORAGE_KEY) return
  const kind = state.session.kind
  if (kind !== 'guest' && kind !== 'anonymous') return
  const stored = event.newValue === null ? null : readGuestSession()
  if (!stored) {
    if (kind === 'guest') signOut('signedOut')
    return
  }
  if (kind === 'guest' && stored.accessToken === guestToken) return
  guestToken = stored.accessToken
  setEntraHint(null)
  update({ session: { kind: 'guest', userId: stored.userId, expiresAt: stored.expiresAt } })
  signOutListeners.forEach((listener) => listener())
}

if (typeof window !== 'undefined') window.addEventListener('storage', onStorage)

// ---------- Entra controller registry ----------

export function registerEntraController(next: EntraController | null): void {
  controller = next
  update({ entraReady: next !== null })
}

/** Begins the Entra redirect sign-in. A guest session is cleared first (ADR 0014 §8). */
export async function signInWithEntra(): Promise<void> {
  if (!controller) throw new Error('Entra sign-in is not available')
  if (state.session.kind === 'guest') signOut('signedOut')
  setEntraHint('pending')
  try {
    await controller.login()
  } catch (error) {
    setEntraHint(null)
    throw error
  }
}

// ---------- tokens ----------

/** Whether an `Authorization` header value belongs to the current session (guards against late 401s of old sessions). */
export function bearerMatchesSession(authorization: string | null): boolean {
  if (!authorization?.startsWith('Bearer ')) return false
  if (state.session.kind === 'guest') return authorization.slice(7) === guestToken
  return state.session.kind === 'entra'
}

/** The bearer token for the current session, or null when there is none (or it just ended). */
export async function getAccessToken(): Promise<string | null> {
  const session = state.session
  if (session.kind === 'guest') {
    if (session.expiresAt <= Date.now()) {
      signOut('expired')
      return null
    }
    return guestToken
  }
  if (session.kind === 'entra' && controller) {
    const token = await controller.acquireToken()
    if (token === null) signOut('sessionEnded')
    return token
  }
  return null
}

/** Test helper: re-reads storage and drops the Entra controller. */
export function resetSessionForTests(): void {
  controller = null
  listeners.clear()
  signOutListeners.clear()
  state = initialState()
}
