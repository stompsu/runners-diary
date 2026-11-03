import { useEffect, useState } from 'react'

/** The latest `value` once it has stayed unchanged for `delayMs` (e.g. for screen-reader announcements while typing). */
export function useDebouncedValue<T>(value: T, delayMs: number): T {
  const [debounced, setDebounced] = useState(value)
  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), delayMs)
    return () => clearTimeout(timer)
  }, [value, delayMs])
  return debounced
}
