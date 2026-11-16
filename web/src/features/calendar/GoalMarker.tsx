import { Target } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import type { GoalDto } from '@/features/goals/goalTypes'

/**
 * A `Target` icon next to the day number on an Active goal's target date (ADR 0041 §4). Decorative: the cell's
 * accessible name carries "goal: <title>"; the `title` attribute is the mouse tooltip.
 */
export function GoalMarker({ goal }: { goal: GoalDto }) {
  const { t } = useTranslation('calendar')
  return (
    <span
      aria-hidden="true"
      data-kind="goal"
      data-goal-id={goal.id}
      title={t('goalMarker', { title: goal.title })}
      className="ml-0.5 inline-flex size-5 items-center justify-center align-middle text-foreground"
    >
      <Target className="size-3.5" />
    </span>
  )
}
