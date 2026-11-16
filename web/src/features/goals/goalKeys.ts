/**
 * Query-key prefixes of the M9 queries (plan §4.8). The generated client keys are `[method, path, init]`, so a prefix
 * matches every variant of one path. Kept in a tiny module because the shared run-write helper and the diary import
 * them eagerly.
 */
export const GOALS_QUERY_KEY = ['get', '/api/goals'] as const
export const GOAL_QUERY_KEY = ['get', '/api/goals/{goalId}'] as const
export const SUGGESTIONS_QUERY_KEY = ['get', '/api/training-suggestions'] as const
