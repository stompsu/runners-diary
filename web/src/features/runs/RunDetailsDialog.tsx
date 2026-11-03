import { ExternalLink, Star } from 'lucide-react'
import { useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useParams, useSearchParams } from 'react-router'
import { ResponsiveDialog } from '@/components/ResponsiveDialog'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { useMe } from '@/auth/useMe'
import { useRunTypeCodes } from '@/features/calendar/calendarQueries'
import { FAMILY_ICON, familyOf, familyStyle, runTypeLabelKey } from '@/features/calendar/runTypePresentation'
import { diaryPath } from '@/features/diary/diaryMonth'
import { QueryError } from '@/features/errors/QueryError'
import { parseProblem } from '@/lib/api/problem'
import { dayLabel } from '@/lib/dates/plainDate'
import { formatDuration } from '@/lib/format/duration'
import { formatDistance, formatElevation, formatPace } from '@/lib/format/units'
import { DeleteRunDialog } from './DeleteRunDialog'
import { RateRunDialog } from './RateRunDialog'
import { StatusLabel } from './RunList'
import { RunGone } from './RunGone'
import type { RunDto } from './runFormTypes'
import { useRun } from './runQueries'
import { safeHttpUrl } from './safeUrl'
import { returnFocusState } from './returnFocus'
import { useClosePanel, usePanelMonth } from './usePanel'

/** `/diary/:year/:month/runs/:runId[?rate=1]`: everything about one run, with Edit / Mark as completed / Rate / Delete. */
export function RunDetailsDialog() {
  const { runId = '' } = useParams()
  const [search] = useSearchParams()
  const close = useClosePanel()
  const run = useRun(runId)

  if (run.isError && parseProblem(run.error).kind === 'notFound') return <RunGone onClose={close} />
  if (run.data && search.get('rate') === '1' && run.data.status === 'Completed') {
    return <RateRunDialog run={run.data} onClose={close} onGone={close} />
  }
  return <RunDetailsView run={run} close={close} />
}

function RunDetailsView({ run: query, close }: { run: ReturnType<typeof useRun>; close: () => void }) {
  const { t, i18n } = useTranslation('runs')
  const { t: tType } = useTranslation('runTypes')
  const month = usePanelMonth()
  const me = useMe()
  const { query: types, codes } = useRunTypeCodes()
  const [deleting, setDeleting] = useState(false)
  const [origin, setOrigin] = useState<HTMLElement | null>(null)
  const run = query.data
  const code = run ? codes.get(run.runTypeId) : undefined
  const typeLabel = tType(runTypeLabelKey(code))
  const base = diaryPath(month)
  const failed = query.isError ? query : types.isError ? types : null
  const action = 'h-11 sm:h-9'

  return (
    <ResponsiveDialog
      title={run ? (run.title ?? typeLabel) : t('details.loading')}
      onClose={close}
      size="lg"
      restoreFocusDate={run?.date}
      panel="details"
      footer={
        run ? (
          <>
            <Button
              type="button"
              variant="outline"
              className={`${action} text-destructive`}
              onClick={(event) => {
                setOrigin(event.currentTarget)
                setDeleting(true)
              }}
            >
              {t('actions.delete')}
            </Button>
            {run.status === 'Planned' ? (
              <Button asChild variant="outline" className={action}>
                <Link
                  to={`${base}/runs/${run.id}/edit?complete=1`}
                  data-action="complete"
                  state={returnFocusState('[data-panel="details"] a[data-action="complete"]')}
                >
                  {t('actions.markCompleted')}
                </Link>
              </Button>
            ) : (
              <Button asChild variant="outline" className={action}>
                <Link
                  to={`${base}/runs/${run.id}?rate=1`}
                  data-action="rate"
                  state={returnFocusState('[data-panel="details"] a[data-action="rate"]')}
                >
                  {t(run.rating ? 'actions.changeRating' : 'actions.rate')}
                </Link>
              </Button>
            )}
            <Button asChild className={action}>
              <Link
                to={`${base}/runs/${run.id}/edit`}
                data-action="edit"
                state={returnFocusState('[data-panel="details"] a[data-action="edit"]')}
              >
                {t('actions.edit')}
              </Link>
            </Button>
          </>
        ) : undefined
      }
    >
      {failed ? (
        <QueryError error={failed.error} onRetry={() => void failed.refetch()} />
      ) : !run || !types.data || !me.data ? (
        <Skeleton className="h-48 w-full" />
      ) : (
        <>
          <RunDetailsBody
            run={run}
            code={code}
            profileZone={me.data.timeZoneId}
            language={i18n.language}
            typeLabel={typeLabel}
          />
          <DeleteRunDialog
            target={
              deleting
                ? {
                    id: run.id,
                    summary: t('delete.summary', { type: typeLabel, date: dayLabel(run.date, i18n.language) }),
                  }
                : null
            }
            origin={origin}
            onClose={() => setDeleting(false)}
            onDeleted={close}
          />
        </>
      )}
    </ResponsiveDialog>
  )
}

