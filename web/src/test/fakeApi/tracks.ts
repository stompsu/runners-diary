import type { components } from '../../api/generated/schema.d.ts'
import type { TrackGeoJson } from '../../features/tracks/trackGeoJson.ts'
import gliwice10k from '../fixtures/gliwice-10k.track.json' with { type: 'json' }

type S = components['schemas']
export type TrackDto = S['TrackDto']
export type TrackSummaryDto = S['TrackSummaryDto']
export type TrackWarningDto = S['TrackWarningDto']
export type RunTrackDto = S['RunTrackDto']
export type GpxTrackKind = S['GpxTrackKind']
export type PlaceDto = S['PlaceDto']

/** A track as the fake API stores it: the DTO plus what the download endpoint returns. */
export interface StoredTrack {
  dto: TrackDto
  geoJson: TrackGeoJson | null
  gpx: string
}

/** `seed/demo/gpx/gliwice-10k.gpx` simplified (shared with the Vitest/Playwright viewers). */
export const GLIWICE_GEOJSON = gliwice10k as unknown as TrackGeoJson

/** README values of the demo file; every uploaded fake file "measures" like this one (see `processFakeGpx`). */
const GLIWICE_DISTANCE_M = 10_004.8
const GLIWICE_START = { latitude: 50.294862, longitude: 18.676642 }
const GLIWICE_FINISH = { latitude: 50.294858, longitude: 18.676642 }

export const FAKE_PLACES: PlaceDto[] = [
  { name: 'Gliwice Rynek', latitude: 50.2945, longitude: 18.6714, type: 'Market Square' },
  { name: 'Gliwice', latitude: 50.2945, longitude: 18.6714, type: 'City' },
  { name: 'Katowice Spodek', latitude: 50.2662, longitude: 19.0254, type: 'Arena' },
  { name: 'Zabrze', latitude: 50.3249, longitude: 18.7857, type: 'City' },
  { name: 'Tarnowskie Gory', latitude: 50.4449, longitude: 18.8554, type: 'City' },
]

export function searchFakePlaces(query: string): PlaceDto[] {
  const needle = query.trim().toLowerCase()
  return FAKE_PLACES.filter((place) => place.name.toLowerCase().includes(needle)).slice(0, 5)
}

// ---------- multipart ----------

export interface UploadedForm {
  fields: Record<string, string>
  file: { name: string; text: string; size: number } | null
}

/**
 * Parses a `multipart/form-data` request by hand. `Request.formData()` is not usable here: under Vitest's jsdom
 * environment the `File` class differs from the one undici expects. Only text parts are supported (GPX is XML).
 */
export async function readUpload(request: Request): Promise<UploadedForm | null> {
  const type = request.headers.get('content-type') ?? ''
  const boundary = /boundary=(?:"([^"]+)"|([^;]+))/i.exec(type)
  const delimiter = boundary?.[1] ?? boundary?.[2]
  if (!type.toLowerCase().startsWith('multipart/form-data') || !delimiter) return null
  const body = await request.text()
  const form: UploadedForm = { fields: {}, file: null }
  for (const part of body.split(`--${delimiter}`).slice(1)) {
    if (part.startsWith('--')) break
    const split = part.indexOf('\r\n\r\n')
    if (split < 0) continue
    const head = part.slice(0, split)
    const content = part.slice(split + 4).replace(/\r\n$/, '')
    const name = /name="([^"]*)"/i.exec(head)?.[1]
    const fileName = /filename="([^"]*)"/i.exec(head)?.[1]
    if (!name) continue
    if (fileName !== undefined)
      form.file = { name: fileName, text: content, size: new TextEncoder().encode(content).length }
    else form.fields[name] = content
  }
  return form
}

// ---------- fake GPX processing ----------

export type FakeGpxRejection =
  | 'wrongExtension'
  | 'malformedXml'
  | 'dtdProhibited'
  | 'unsupportedFormat'
  | 'notGpx'
  | 'tooFewPoints'
  | 'invalidCoordinate'

export const REJECTION_MESSAGES: Record<FakeGpxRejection, string> = {
  wrongExtension: 'The file must have the .gpx extension.',
  malformedXml: 'The file is not well-formed XML.',
  dtdProhibited: 'Document type declarations are not allowed.',
  unsupportedFormat: 'The file is not a GPX document.',
  notGpx: 'The file is not a GPX document.',
  tooFewPoints: 'The track needs at least two points.',
  invalidCoordinate: 'The file contains an invalid coordinate.',
}

export interface InspectedGpx {
  points: number
  hasElevation: boolean
  times: string[]
  routeOnly: boolean
}

