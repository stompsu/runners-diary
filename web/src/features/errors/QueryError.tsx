import { CircleAlert } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Button } from '@/components/ui/button'
import { parseProblem } from '@/lib/api/problem'
import { loadProblemMessage } from './problemMessage'

/** Inline error state for a failed query: localized reason and a Retry button. */
export function QueryError({ error, onRetry }: { error: unknown; onRetry: () => void }) {
  const { t } = useTranslation('errors')

  return (
    <div role="alert" className="flex flex-col items-start gap-3 rounded-md border border-destructive/50 p-4">
      <p className="flex items-center gap-2 font-medium">
        <CircleAlert className="size-4 shrink-0 text-destructive" aria-hidden="true" />
        {t('loadFailedTitle')}
      </p>
      <p className="text-sm text-muted-foreground">{loadProblemMessage(parseProblem(error), t)}</p>
      <Button type="button" variant="outline" onClick={onRetry}>
        {t('retry')}
      </Button>
    </div>
  )
}
