import { Sparkles } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'
import { setShowSuggestions, useGoalsEnabled, useShowSuggestions } from './suggestionQueries'

/**
 * Month header control for the suggestions layer (ADR 0041 §4): hides or shows the bubbles. The choice is kept per
 * browser in `localStorage` (`rd.showSuggestions`); the Day panel still lists suggestions. Not rendered while the
 * goals feature is off.
 */
export function SuggestionsToggle() {
  const { t } = useTranslation('calendar')
  const enabled = useGoalsEnabled()
  const shown = useShowSuggestions()
  if (!enabled) return null

  return (
    <Button
      type="button"
      variant="outline"
      aria-pressed={shown}
      data-testid="suggestions-toggle"
      className={cn('h-11 sm:h-9', shown && 'border-foreground')}
      onClick={() => setShowSuggestions(!shown)}
    >
      <Sparkles aria-hidden="true" className={shown ? 'fill-current' : undefined} />
      {t('suggestionsToggle.label')}
    </Button>
  )
}
