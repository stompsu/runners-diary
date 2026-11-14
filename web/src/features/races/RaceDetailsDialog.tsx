import { ExternalLink, Flag, Star } from 'lucide-react'
import { lazy, Suspense, useId, useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useParams } from 'react-router'
import { useClientConfig } from '@/auth/useClientConfig'
import { useMe } from '@/auth/useMe'
import { ResponsiveDialog } from '@/components/ResponsiveDialog'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { diaryPath } from '@/features/diary/diaryMonth'
import { QueryError } from '@/features/errors/QueryError'
import { formatCoordinates } from '@/features/runs/locationCoordinates'
import { safeHttpUrl } from '@/features/runs/safeUrl'
import { useClosePanel, usePanelMonth } from '@/features/runs/usePanel'
import { parseProblem } from '@/lib/api/problem'
import { compare, dayLabel, diffDays, todayFromMe } from '@/lib/dates/plainDate'
import { formatElevation } from '@/lib/format/units'
import { DateChangedBanner } from './DateChangedBanner'
import { ExternalAnchor } from './ExternalAnchor'
import { RaceSignupForm } from './RaceSignupForm'
import { RaceSourceAttribution } from './RaceSourceAttribution'
import { formatRaceDistances, formatRacePrice } from './raceFormat'
import { useRaceDetails } from './raceQueries'
import type { RaceDetails } from './raceTypes'
import { useRacePlace } from './useRacePlace'
import { useRaceStateChange } from './useRaceStateChange'
import '@/i18n/lazyBundles'

// The map engine stays out of this chunk until a race with a position is opened.
const MapView = lazy(() => import('@/components/map/MapView').then((m) => ({ default: m.MapView })))

/** `/diary/:year/:month/races/:raceId`: one catalogue race with its actions (ADR 0036 §5, ADR 0037). */
export function RaceDetailsDialog() {
  const { raceId = '' } = useParams()
  const close = useClosePanel()
  const race = useRaceDetails(raceId)

  if (race.isError && parseProblem(race.error).kind === 'notFound') return <RaceGone onClose={close} />
  return <RaceDetailsView query={race} close={close} />
}

function RaceGone({ onClose }: { onClose: () => void }) {
  const { t } = useTranslation('races')
  const { t: tRuns } = useTranslation('runs')
  return (
    <ResponsiveDialog
      title={t('details.goneTitle')}
      description={t('details.goneBody')}
      onClose={onClose}
      panel="race-gone"
      footer={
        <Button type="button" className="h-11 sm:h-9" onClick={onClose}>
          {tRuns('actions.close')}
        </Button>
      }
    >
      <span className="sr-only">{t('details.goneTitle')}</span>
    </ResponsiveDialog>
  )
}

function RaceDetailsView({ query, close }: { query: ReturnType<typeof useRaceDetails>; close: () => void }) {
  const { t } = useTranslation('races')
  const me = useMe()
  const race = query.data

  return (
    <ResponsiveDialog
      title={race ? race.name : t('details.loading')}
      onClose={close}
      size="lg"
      restoreFocusDate={race?.date}
      panel="race"
    >
      {query.isError ? (
        <QueryError error={query.error} onRetry={() => void query.refetch()} />
      ) : !race || !me.data ? (
        <Skeleton className="h-48 w-full" />
      ) : (
        <RaceBody race={race} today={todayFromMe(me.data)} />
      )}
    </ResponsiveDialog>
  )
}

