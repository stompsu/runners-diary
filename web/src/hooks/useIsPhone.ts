import { useSyncExternalStore } from 'react'

/** Tailwind's `sm` breakpoint is 640 px; below it dialogs become bottom sheets (ADR 0016, AC14). */
const PHONE_QUERY = '(max-width: 639.98px)'

const hasMatchMedia = () => typeof window !== 'undefined' && typeof window.matchMedia === 'function'

function subscribe(onChange: () => void): () => void {
  if (!hasMatchMedia()) return () => {}
  const list = window.matchMedia(PHONE_QUERY)
  list.addEventListener('change', onChange)
  return () => list.removeEventListener('change', onChange)
}

const getSnapshot = () => hasMatchMedia() && window.matchMedia(PHONE_QUERY).matches

/** True on phone-sized viewports; false when `matchMedia` is unavailable (SSR, old test environments). */
export function useIsPhone(): boolean {
  return useSyncExternalStore(subscribe, getSnapshot, () => false)
}
