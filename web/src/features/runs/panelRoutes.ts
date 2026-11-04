import type { RouteObject } from 'react-router'

/**
 * Panel routes under `/diary/:year/:month` (ADR 0016). Each is code-split: the dialogs, and above all the run form
 * with react-hook-form and zod, load only when a panel is first opened (or after the grid is idle, see `preloadPanels`).
 */
const loaders = {
  day: () => import('./DayPanel').then((m) => m.DayPanel),
  list: () => import('./MonthListPanel').then((m) => m.MonthListPanel),
  details: () => import('./RunDetailsDialog').then((m) => m.RunDetailsDialog),
  track: () => import('@/features/tracks/TrackViewPanel').then((m) => m.TrackViewPanel),
  runNew: () => import('./RunFormPanels').then((m) => m.RunNewPanel),
  runEdit: () => import('./RunFormPanels').then((m) => m.RunEditPanel),
}

const lazyRoute = (path: string, load: () => Promise<() => React.JSX.Element>): RouteObject => ({
  path,
  lazy: async () => ({ Component: await load() }),
})

export const panelRoutes: RouteObject[] = [
  lazyRoute('day/:date', loaders.day),
  lazyRoute('list', loaders.list),
  lazyRoute('runs/new', loaders.runNew),
  lazyRoute('runs/:runId', loaders.details),
  lazyRoute('runs/:runId/edit', loaders.runEdit),
  lazyRoute('runs/:runId/tracks/:trackId', loaders.track),
]

/** Fetches the panel chunks in the background once the grid is on screen, so the first click opens instantly. */
export function preloadPanels(): void {
  // Best effort: a failed preload is retried (and reported) by the navigation that actually needs the chunk.
  for (const load of Object.values(loaders)) load().catch(() => undefined)
}

/** Resolves when every panel chunk is loaded. Tests await it so lazy routes never race the async-util timeouts. */
export function loadAllPanels(): Promise<unknown> {
  return Promise.all(Object.values(loaders).map((load) => load()))
}
