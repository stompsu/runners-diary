/**
 * A GPX file for `userEvent.upload`. The test setup replaces jsdom's `File` by Node's, so it survives the trip through
 * `FormData` -> `Request` -> the fake API (see setup.ts).
 */
export function gpxFile(name: string, content: string | ArrayBuffer | Uint8Array, type = 'application/gpx+xml'): File {
  return new File([content as BlobPart], name, { type })
}
