import { ArrowDown, ArrowUp, Minus } from 'lucide-react'
import { deltaDirection, formatDeltaAmount, formatDeltaPercent, type DeltaKind } from './insightsFormat'
import type { Delta } from './insightsTypes'
import { useInsightsT } from './useInsightsT'

/**
 * A change against the comparison period. The direction is written in words ("Up", "Down", "Slower", "Faster", "No change"); the arrow
 * is decoration, and there is deliberately no good/bad colour: more load is not better or worse in itself.
 */
export function DeltaText({ kind, delta }: { kind: DeltaKind; delta: Delta }) {
  const { t, i18n } = useInsightsT()
  const direction = deltaDirection(kind, delta)
  const Icon = direction === 'up' ? ArrowUp : direction === 'down' ? ArrowDown : Minus
  // More seconds per km is slower, so a pace delta is worded "Slower"/"Faster", never "Up"/"Down".
  const key =
    kind === 'pace'
      ? direction === 'up'
        ? 'delta.slower'
        : 'delta.faster'
      : direction === 'up'
        ? 'delta.up'
        : 'delta.down'
  const text =
    direction === 'same'
      ? t('delta.same')
      : t(key, {
          amount: formatDeltaAmount(kind, delta.absolute, i18n.language),
          percent: formatDeltaPercent(delta.percent, i18n.language),
        })

  return (
    <span className="inline-flex items-center gap-1 text-sm text-muted-foreground" data-direction={direction}>
      <Icon className="size-3.5 shrink-0" aria-hidden="true" />
      <span>{text}</span>
    </span>
  )
}
