import { Flag, Star } from 'lucide-react'
import { useId, useRef } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { toast } from 'sonner'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { returnFocusState } from '@/features/runs/returnFocus'
import { dayLabel } from '@/lib/dates/plainDate'
import { formatRaceDistances } from './raceFormat'
import type { RaceProposal } from './raceTypes'
import { useRacePlace } from './useRacePlace'
import { useRaceStateChange } from './useRaceStateChange'
import '@/i18n/lazyBundles'

/**
 * One race proposal with its actions: Details, Interested (a toggle), Not interested / Undo. Used by the list panel
 * and by the Day panel, so both give the keyboard the same real links and buttons. A signed-up race offers the run.
 */
export function ProposalRow({
  proposal,
  base,
  panel,
  showDate,
  isNew,
}: {
  proposal: RaceProposal
  /** `/diary/:year/:month` of the open panel. */
  base: string
  /** The `data-panel` of the dialog the row is in (focus goes back to its Details link). */
  panel: string
  showDate?: boolean
  isNew?: boolean
}) {
  const { t, i18n } = useTranslation('races')
  const place = useRacePlace()
  const { change, pending } = useRaceStateChange()
  const headingId = useId()
  const row = useRef<HTMLLIElement>(null)
  const distances = formatRaceDistances(proposal.distancesMeters, i18n.language)
  const state = proposal.state
  const action = 'h-11 sm:h-9'

  async function set(next: 'None' | 'Interested' | 'Dismissed') {
    const ok = await change(proposal.raceId, next)
    if (!ok) return
    if (next === 'Dismissed') {
      toast(t('proposals.toast.dismissed', { name: proposal.name }), {
        action: { label: t('proposals.actions.undo'), onClick: () => void change(proposal.raceId, 'None') },
      })
    }
    // The row may have left the list: put the focus on the panel title instead of losing it.
    window.setTimeout(() => {
      if (row.current && !row.current.isConnected) {
        document.querySelector<HTMLElement>('[data-panel] [data-slot="dialog-title"]')?.focus()
      }
    }, 0)
  }

  return (
    <li
      ref={row}
      data-race-id={proposal.raceId}
      data-state={state}
      data-kind="proposal"
      className="rounded-lg border border-dotted border-muted-foreground p-3"
    >
      <div className="flex flex-wrap items-center gap-2">
        <h3 id={headingId} className="flex min-w-0 items-center gap-1.5 font-medium">
          <Flag className="size-4 shrink-0 text-muted-foreground" aria-hidden="true" />
          <span className="min-w-0 break-words">{proposal.name}</span>
        </h3>
        {isNew ? <Badge variant="default">{t('proposals.new')}</Badge> : null}
        {state === 'Interested' ? (
          <Badge variant="outline">
            <Star className="fill-amber-500 text-amber-600" aria-hidden="true" />
            {t('proposals.state.Interested')}
          </Badge>
        ) : null}
        {state === 'Dismissed' ? <Badge variant="outline">{t('proposals.state.Dismissed')}</Badge> : null}
        {state === 'SignedUp' ? <Badge variant="secondary">{t('proposals.state.SignedUp')}</Badge> : null}
        {proposal.dateChanged ? <Badge variant="outline">{t('proposals.state.dateChanged')}</Badge> : null}
      </div>
      {showDate ? <p className="mt-1 text-sm text-muted-foreground">{dayLabel(proposal.date, i18n.language)}</p> : null}
      <p className="mt-1 text-sm">
        {[place(proposal), t(`kinds.${proposal.kind}`), distances || t('proposals.noDistance')]
          .filter(Boolean)
          .join(' · ')}
      </p>
      <div className="mt-3 flex flex-wrap gap-2">
        <Button asChild variant="outline" className={action}>
          <Link
            to={`${base}/races/${proposal.raceId}`}
            data-action="details"
            data-race-id={proposal.raceId}
            aria-describedby={headingId}
            state={returnFocusState(
              `[data-panel="${panel}"] a[data-action="details"][data-race-id="${proposal.raceId}"]`,
            )}
          >
            {t('proposals.actions.details')}
          </Link>
        </Button>
        {state === 'SignedUp' && proposal.linkedRunId ? (
          <Button asChild variant="outline" className={action}>
            <Link to={`${base}/runs/${proposal.linkedRunId}`} aria-describedby={headingId}>
              {t('proposals.actions.viewRun')}
            </Link>
          </Button>
        ) : null}
        {state !== 'SignedUp' && state !== 'Dismissed' ? (
          <Button
            type="button"
            variant="outline"
            className={action}
            aria-pressed={state === 'Interested'}
            aria-describedby={headingId}
            disabled={pending}
            onClick={() => void set(state === 'Interested' ? 'None' : 'Interested')}
          >
            <Star aria-hidden="true" className={state === 'Interested' ? 'fill-amber-500 text-amber-600' : undefined} />
            {t('proposals.actions.interested')}
          </Button>
        ) : null}
        {state === 'Dismissed' ? (
          <Button
            type="button"
            variant="outline"
            className={action}
            aria-describedby={headingId}
            disabled={pending}
            onClick={() => void set('None')}
          >
            {t('proposals.actions.undo')}
          </Button>
        ) : null}
        {state !== 'SignedUp' && state !== 'Dismissed' ? (
          <Button
            type="button"
            variant="outline"
            className={action}
            aria-describedby={headingId}
            disabled={pending}
            onClick={() => void set('Dismissed')}
          >
            {t('proposals.actions.notInterested')}
          </Button>
        ) : null}
      </div>
    </li>
  )
}
