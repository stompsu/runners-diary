import { Info } from 'lucide-react'
import { formatShare, hasDataQualityIssues } from './insightsFormat'
import type { InsightsReport } from './insightsTypes'
import { useInsightsT } from './useInsightsT'

export function DataQualityNote({ quality }: { quality: InsightsReport['dataQuality'] }) {
  const { t, i18n } = useInsightsT()
  if (!hasDataQualityIssues(quality)) return null

  return (
    <aside aria-labelledby="quality-title" className="flex gap-3 rounded-lg border bg-muted/40 p-4 text-sm">
      <Info className="mt-0.5 size-4 shrink-0" aria-hidden="true" />
      <div className="space-y-1">
        <h2 id="quality-title" className="font-medium">
          {t('quality.title')}
        </h2>
        {quality.estimatedLoadShare > 0 ? (
          <p>
            {t('quality.estimated', {
              share: formatShare(quality.estimatedLoadShare, i18n.language),
              count: quality.sessionsWithEstimatedRpe,
            })}
          </p>
        ) : null}
        {quality.sessionsWithoutDuration > 0 ? (
          <p>{t('quality.noDuration', { count: quality.sessionsWithoutDuration })}</p>
        ) : null}
        {quality.runningSessionsWithoutDistance > 0 ? (
          <p>{t('quality.noDistance', { count: quality.runningSessionsWithoutDistance })}</p>
        ) : null}
      </div>
    </aside>
  )
}
