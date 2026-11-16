/* oxlint-disable jsx-a11y/click-events-have-key-events, jsx-a11y/no-redundant-roles --
   Keys are handled once on the grid (MonthGrid.onKeyDown). The explicit gridcell role is redundant in browsers
   (td inside role=grid) but keeps AT and test-library role mappings unambiguous. */
import { useTranslation } from 'react-i18next'
import { cn } from '@/lib/utils'
import { dayLabel, parse, type PlainDate } from '@/lib/dates/plainDate'
import { formatDistance, metersToKm } from '@/lib/format/units'
import type { GoalDto, TrainingSuggestion } from '@/features/goals/goalTypes'
import { formatRaceDistances } from '@/features/races/raceFormat'
import type { RaceProposal } from '@/features/races/raceTypes'
import { GoalMarker } from './GoalMarker'
import { ProposalBubble, ProposalDot } from './ProposalBubble'
import { RunBubble, RunDot } from './RunBubble'
import { runTypeLabelKey } from './runTypePresentation'
import { SuggestionBubble, SuggestionDot } from './SuggestionBubble'
import type { Run } from './types'

const MAX_BUBBLES = 3
const NO_SUGGESTIONS: TrainingSuggestion[] = []
const NO_GOALS: GoalDto[] = []

interface DayCellProps {
  date: PlainDate
  inMonth: boolean
  isToday: boolean
  isTabStop: boolean
  isSelected: boolean
  runs: Run[]
  /** Pending training suggestions of the day (ADR 0041): after the runs, before the proposals. */
  suggestions?: TrainingSuggestion[]
  /** Active goals whose target date is this day. */
  goals?: GoalDto[]
  /** Race proposals of the day (signed-up ones are left out: the run's own bubble shows). */
  proposals: RaceProposal[]
  codes: ReadonlyMap<number, string>
  onActivate: (date: PlainDate) => void
  onOpenDay: (date: PlainDate) => void
  onOpenRun: (runId: string) => void
  onOpenProposal: (raceId: string) => void
}

/**
 * One day: a gridcell and a single focus stop (roving tabindex). Its accessible name carries the full date and every
 * run, e.g. "Saturday, November 14, 2026, 2 runs: Long run, Completed, 21.1 km; Strides and drills, Planned", followed by
 * the suggested trainings ("; 2 suggested trainings: Easy run, 8 km; Long run, 18 km"), the goal ("; goal: Trail 25K")
 * and the race proposals: "; 1 race proposal: Gliwice Night 10K, 10 km".
 */
