import { act } from '@testing-library/react'

/**
 * jsdom has no `matchMedia`. This stub answers the one query the app cares about (phone: max-width 639.98px) from a
 * switch that tests flip with `setPhoneViewport`; every other query (sonner's colour scheme, ...) never matches.
 */
type Listener = (event: MediaQueryListEvent) => void

let phone = false
const listeners = new Map<string, Set<Listener>>()

const isPhoneQuery = (query: string) => query.includes('max-width: 639')

function create(query: string): MediaQueryList {
  const set = listeners.get(query) ?? new Set<Listener>()
  listeners.set(query, set)
  const list = {
    media: query,
    get matches() {
      return isPhoneQuery(query) && phone
    },
    onchange: null,
    addEventListener: (_type: string, listener: Listener) => set.add(listener),
    removeEventListener: (_type: string, listener: Listener) => set.delete(listener),
    addListener: (listener: Listener) => set.add(listener),
    removeListener: (listener: Listener) => set.delete(listener),
    dispatchEvent: () => true,
  }
  return list as unknown as MediaQueryList
}

export function installMatchMedia(): void {
  phone = false
  window.matchMedia = (query: string) => create(query)
}

/** Switches the viewport between phone (bottom sheets) and desktop (dialogs) and notifies subscribers. */
export function setPhoneViewport(value: boolean): void {
  phone = value
  act(() => {
    for (const [query, set] of listeners) {
      if (!isPhoneQuery(query)) continue
      for (const listener of set) listener({ matches: value, media: query } as MediaQueryListEvent)
    }
  })
}