function RaceBody({ race, today }: { race: RaceDetails; today: string }) {
  const { t, i18n } = useTranslation('races')
  const locale = i18n.language
  const month = usePanelMonth()
  const base = diaryPath(month)
  const place = useRacePlace()
  const { change, pending } = useRaceStateChange()
  const [signingUp, setSigningUp] = useState(false)
  const reasonId = useId()
  const signupReasonId = useId()
  const signupUrl = safeHttpUrl(race.signupUrl)
  const eventUrl = safeHttpUrl(race.eventUrl)
  const { state } = race
  const signedUp = state === 'SignedUp' && race.linkedRunId !== null
  const duplicate = race.canonicalId !== null

  // Why signing up through us is not possible, if it is not (the primary action then says so).
  const blocked = race.removed
    ? t('details.signupUnavailable.removed')
    : duplicate
      ? t('details.signupUnavailable.duplicate')
      : compare(race.date, today) < 0
        ? t('details.signupUnavailable.past')
        : null
  const action = 'h-11 sm:h-9'

  const when = [dayLabel(race.date, locale), race.startTime].filter(Boolean).join(' · ')

  return (
    <div className="flex flex-col gap-5">
      {race.removed ? <Notice kind="removed">{t('banners.removed')}</Notice> : null}
      {duplicate ? (
        <Notice kind="duplicate">
          <p>{t('banners.duplicate')}</p>
          <Link className="underline" to={`${base}/races/${race.canonicalId}`} replace>
            {t('banners.openCanonical')}
          </Link>
        </Notice>
      ) : null}
      {race.dateChanged ? <DateChangedBanner race={race} /> : null}

      <p className="flex flex-wrap items-center gap-2">
        <span className="inline-flex items-center gap-1.5 font-medium">
          <Flag className="size-4" aria-hidden="true" />
          {when}
        </span>
        {state === 'Interested' ? (
          <span className="inline-flex items-center gap-1 text-sm">
            <Star className="size-4 fill-amber-500 text-amber-600" aria-hidden="true" />
            {t('proposals.state.Interested')}
          </span>
        ) : null}
        {state === 'Dismissed' ? <span className="text-sm">{t('proposals.state.Dismissed')}</span> : null}
        {signedUp ? <span className="text-sm">{t('proposals.state.SignedUp')}</span> : null}
      </p>

      <Facts
        items={[
          [t('details.facts.place'), place(race) || null],
          [t('details.facts.kind'), t(`kinds.${race.kind}`)],
          [t('details.facts.distances'), formatRaceDistances(race.distancesMeters, locale) || null],
          [
            t('details.facts.elevation'),
            race.elevationGainMeters != null ? formatElevation(race.elevationGainMeters, locale) : null,
          ],
          [
            t('details.facts.price'),
            race.price ? formatRacePrice(race.price.amount, race.price.currency, locale) : null,
          ],
          [t('details.facts.organizer'), race.organizerName],
          [
            t('details.facts.deadline'),
            race.signupDeadline ? deadlineText(race.signupDeadline, today, locale, t) : null,
          ],
        ]}
      />

      <section aria-labelledby={`${reasonId}-map`} className="flex flex-col gap-2">
        <h3 id={`${reasonId}-map`} className="sr-only">
          {t('details.map.label')}
        </h3>
        {race.location ? (
          <RaceMap race={race} location={race.location} />
        ) : (
          <p className="text-sm text-muted-foreground">{t('details.map.none')}</p>
        )}
      </section>

      <RaceSourceAttribution sourceId={race.source} sourceUrl={race.sourceUrl} />

      {signingUp && !signedUp ? <RaceSignupForm race={race} onCancel={() => setSigningUp(false)} /> : null}

      <div className="flex flex-wrap gap-2">
        {signupUrl && !race.removed ? (
          <Button asChild className={action}>
            <ExternalAnchor href={signupUrl}>
              {t('details.actions.signupPage')}
              <ExternalLink aria-hidden="true" />
              <span className="sr-only">{t('details.actions.opensNewTab')}</span>
            </ExternalAnchor>
          </Button>
        ) : (
          <>
            <Button type="button" className={action} disabled aria-describedby={reasonId}>
              {t('details.actions.signupPage')}
            </Button>
            <p id={reasonId} className="basis-full text-sm text-muted-foreground">
              {race.removed ? t('details.signupUnavailable.removed') : t('details.signupUnavailable.noUrl')}
            </p>
          </>
        )}
        {eventUrl ? (
          <Button asChild variant="outline" className={action}>
            <ExternalAnchor href={eventUrl}>
              {t('details.actions.eventSite')}
              <ExternalLink aria-hidden="true" />
              <span className="sr-only">{t('details.actions.opensNewTab')}</span>
            </ExternalAnchor>
          </Button>
        ) : null}
        {signedUp ? (
          <Button asChild variant="outline" className={action}>
            <Link to={`${base}/runs/${race.linkedRunId}`}>{t('details.actions.viewRun')}</Link>
          </Button>
        ) : (
          <Button
            type="button"
            variant="outline"
            className={action}
            disabled={blocked !== null || signingUp}
            aria-describedby={blocked ? signupReasonId : undefined}
            onClick={() => setSigningUp(true)}
          >
            {t('details.actions.signedUp')}
          </Button>
        )}
        {!signedUp && state !== 'Dismissed' ? (
          <Button
            type="button"
            variant="outline"
            className={action}
            aria-pressed={state === 'Interested'}
            disabled={pending || (state !== 'Interested' && (race.removed || duplicate))}
            onClick={() => void change(race.raceId, state === 'Interested' ? 'None' : 'Interested')}
          >
            <Star aria-hidden="true" className={state === 'Interested' ? 'fill-amber-500 text-amber-600' : undefined} />
            {t('details.actions.interested')}
          </Button>
        ) : null}
        {state === 'Dismissed' ? (
          <Button
            type="button"
            variant="outline"
            className={action}
            disabled={pending}
            onClick={() => void change(race.raceId, 'None')}
          >
            {t('details.actions.undo')}
          </Button>
        ) : null}
        {!signedUp && state !== 'Dismissed' && !race.removed && !duplicate ? (
          <Button
            type="button"
            variant="outline"
            className={action}
            disabled={pending}
            onClick={() => void change(race.raceId, 'Dismissed')}
          >
            {t('details.actions.notInterested')}
          </Button>
        ) : null}
        {blocked && !signedUp ? (
          <p id={signupReasonId} className="basis-full text-sm text-muted-foreground">
            {blocked}
          </p>
        ) : null}
      </div>
    </div>
  )
}

