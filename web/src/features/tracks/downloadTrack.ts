import { fetchClient } from '@/api/client'

const REVOKE_DELAY_MS = 10_000

/** A name that is safe as a download attribute on every platform; keeps the user's own name where possible. */
export function downloadFileName(original: string): string {
  // eslint-disable-next-line no-control-regex
  const cleaned = original.replace(/[\u0000-\u001f\\/:*?"<>|]+/g, '_').trim()
  const name = cleaned === '' || cleaned === '.' || cleaned === '..' ? 'track' : cleaned
  return /\.gpx$/i.test(name) ? name : `${name}.gpx`
}

/** Fetches the original file through the API client (bearer token) and hands it to the browser as a download. */
export async function downloadTrackFile(runId: string, track: { id: string; originalFileName: string }): Promise<void> {
  const { data, error } = await fetchClient.GET('/api/runs/{runId}/tracks/{trackId}/gpx', {
    params: { path: { runId, trackId: track.id } },
    parseAs: 'blob',
  })
  if (error !== undefined || data === undefined) throw error ?? new TypeError('Empty download response')
  const url = URL.createObjectURL(data)
  try {
    const link = document.createElement('a')
    link.href = url
    link.download = downloadFileName(track.originalFileName)
    document.body.append(link)
    link.click()
    link.remove()
  } finally {
    // Some browsers read the blob asynchronously after the click: revoke only after a generous delay.
    setTimeout(() => URL.revokeObjectURL(url), REVOKE_DELAY_MS)
  }
}
