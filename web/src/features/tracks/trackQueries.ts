import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useCallback } from 'react'
import { $api } from '@/api/client'
import { useInvalidateRuns } from '@/features/runs/runQueries'
import { uploadTrack } from './uploadTrack'

const FIVE_MINUTES_MS = 5 * 60 * 1000
const THIRTY_MINUTES_MS = 30 * 60 * 1000

const TRACK_PATH = '/api/runs/{runId}/tracks/{trackId}'
const GEOJSON_PATH = '/api/runs/{runId}/tracks/{trackId}/geojson'

const trackInit = (runId: string, trackId: string) => ({ params: { path: { runId, trackId } } })

/** `GET /api/runs/{runId}/tracks/{trackId}`: summary and warnings (plan §3.7: 5 minutes). */
export function useTrack(runId: string, trackId: string, enabled = true) {
  return $api.useQuery('get', TRACK_PATH, trackInit(runId, trackId), { staleTime: FIVE_MINUTES_MS, enabled })
}

/** The processed document never changes for a track id: keep it for the session (plan §3.7). */
export function useTrackGeoJson(runId: string, trackId: string, enabled = true) {
  return $api.useQuery('get', GEOJSON_PATH, trackInit(runId, trackId), {
    staleTime: Infinity,
    gcTime: THIRTY_MINUTES_MS,
    enabled,
  })
}

export const useUploadTrack = () => useMutation({ mutationFn: uploadTrack, retry: false })
export const useDeleteTrack = () => $api.useMutation('delete', TRACK_PATH)

/**
 * After an upload, replace or delete: the run (its `tracks[]`), the calendar months and run lists are stale, and so
 * are the cached summaries of the run's tracks. A removed track id is dropped from the cache (a replacement has a new
 * id; the immutable GeoJSON of the old one is never needed again).
 */
export function useInvalidateTracks(runId: string) {
  const queryClient = useQueryClient()
  const invalidateRuns = useInvalidateRuns()
  return useCallback(
    async (removedTrackId?: string) => {
      if (removedTrackId) {
        for (const path of [TRACK_PATH, GEOJSON_PATH]) {
          queryClient.removeQueries({ queryKey: ['get', path, trackInit(runId, removedTrackId)] })
        }
      }
      await Promise.all([
        invalidateRuns(runId),
        queryClient.invalidateQueries({ queryKey: ['get', TRACK_PATH, { params: { path: { runId } } }] }),
      ])
    },
    [queryClient, invalidateRuns, runId],
  )
}

/** Seeds the cache with a track the app has just received (the upload response), so the prefill needs no fetch. */
export function useSeedTrack() {
  const queryClient = useQueryClient()
  return useCallback(
    (track: { id: string; runId: string }, data: unknown) =>
      queryClient.setQueryData(['get', TRACK_PATH, trackInit(track.runId, track.id)], data),
    [queryClient],
  )
}