function Notice({ kind, children }: { kind: string; children: ReactNode }) {
  return (
    <output
      data-banner={kind}
      className="flex flex-col items-start gap-1 rounded-lg border border-amber-600/60 bg-amber-50 p-3 text-sm text-amber-950"
    >
      {children}
    </output>
  )
}

function RaceMap({ race, location }: { race: RaceDetails; location: NonNullable<RaceDetails['location']> }) {
  const { t } = useTranslation('races')
  const maps = useClientConfig().data?.maps
  return (
    <>
      <p className="text-sm text-muted-foreground tabular-nums">
        {t('details.map.coordinates', { coordinates: formatCoordinates(location.latitude, location.longitude) })}
      </p>
      <Suspense fallback={<Skeleton className="h-40 w-full" />}>
        <MapView
          ariaLabel={t('details.map.label')}
          marker={{ latitude: location.latitude, longitude: location.longitude }}
          markerKind="race"
          markerLabel={t('details.map.marker', { name: race.name })}
          fitTo="marker"
          provider={maps?.provider}
          styleUrl={maps?.mapLibreStyleUrl}
          azureMapsDomain={maps?.azureMapsDomain}
          className="h-40 w-full"
        />
      </Suspense>
    </>
  )
}

function deadlineText(
  deadline: string,
  today: string,
  locale: string,
  t: ReturnType<typeof useTranslation<'races'>>['t'],
): string {
  const date = dayLabel(deadline, locale)
  const days = diffDays(today, deadline)
  if (days < 0) return t('details.deadline.closed', { date })
  if (days === 0) return t('details.deadline.closesToday', { date })
  return t('details.deadline.closesIn', { date, count: days })
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
