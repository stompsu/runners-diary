import { Sparkles } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import type { TrainingSuggestion } from '@/features/goals/goalTypes'
import { formatDistance } from '@/lib/format/units'
import { runTypeLabelKey } from './runTypePresentation'

/** Diagonal hatching in the muted ink: the shape cue that separates a suggestion from a plain planned run. */
const HATCH =
  'repeating-linear-gradient(135deg, transparent 0 4px, color-mix(in oklab, var(--muted-foreground) 22%, transparent) 4px 5px)'

/**
 * Desktop bubble of a training suggestion (ADR 0041 §4): hatched background, a thin solid border, a sparkles icon,
 * lighter text and a "Suggested" badge. A planned run is dashed 2 px and coloured, a race proposal dotted 1 px with a
 * flag, so the three differ by shape and icon, never only by colour. Presentational for keyboard and screen-reader
 * users (the cell's accessible name lists the suggestions); a click opens the Day panel, where the actions are.
 */
export function SuggestionBubble({
  suggestion,
  code,
  onOpen,
}: {
  suggestion: TrainingSuggestion
  code: string | undefined
  onOpen: (date: string) => void
}) {
  const { t, i18n } = useTranslation('calendar')
  const { t: tType } = useTranslation('runTypes')
  const type = tType(runTypeLabelKey(code))

  return (
    <button
      type="button"
      tabIndex={-1}
      aria-hidden="true"
      data-kind="suggestion"
      data-suggestion-id={suggestion.id}
      data-intensity={suggestion.intensity}
      title={t('suggestionBubble', { type })}
      style={{ backgroundImage: HATCH }}
      onClick={(event) => {
        event.stopPropagation()
        onOpen(suggestion.date)
      }}
      className="hidden w-full items-center gap-1 rounded border border-solid border-muted-foreground/60 bg-transparent px-1 text-left text-[11px] leading-4 text-muted-foreground sm:flex"
    >
      <Sparkles className="size-3 shrink-0" />
      <span className="truncate">{type}</span>
      {suggestion.distanceMeters ? (
        <span className="ml-auto shrink-0">{formatDistance(suggestion.distanceMeters, i18n.language, 1)}</span>
      ) : null}
      <span className="hidden shrink-0 rounded-sm bg-background px-1 text-[9px] text-foreground leading-3 font-medium uppercase lg:inline">
        {t('legend.suggestedBadge')}
      </span>
    </button>
  )
}

/** Phone marker of a suggestion: a sparkles glyph. */
export function SuggestionDot({ suggestion }: { suggestion: TrainingSuggestion }) {
  return <Sparkles data-kind="suggestion" data-suggestion-id={suggestion.id} className="size-3 text-muted-foreground" />
}
