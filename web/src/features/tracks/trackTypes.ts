import type { components } from '@/api/generated/schema'

export type TrackDto = components['schemas']['TrackDto']
export type TrackSummaryDto = components['schemas']['TrackSummaryDto']
export type RunTrackDto = components['schemas']['RunTrackDto']
export type TrackKind = components['schemas']['GpxTrackKind']
export type PlaceDto = components['schemas']['PlaceDto']

export const TRACK_KINDS: readonly TrackKind[] = ['PlannedCourse', 'RecordedTrack']

/** Plan §3.3 / `GpxLimits` defaults: the server is authoritative, this only spares a pointless upload. */
export const MAX_FILE_BYTES = 20 * 1024 * 1024
export const GUEST_MAX_FILE_BYTES = 5 * 1024 * 1024

export const maxFileBytes = (guest: boolean): number => (guest ? GUEST_MAX_FILE_BYTES : MAX_FILE_BYTES)

/** The track of a kind on a run, if any (at most one per kind, ADR 0023 §1). */
export const trackOfKind = (tracks: readonly RunTrackDto[], kind: TrackKind): RunTrackDto | undefined =>
  tracks.find((track) => track.kind === kind)