function RunDetailsBody({
  run,
  code,
  profileZone,
  language,
  typeLabel,
}: {
  run: RunDto
  code: string | undefined
  profileZone: string
  language: string
  typeLabel: string
}) {
  const { t } = useTranslation('runs')
  const Icon = FAMILY_ICON[familyOf(code)]
  const pace = formatPace(run.durationSeconds, run.distanceMeters)
  const when = [dayLabel(run.date, language), run.startTime].filter(Boolean).join(' · ')
  const resultsUrl = safeHttpUrl(run.race?.resultsUrl)

  return (
    <div className="flex flex-col gap-5">
      <div style={familyStyle(code)} className="flex flex-wrap items-center gap-2">
        <span className="inline-flex items-center gap-1.5 font-medium text-(--run)">
          <Icon className="size-4" aria-hidden="true" />
          {typeLabel}
        </span>
        <StatusLabel status={run.status} />
      </div>
      <p>
        {when}
        {run.timeZoneId !== profileZone ? (
          <span className="text-muted-foreground">
            {' '}
            · {t('details.zone')}: {run.timeZoneId}
          </span>
        ) : null}
      </p>

      <Facts
        items={[
          [t('fields.distance'), run.distanceMeters ? formatDistance(run.distanceMeters, language) : null],
          [t('fields.duration'), run.durationSeconds ? formatDuration(run.durationSeconds) : null],
          [t('fields.pace'), pace],
          [
            t('fields.elevationGain'),
            run.elevationGainMeters != null ? formatElevation(run.elevationGainMeters, language) : null,
          ],
          [
            t('fields.elevationLoss'),
            run.elevationLossMeters != null ? formatElevation(run.elevationLossMeters, language) : null,
          ],
          [t('fields.avgHeartRate'), run.avgHeartRate != null ? String(run.avgHeartRate) : null],
          [t('fields.maxHeartRate'), run.maxHeartRate != null ? String(run.maxHeartRate) : null],
          [t('fields.rpe'), run.rpe != null ? String(run.rpe) : null],
          [t('fields.surface'), run.surface ? t(`surface.${run.surface}`) : null],
          [t('fields.gear'), run.gear],
          [t('fields.weather'), run.weather],
        ]}
      />

      {run.notes ? (
        <section aria-labelledby="details-notes">
          <h3 id="details-notes" className="mb-1 text-sm font-semibold">
            {t('fields.notes')}
          </h3>
          <p className="whitespace-pre-wrap">{run.notes}</p>
        </section>
      ) : null}

      {run.race ? (
        <section aria-labelledby="details-race" className="rounded-lg border p-3">
          <h3 id="details-race" className="mb-2 text-sm font-semibold">
            {t('race.legend')}: {run.race.name}
          </h3>
          <Facts
            items={[
              [t('race.distanceCategory'), t(`race.categories.${run.race.distanceCategory}`)],
              [
                t('race.officialDistance'),
                run.race.officialDistanceMeters ? formatDistance(run.race.officialDistanceMeters, language) : null,
              ],
              [t('race.bib'), run.race.bibNumber ?? null],
              [t('race.targetTime'), run.race.targetTimeSeconds ? formatDuration(run.race.targetTimeSeconds) : null],
              [t('race.resultTime'), run.race.resultTimeSeconds ? formatDuration(run.race.resultTimeSeconds) : null],
              [t('race.overallPlace'), run.race.overallPlace != null ? String(run.race.overallPlace) : null],
              [t('race.categoryPlace'), run.race.categoryPlace != null ? String(run.race.categoryPlace) : null],
              [t('race.categoryName'), run.race.categoryName ?? null],
            ]}
          />
          {resultsUrl ? (
            <a
              href={resultsUrl}
              target="_blank"
              rel="noopener noreferrer"
              className="mt-2 inline-flex items-center gap-1 text-sm underline"
            >
              {t('race.openResults')}
              <ExternalLink className="size-3.5" aria-hidden="true" />
            </a>
          ) : null}
        </section>
      ) : null}

      {run.rating ? (
        <section aria-labelledby="details-rating">
          <h3 id="details-rating" className="mb-1 text-sm font-semibold">
            {t('form.rating')}
          </h3>
          <p className="flex items-center gap-2">
            <span className="flex" aria-hidden="true">
              {[1, 2, 3, 4, 5].map((n) => (
                <Star
                  key={n}
                  className={
                    n <= run.rating!.stars ? 'size-5 fill-amber-500 text-amber-600' : 'size-5 text-muted-foreground'
                  }
                />
              ))}
            </span>
            <span>{t('rating.selected', { count: run.rating.stars })}</span>
          </p>
          {run.rating.comment ? <p className="mt-1 whitespace-pre-wrap">{run.rating.comment}</p> : null}
        </section>
      ) : null}
    </div>
  )
}

function Facts({ items }: { items: Array<[string, ReactNode | null | undefined]> }) {
  const present = items.filter(([, value]) => value !== null && value !== undefined && value !== '')
  if (present.length === 0) return null
  return (
    <dl className="grid grid-cols-2 gap-x-4 gap-y-2 text-sm sm:grid-cols-3">
      {present.map(([label, value]) => (
        <div key={label}>
          <dt className="text-muted-foreground">{label}</dt>
          <dd className="font-medium">{value}</dd>
        </div>
      ))}
    </dl>
  )
}
