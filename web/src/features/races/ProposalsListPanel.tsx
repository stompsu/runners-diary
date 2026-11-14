import { useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { ResponsiveDialog } from '@/components/ResponsiveDialog'
import { Button } from '@/components/ui/button'
import { Label } from '@/components/ui/label'
import { Skeleton } from '@/components/ui/skeleton'
import { Switch } from '@/components/ui/switch'
import { useMe } from '@/auth/useMe'
import { diaryPath } from '@/features/diary/diaryMonth'
import { QueryError } from '@/features/errors/QueryError'
import { useClosePanel, usePanelMonth } from '@/features/runs/usePanel'
import {
  compare,
  dayLabel,
  diffDays,
  firstOfMonth,
  lastOfMonth,
  monthLabel,
  rangeLabel,
  todayFromMe,
  type PlainDate,
} from '@/lib/dates/plainDate'
import { ProposalRow } from './ProposalRow'
import { CATALOGUE_STALE_DAYS, plainDateIn } from './raceFormat'
import { useMarkRaceProposalsSeen, useMonthProposals } from './raceQueries'
import type { RaceProposal } from './raceTypes'
import '@/i18n/lazyBundles'

/**
 * `/diary/:year/:month/races`: the proposals of the month by date, with a "New" badge for those not seen before, a
 * "Show dismissed" switch and the age of the race list. Opening it marks the proposals as seen (ADR 0037 §3).
 */
export function ProposalsListPanel() {
  const { t, i18n } = useTranslation('races')
  const locale = i18n.language
  const month = usePanelMonth()
  const close = useClosePanel()
  const me = useMe()
  const [showDismissed, setShowDismissed] = useState(false)
  const { gate, query } = useMonthProposals(month, { includeDismissed: showDismissed, always: true })
  const markSeen = useMarkRaceProposalsSeen()
  const markedRef = useRef(false)
  // The ids that were new when the panel opened: the badges stay while it is open, whatever `markSeen` clears in the cache.
  const [newIds, setNewIds] = useState<ReadonlySet<string> | null>(null)
  const data = query.data

  if (data && newIds === null) setNewIds(new Set(data.items.filter((item) => item.isNew).map((item) => item.raceId)))

  useEffect(() => {
    if (!data || markedRef.current || !data.profileConfigured) return
    markedRef.current = true
    markSeen.mutate({})
  }, [data, markSeen])

  const first = firstOfMonth(month.year, month.month)
  const last = lastOfMonth(month.year, month.month)
  const groups = useMemo(() => {
    const byDate = new Map<PlainDate, RaceProposal[]>()
    for (const item of data?.items ?? []) {
      if (compare(item.date, first) < 0 || compare(item.date, last) > 0) continue
      byDate.set(item.date, [...(byDate.get(item.date) ?? []), item])
    }
    return [...byDate.entries()]
  }, [data, first, last])

  const base = diaryPath(month)
  const failed = gate.interests.isError ? gate.interests : query.isError ? query : null
  const title = t('proposals.title', { month: monthLabel(month.year, month.month, locale) })

  return (
    <ResponsiveDialog title={title} onClose={close} size="lg" panel="races">
      {failed ? (
        <QueryError error={failed.error} onRetry={() => void failed.refetch()} />
      ) : !gate.interests.data ? (
        <Skeleton className="h-32 w-full" />
      ) : !gate.configured ? (
        <div className="flex flex-col items-start gap-3">
          <p className="text-muted-foreground">{t('proposals.notConfigured')}</p>
          <Button asChild className="h-11 sm:h-9">
            <Link to="/settings/races">{t('proposals.setup')}</Link>
          </Button>
        </div>
      ) : !data ? (
        <Skeleton className="h-32 w-full" />
      ) : (
        <div className="flex flex-col gap-5">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div className="flex items-center gap-2">
              <Switch id="proposals-show-dismissed" checked={showDismissed} onCheckedChange={setShowDismissed} />
              <Label htmlFor="proposals-show-dismissed">{t('proposals.showDismissed')}</Label>
            </div>
            <Updated
              updatedUtc={data.catalogueUpdatedUtc}
              zone={me.data?.timeZoneId}
              today={me.data ? todayFromMe(me.data) : null}
            />
          </div>
          <WindowNote
            effectiveFrom={data.effectiveFrom}
            effectiveTo={data.effectiveTo}
            first={first}
            last={last}
            locale={locale}
          />
          {data.truncated ? <p className="text-sm text-muted-foreground">{t('proposals.truncated')}</p> : null}
          {groups.length === 0 ? (
            <p className="text-muted-foreground">{t('proposals.empty')}</p>
          ) : (
            groups.map(([date, items]) => (
              <section key={date} aria-labelledby={`proposals-${date}`}>
                <h3 id={`proposals-${date}`} className="mb-2 font-semibold">
                  {dayLabel(date, locale)}
                </h3>
                <ul className="flex flex-col gap-3">
                  {items.map((proposal) => (
                    <ProposalRow
                      key={proposal.raceId}
                      proposal={proposal}
                      base={base}
                      panel="races"
                      isNew={newIds?.has(proposal.raceId) ?? false}
                    />
                  ))}
                </ul>
              </section>
            ))
          )}
        </div>
      )}
    </ResponsiveDialog>
  )
}

/** "Race list updated Nov 12, 2026" plus a hint once it is more than 3 weeks old (the importer may have stalled). */
function Updated({
  updatedUtc,
  zone,
  today,
}: {
  updatedUtc: string | null
  zone: string | undefined
  today: PlainDate | null
}) {
  const { t, i18n } = useTranslation('races')
  if (!updatedUtc || !zone) return null
  const updated = plainDateIn(updatedUtc, zone)
  const stale = today !== null && diffDays(updated, today) > CATALOGUE_STALE_DAYS
  return (
    <div className="text-sm text-muted-foreground">
      <p>{t('proposals.updated', { date: rangeLabel(updated, updated, i18n.language) })}</p>
      {stale ? <p role="note">{t('proposals.stale')}</p> : null}
    </div>
  )
}

/** The API clamps to [today, today + horizon]: say so when the month is only partly (or not at all) covered. */
function WindowNote({
  effectiveFrom,
  effectiveTo,
  first,
  last,
  locale,
}: {
  effectiveFrom: PlainDate
  effectiveTo: PlainDate
  first: PlainDate
  last: PlainDate
  locale: string
}) {
  const { t } = useTranslation('races')
  const empty =
    compare(effectiveTo, effectiveFrom) < 0 || compare(effectiveTo, first) < 0 || compare(effectiveFrom, last) > 0
  if (empty) return <p className="text-sm text-muted-foreground">{t('proposals.outsideWindow')}</p>
  const from = compare(effectiveFrom, first) > 0 ? effectiveFrom : first
  const to = compare(effectiveTo, last) < 0 ? effectiveTo : last
  if (from === first && to === last) return null
  return (
    <p className="text-sm text-muted-foreground">{t('proposals.window', { range: rangeLabel(from, to, locale) })}</p>
  )
}
