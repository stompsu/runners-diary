const TYPES = {
  alreadyLinked: 'urn:runnersdiary:problem:race-already-linked',
  stateLimit: 'urn:runnersdiary:problem:proposal-state-limit',
} as const

export type RaceConflict = { kind: 'alreadyLinked'; runId: string | null } | { kind: 'stateLimit' }

/**
 * The two M8 409 problems (api-contract "Planned in M8"). The shared `parseProblem` knows only the run conflicts, so
 * they are read from the error body here; `race-already-linked` carries the caller's own `runId`.
 */
export function raceConflict(error: unknown): RaceConflict | null {
  if (typeof error !== 'object' || error === null) return null
  const body = error as { type?: unknown; runId?: unknown }
  if (body.type === TYPES.alreadyLinked)
    return { kind: 'alreadyLinked', runId: typeof body.runId === 'string' ? body.runId : null }
  if (body.type === TYPES.stateLimit) return { kind: 'stateLimit' }
  return null
}
