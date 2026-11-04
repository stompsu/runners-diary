import { vi } from 'vitest'

/** jsdom has no ResizeObserver; Recharts needs one that reports a size (600 x 160) as soon as it observes. */
export function stubResizeObserver(): void {
  vi.stubGlobal(
    'ResizeObserver',
    class {
      callback: ResizeObserverCallback
      constructor(callback: ResizeObserverCallback) {
        this.callback = callback
      }
      observe(target: Element) {
        this.callback([{ target, contentRect: { width: 600, height: 160 } } as ResizeObserverEntry], this as never)
      }
      unobserve() {}
      disconnect() {}
    },
  )
}
