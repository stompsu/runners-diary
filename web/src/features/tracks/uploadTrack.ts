import { fetchClient } from '@/api/client'
import type { components } from '@/api/generated/schema'
import type { TrackDto, TrackKind } from './trackTypes'

export interface UploadTrackInput {
  runId: string
  kind: TrackKind
  file: File
  /** Only after the user confirmed replacing the existing file of this kind (ADR 0023). */
  replace?: boolean
}

/**
 * `POST /api/runs/{runId}/tracks` (multipart). The generated body type describes the file as a string, so the form is
 * built by hand and passed through unchanged (`bodySerializer`); the browser sets the multipart boundary itself.
 * Still goes through `fetchClient`, so the bearer token and the error normalisation apply.
 * Throws the problem body on failure, like the `$api` hooks, so callers share `classifyTrackProblem`.
 */
export async function uploadTrack({ runId, kind, file, replace }: UploadTrackInput): Promise<TrackDto> {
  const form = new FormData()
  // Server-side binding is case-sensitive: "PlannedCourse" | "RecordedTrack".
  form.set('kind', kind)
  form.set('file', file, file.name)
  if (replace) form.set('replace', 'true')
  const { data, error } = await fetchClient.POST('/api/runs/{runId}/tracks', {
    params: { path: { runId } },
    body: form as unknown as components['schemas']['UploadTrackForm'],
    bodySerializer: (body) => body,
  })
  if (error !== undefined || data === undefined) throw error ?? new TypeError('Empty upload response')
  return data
}
