import { Flag, Star } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { formatRaceDistances } from '@/features/races/raceFormat'
import type { RaceProposal } from '@/features/races/raceTypes'
import { cn } from '@/lib/utils'

/**
 * Desktop bubble of a race proposal (ADR 0037 §4): dotted 1 px border in the muted ink, transparent fill, a flag, the
 * name and the distances, plus a star when the user is Interested. Distinct from a planned run (dashed 2 px, coloured)
 * by shape and icon, so it never relies on colour. Presentational for keyboard and screen-reader users (the cell's
 * accessible name lists the proposals); mouse and touch users can click it to open the race.
 */
export function ProposalBubble({ proposal, onOpen }: { proposal: RaceProposal; onOpen: (raceId: string) => void }) {
  const { t, i18n } = useTranslation('calendar')
  const distances = formatRaceDistances(proposal.distancesMeters, i18n.language)
  const interested = proposal.state === 'Interested'

  return (
    <button
      type="button"
      tabIndex={-1}
      aria-hidden="true"
      data-kind="proposal"
      data-race-id={proposal.raceId}
      data-state={proposal.state}
      title={t('proposalBubble', { name: proposal.name })}
      onClick={(event) => {
        event.stopPropagation()
        onOpen(proposal.raceId)
      }}
      className={cn(
        'hidden w-full items-center gap-1 rounded border border-dotted border-muted-foreground bg-transparent px-1 text-left text-[11px] leading-4 text-foreground sm:flex',
      )}
    >
      <Flag className="size-3 shrink-0 text-muted-foreground" />
      <span className="truncate">{proposal.name}</span>
      {distances ? <span className="ml-auto shrink-0 text-muted-foreground">{distances}</span> : null}
      {interested ? <Star className="size-3 shrink-0 fill-amber-500 text-amber-600" /> : null}
    </button>
  )
}

/** Phone marker of a proposal: a hollow flag glyph (a star-filled flag when Interested). */
export function ProposalDot({ proposal }: { proposal: RaceProposal }) {
  return (
    <Flag
      data-kind="proposal"
      data-race-id={proposal.raceId}
      data-state={proposal.state}
      className={cn(
        'size-3 text-muted-foreground',
        proposal.state === 'Interested' ? 'fill-amber-500 text-amber-600' : 'fill-transparent',
      )}
    />
  )
}
