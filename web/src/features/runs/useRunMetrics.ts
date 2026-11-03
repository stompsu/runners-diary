import { useTranslation } from 'react-i18next'
import type { Run } from '@/features/calendar/types'
import { formatDuration } from '@/lib/format/duration'
import { formatDistance, formatElevation, formatPace } from '@/lib/format/units'

/** "8 km · 0:44:00 · 5:30 /km · 40 m" (parts that exist). Shared with the details view. */
export function useRunMetrics() {
  const { i18n } = useTranslation()
  return (run: Pick<Run, 'distanceMeters' | 'durationSeconds' | 'elevationGainMeters'>): string[] => {
    const parts: string[] = []
    if (run.distanceMeters) parts.push(formatDistance(run.distanceMeters, i18n.language))
    if (run.durationSeconds) parts.push(formatDuration(run.durationSeconds))
    const pace = formatPace(run.durationSeconds, run.distanceMeters)
    if (pace) parts.push(pace)
    if (run.elevationGainMeters) parts.push(formatElevation(run.elevationGainMeters, i18n.language))
    return parts
  }
}
