import { useSyncExternalStore } from 'react'

/**
 * Session-wide "Azure Maps does not work" flag (ADR 0022 §5, ADR 0033). Once the token endpoint or the SDK fails, every map in this
 * page session uses MapLibre instead; a reload tries Azure Maps again. Kept in a tiny module with no SDK import, so `MapView`
 * (initial bundle) can read it without pulling the Azure Maps chunk.
 */
let failed = false
const listeners = new Set<() => void>()

export function markAzureMapsUnavailable() {
  if (failed) return
  failed = true
  listeners.forEach((listener) => listener())
}

/** Test helper: a new "session". */
export function resetAzureMapsFallback() {
  failed = false
  listeners.forEach((listener) => listener())
}

function subscribe(listener: () => void) {
  listeners.add(listener)
  return () => {
    listeners.delete(listener)
  }
}

export function useAzureMapsFailed(): boolean {
  return useSyncExternalStore(subscribe, () => failed)
}
