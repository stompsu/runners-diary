import { useTranslation } from 'react-i18next'
import { Button } from '@/components/ui/button'
import { dayLabel } from '@/lib/dates/plainDate'
import { formatDuration } from '@/lib/format/duration'
import { formatDistance, formatPaceValue } from '@/lib/format/units'
import type { EstimateGoalRequest } from './goalTypes'
import { useGoalEstimate } from './goalQueries'

const KNOWN_WARNINGS = [
  'noRecentEffort',
  'targetMuchFasterThanPredicted',
  'tooLittleTime',
  'lowWeeklyVolume',
  'beyondPlanHorizon',
] as const
type WarningCode = (typeof KNOWN_WARNINGS)[number]
const isKnown = (code: string): code is WarningCode => (KNOWN_WARNINGS as readonly string[]).includes(code)

/**
 * "Based on your 10K on 12 Oct, about 1:52 looks realistic" with Use as target, plus the polite warning list
 * (never blocking, ADR 0039 §4). Reads the estimate for a valid part of the form; nothing is saved.
 */
export function RealisticTargetHint({
  input,
  onUse,
}: {
  input: EstimateGoalRequest | null
  onUse: (durationSeconds: number) => void
}) {
  const { t, i18n } = useTranslation('goals')
  const estimate = useGoalEstimate(input)
  if (!input) return null
  const data = estimate.data
  const locale = i18n.language

  return (
    <section
      aria-label={t('estimate.title')}
      className="rounded-md border bg-muted/30 p-3 text-sm"
      data-testid="estimate-hint"
    >
      {estimate.isError ? (
        <p>{t('estimate.failed')}</p>
      ) : !data ? (
        <p className="text-muted-foreground">{t('estimate.loading')}</p>
      ) : (
        <div className="flex flex-col gap-2">
          {data.prediction ? (
            <>
              <p>
                {data.prediction.basis.kind === 'RecentTraining'
                  ? t('estimate.basisTraining', {
                      count: data.prediction.basis.runCount,
                      time: formatDuration(data.prediction.durationSeconds),
                      pace: formatPaceValue(data.prediction.paceSecondsPerKm),
                    })
                  : t(data.prediction.basis.kind === 'Race' ? 'estimate.based' : 'estimate.basisRun', {
                      distance: formatDistance(data.prediction.basis.distanceMeters, locale, 1),
                      date: dayLabel(data.prediction.basis.date, locale),
                      time: formatDuration(data.prediction.durationSeconds),
                      pace: formatPaceValue(data.prediction.paceSecondsPerKm),
                    })}
              </p>
              {data.prediction.confidence === 'Low' ? (
                <p className="text-muted-foreground">{t('estimate.lowConfidence')}</p>
              ) : null}
              <div>
                <Button
                  type="button"
                  variant="outline"
                  className="h-11 sm:h-9"
                  onClick={() => onUse(data.prediction!.durationSeconds)}
                >
                  {t('estimate.useAsTarget')}
                </Button>
              </div>
            </>
          ) : (
            <p>{t('estimate.none')}</p>
          )}
          {data.warnings.filter((w) => isKnown(w.code) && w.code !== 'noRecentEffort').length > 0 ? (
            <ul className="list-disc pl-5">
              {data.warnings
                .filter((w) => isKnown(w.code) && w.code !== 'noRecentEffort')
                .map((warning) => (
                  <li key={warning.code}>
                    {warning.code === 'lowWeeklyVolume'
                      ? t('warnings.lowWeeklyVolume', { value: formatDistance(warning.value ?? 0, locale, 1) })
                      : warning.code === 'tooLittleTime'
                        ? t('warnings.tooLittleTime', {
                            count: Math.round(warning.value ?? 1),
                            value: Math.round(warning.value ?? 1),
                          })
                        : t(`warnings.${warning.code as 'targetMuchFasterThanPredicted' | 'beyondPlanHorizon'}`, {
                            value: warning.value ?? '',
                          })}
                  </li>
                ))}
            </ul>
          ) : null}
        </div>
      )}
    </section>
  )
}
