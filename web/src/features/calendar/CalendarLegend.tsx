import { Flag, Sparkles, Target } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { FAMILY_ICON } from './runTypePresentation'

const Trophy = FAMILY_ICON.race

/**
 * What the bubbles mean: completed = filled, planned = dashed, race = trophy, race proposal = dotted with a flag.
 * Every entry pairs a shape with its label, so no meaning rests on colour. The proposal entry is there only while the
 * proposals are shown. M9 adds the hatched sparkles "Suggested" entry (while the layer is on), the goal target and the
 * "from plan" sparkles (while the goals feature is on).
 */
export function CalendarLegend({
  showProposals,
  showSuggestions = false,
  goalsEnabled = false,
}: {
  showProposals: boolean
  /** The suggestions layer is on (feature enabled and the toggle pressed). */
  showSuggestions?: boolean
  /** The goals feature is on: goal markers and the "from plan" icon can appear. */
  goalsEnabled?: boolean
}) {
  const { t } = useTranslation('calendar')
  return (
    <ul aria-label={t('legend.title')} className="mt-3 flex flex-wrap items-center gap-x-5 gap-y-1 text-xs">
      <li className="flex items-center gap-1.5" data-legend="completed">
        <span aria-hidden="true" className="h-3 w-5 rounded border-2 border-solid border-foreground bg-foreground" />
        {t('legend.completed')}
      </li>
      <li className="flex items-center gap-1.5" data-legend="planned">
        <span aria-hidden="true" className="h-3 w-5 rounded border-2 border-dashed border-foreground bg-transparent" />
        {t('legend.planned')}
      </li>
      <li className="flex items-center gap-1.5" data-legend="race">
        <Trophy aria-hidden="true" className="size-3.5" />
        {t('legend.race')}
      </li>
      {showSuggestions ? (
        <li className="flex items-center gap-1.5" data-legend="suggestion">
          <span
            aria-hidden="true"
            className="flex h-4 w-6 items-center justify-center rounded border border-solid border-muted-foreground/60 text-muted-foreground"
            style={{
              backgroundImage:
                'repeating-linear-gradient(135deg, transparent 0 4px, color-mix(in oklab, var(--muted-foreground) 22%, transparent) 4px 5px)',
            }}
          >
            <Sparkles className="size-3" />
          </span>
          {t('legend.suggested')}
        </li>
      ) : null}
      {goalsEnabled ? (
        <li className="flex items-center gap-1.5" data-legend="goal">
          <Target aria-hidden="true" className="size-3.5" />
          {t('legend.goal')}
        </li>
      ) : null}
      {goalsEnabled ? (
        <li className="flex items-center gap-1.5" data-legend="fromPlan">
          <Sparkles aria-hidden="true" className="size-3.5" />
          {t('legend.fromPlan')}
        </li>
      ) : null}
      {showProposals ? (
        <li className="flex items-center gap-1.5" data-legend="proposal">
          <span
            aria-hidden="true"
            className="flex h-4 w-6 items-center justify-center rounded border border-dotted border-muted-foreground"
          >
            <Flag className="size-3 text-muted-foreground" />
          </span>
          {t('legend.proposal')}
        </li>
      ) : null}
    </ul>
  )
}