/** A coarse stand-in for the real parser: enough to exercise every error path the UI knows about. */
export function inspectGpx(fileName: string, text: string): FakeGpxRejection | InspectedGpx {
  if (!/\.gpx$/i.test(fileName)) return 'wrongExtension'
  const trimmed = text.trim()
  if (trimmed === '') return 'malformedXml'
  if (!trimmed.startsWith('<')) return 'unsupportedFormat'
  if (/<!DOCTYPE/i.test(trimmed)) return 'dtdProhibited'
  if (!/<gpx[\s>]/i.test(trimmed)) return 'notGpx'
  if (!/<\/gpx>\s*$/i.test(trimmed)) return 'malformedXml'
  const pointTags = [...trimmed.matchAll(/<(trkpt|rtept)\b([^>]*)>/gi)]
  for (const [, , attributes] of pointTags) {
    const lat = Number(/lat="([^"]*)"/i.exec(attributes ?? '')?.[1])
    const lon = Number(/lon="([^"]*)"/i.exec(attributes ?? '')?.[1])
    if (!Number.isFinite(lat) || !Number.isFinite(lon) || Math.abs(lat) > 90 || Math.abs(lon) > 180) {
      return 'invalidCoordinate'
    }
  }
  if (pointTags.length < 2) return 'tooFewPoints'
  return {
    points: pointTags.length,
    hasElevation: /<ele>/i.test(trimmed),
    times: [...trimmed.matchAll(/<time>([^<]+)<\/time>/gi)].map((match) => match[1]!.trim()),
    routeOnly: /<rte[\s>]/i.test(trimmed) && !/<trk[\s>]/i.test(trimmed),
  }
}

/** The run's local date and time for an instant: what the real API puts into `startLocal`. */
export function utcToLocal(iso: string, zone: string): { date: string; time: string } {
  const parts = new Intl.DateTimeFormat('en-US', {
    timeZone: zone,
    hourCycle: 'h23',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  }).formatToParts(new Date(iso))
  const get = (type: string) => parts.find((part) => part.type === type)!.value
  return { date: `${get('year')}-${get('month')}-${get('day')}`, time: `${get('hour')}:${get('minute')}` }
}

/**
 * The fake "measures" every accepted file like the Gliwice demo track (distance 10 004.8 m, +15/-15 m, same line),
 * except where the file says otherwise: no `<ele>` = no elevation figures and no profile, `<time>` elements = elapsed
 * time and start from the first and last one, a route without track = untimed + `routeConverted`.
 */
export function processFakeGpx(
  gpx: InspectedGpx,
  zone: string,
): { summary: TrackSummaryDto; warnings: TrackWarningDto[]; geoJson: TrackGeoJson } {
  const warnings: TrackWarningDto[] = []
  if (!gpx.hasElevation) warnings.push('noElevation')
  if (gpx.routeOnly) warnings.push('routeConverted')
  const timed = !gpx.routeOnly && gpx.times.length >= 2
  const startUtc = timed ? new Date(gpx.times[0]!).toISOString() : null
  const endUtc = timed ? new Date(gpx.times.at(-1)!).toISOString() : null
  const elapsed = timed ? Math.round((Date.parse(endUtc!) - Date.parse(startUtc!)) / 1000) : null
  const local = startUtc ? utcToLocal(startUtc, zone) : null
  const summary: TrackSummaryDto = {
    pointCount: gpx.points,
    distanceMeters: GLIWICE_DISTANCE_M,
    elevationGainMeters: gpx.hasElevation ? 15 : null,
    elevationLossMeters: gpx.hasElevation ? 15 : null,
    minElevationMeters: gpx.hasElevation ? 213.6 : null,
    maxElevationMeters: gpx.hasElevation ? 231.4 : null,
    elapsedSeconds: elapsed,
    movingSeconds: elapsed === null ? null : elapsed >= 20 ? elapsed - 5 : elapsed,
    startUtc,
    endUtc,
    start: GLIWICE_START,
    finish: GLIWICE_FINISH,
    startLocal: local ? { ...local, timeZoneId: zone } : null,
  }
  const geoJson: TrackGeoJson = gpx.hasElevation
    ? GLIWICE_GEOJSON
    : {
        ...GLIWICE_GEOJSON,
        profile: null,
        splits: GLIWICE_GEOJSON.splits.map((split) => ({ ...split, elevationDeltaMeters: null })),
      }
  return { summary, warnings, geoJson }
}

export const toRunTrack = (track: TrackDto): RunTrackDto => ({
  id: track.id,
  kind: track.kind,
  status: track.status,
  originalFileName: track.originalFileName,
  createdAt: track.createdAt,
})

/** A minimal valid GPX for tests and the demo seed; `ele`/`times` switch the optional parts on. */
export function sampleGpx(options: { times?: [string, string]; ele?: boolean; route?: boolean } = {}): string {
  const point = (lat: number, lon: number, time?: string) =>
    `<${options.route ? 'rtept' : 'trkpt'} lat="${lat}" lon="${lon}">${options.ele === false ? '' : '<ele>220</ele>'}${
      time ? `<time>${time}</time>` : ''
    }</${options.route ? 'rtept' : 'trkpt'}>`
  const points = [point(50.2948, 18.6766, options.times?.[0]), point(50.2956, 18.6781, options.times?.[1])].join('')
  const body = options.route ? `<rte>${points}</rte>` : `<trk><trkseg>${points}</trkseg></trk>`
  return `<?xml version="1.0" encoding="UTF-8"?><gpx version="1.1" creator="fake" xmlns="http://www.topografix.com/GPX/1/1">${body}</gpx>`
}