export function DayCell({
  date,
  inMonth,
  isToday,
  isTabStop,
  isSelected,
  runs,
  suggestions = NO_SUGGESTIONS,
  goals = NO_GOALS,
  proposals,
  codes,
  onActivate,
  onOpenDay,
  onOpenRun,
  onOpenProposal,
}: DayCellProps) {
  const { t, i18n } = useTranslation('calendar')
  const { t: tType } = useTranslation('runTypes')
  const locale = i18n.language
  const day = parse(date)?.day

  const summaries = runs.map((run) => {
    const type = tType(runTypeLabelKey(codes.get(run.runTypeId)))
    const status = t(`status.${run.status === 'Completed' ? 'completed' : 'planned'}`)
    const summary = run.distanceMeters
      ? t('cell.runWithDistance', { type, status, distance: formatDistance(run.distanceMeters, locale) })
      : t('cell.run', { type, status })
    return run.goalId ? `${summary}, ${t('cell.fromPlan')}` : summary
  })
  const runsLabel =
    runs.length === 0
      ? t('cell.empty', { date: dayLabel(date, locale) })
      : t('cell.runs', { count: runs.length, date: dayLabel(date, locale), runs: summaries.join('; ') })
  const suggestionSummaries = suggestions.map((suggestion) => {
    const type = tType(runTypeLabelKey(codes.get(suggestion.runTypeId)))
    return suggestion.distanceMeters
      ? t('cell.suggestionWithDistance', { type, distance: formatDistance(suggestion.distanceMeters, locale) })
      : t('cell.suggestion', { type })
  })
  const proposalSummaries = proposals.map((proposal) =>
    [
      proposal.name,
      formatRaceDistances(proposal.distancesMeters, locale),
      proposal.state === 'Interested' ? t('cell.proposalInterested') : '',
    ]
      .filter(Boolean)
      .join(', '),
  )
  const label = [
    runsLabel,
    suggestions.length === 0
      ? ''
      : t('cell.suggestions', { count: suggestions.length, suggestions: suggestionSummaries.join('; ') }),
    ...goals.map((goal) => t('cell.goal', { title: goal.title })),
    proposals.length === 0
      ? ''
      : t('cell.proposals', { count: proposals.length, proposals: proposalSummaries.join('; ') }),
  ]
    .filter(Boolean)
    .join('; ')

  // Runs first, then suggestions, then proposals, sharing one cap and one "+n" (ADR 0041 §4).
  const items = [
    ...runs.map((run) => ({ key: run.id, run, suggestion: undefined, proposal: undefined })),
    ...suggestions.map((suggestion) => ({ key: suggestion.id, run: undefined, suggestion, proposal: undefined })),
    ...proposals.map((proposal) => ({ key: proposal.raceId, run: undefined, suggestion: undefined, proposal })),
  ]

  const totalMeters = runs.reduce((sum, run) => sum + (run.distanceMeters ?? 0), 0)

  return (
    // Keys are handled once on the grid (MonthGrid.onKeyDown); the cell is the roving focus stop.
    <td
      role="gridcell"
      aria-current={isToday ? 'date' : undefined}
      data-date={date}
      data-in-month={inMonth}
      data-selected={isSelected || undefined}
      tabIndex={isTabStop ? 0 : -1}
      onFocus={() => onActivate(date)}
      onClick={() => onOpenDay(date)}
      className={cn(
        'h-14 min-w-0 cursor-pointer overflow-hidden border p-0.5 align-top outline-none focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-primary sm:h-24 sm:p-1',
        inMonth ? 'bg-background' : 'bg-muted/60 text-muted-foreground',
        isSelected && 'ring-2 ring-primary ring-inset',
      )}
    >
      {/* The name comes from content (not aria-label), so visible text never mismatches the accessible name. */}
      <span className="sr-only">{label}</span>
      <span
        aria-hidden="true"
        className={cn(
          'inline-flex size-6 items-center justify-center rounded-full text-xs',
          isToday && 'bg-primary font-bold text-primary-foreground',
        )}
      >
        {day}
      </span>
      {goals.map((goal) => (
        <GoalMarker key={goal.id} goal={goal} />
      ))}
      <div aria-hidden="true" className="mt-0.5 hidden flex-col gap-0.5 sm:flex">
        {items
          .slice(0, MAX_BUBBLES)
          .map((item) =>
            item.run ? (
              <RunBubble key={item.key} run={item.run} code={codes.get(item.run.runTypeId)} onOpen={onOpenRun} />
            ) : item.suggestion ? (
              <SuggestionBubble
                key={item.key}
                suggestion={item.suggestion}
                code={codes.get(item.suggestion.runTypeId)}
                onOpen={onOpenDay}
              />
            ) : (
              <ProposalBubble key={item.key} proposal={item.proposal} onOpen={onOpenProposal} />
            ),
          )}
        {items.length > MAX_BUBBLES ? (
          <span className="px-1 text-[11px] text-muted-foreground">
            {t('more', { count: items.length - MAX_BUBBLES })}
          </span>
        ) : null}
      </div>
      {items.length > 0 ? (
        <div aria-hidden="true" className="flex flex-col items-center gap-0.5 sm:hidden">
          <span className="flex flex-wrap justify-center gap-0.5">
            {items
              .slice(0, MAX_BUBBLES + 1)
              .map((item) =>
                item.run ? (
                  <RunDot key={item.key} run={item.run} code={codes.get(item.run.runTypeId)} />
                ) : item.suggestion ? (
                  <SuggestionDot key={item.key} suggestion={item.suggestion} />
                ) : (
                  <ProposalDot key={item.key} proposal={item.proposal} />
                ),
              )}
          </span>
          {totalMeters > 0 ? (
            <span className="text-[10px] leading-3">{Number(metersToKm(totalMeters).toFixed(1))}</span>
          ) : null}
        </div>
      ) : null}
    </td>
  )
}
