import { CircleAlert } from 'lucide-react'
import { useEffect, useRef, useState, type Ref } from 'react'
import { useMe } from '@/auth/useMe'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import type { SummaryResponse } from '../insightsTypes'
import { useInsightsT } from '../useInsightsT'
import { SummaryActions } from './SummaryActions'
import { SummaryBody } from './SummaryBody'
import { SummaryStatus } from './SummaryStatus'
import { formatResetTime } from './summaryFormat'
import { isRetryable, type SummaryError } from './summaryProblem'
import { useGenerateSummary, useInsightsSummary, type SummaryRange } from './summaryQueries'
import { hasSummaryAction } from './summaryState'

/**
 * The AI summary of the period (M6 §3.6). Generation happens only on a click (Generate / Regenerate); the card
 * otherwise shows what the server stored, the demo example or the automatic note. Remount it per period (`key`):
 * a pending generation is aborted when the period changes.
 */
export function SummaryCard({ range }: { range: SummaryRange }) {
  const { t } = useInsightsT()
  const me = useMe()
  const zone = me.data?.timeZoneId
  const query = useInsightsSummary(range)
  const gen = useGenerateSummary(range)
  const lastRegenerate = useRef(false)

  const headlineRef = useRef<HTMLHeadingElement>(null)
  const actionRef = useRef<HTMLButtonElement>(null)
  const retryRef = useRef<HTMLButtonElement>(null)

  // Focus: Generate unmounts while generating (focus moves to Cancel, see `Generating`). When the request ends the user
  // lands on the new headline, back on the button after a cancel, or on "Try again" after an error. Keyed on the attempt,
  // not on a pending -> settled edge: a fast answer may never render the pending state.
  const focused = useRef(0)
  useEffect(() => {
    if (gen.isPending || gen.attempt === 0 || focused.current === gen.attempt) return
    focused.current = gen.attempt
    const target = gen.isSuccess ? headlineRef : gen.aborted ? actionRef : retryRef
    ;(target.current ?? actionRef.current)?.focus()
  }, [gen.isPending, gen.attempt, gen.isSuccess, gen.aborted])

  function run(regenerate: boolean) {
    lastRegenerate.current = regenerate
    gen.generate(regenerate)
  }

  const message = gen.isPending
    ? t('summary.status.generating')
    : gen.isSuccess
      ? t('summary.status.done')
      : gen.aborted
        ? t('summary.status.cancelled')
        : ''

  return (
    <section aria-labelledby="ai-summary-title" className="flex flex-col gap-3" data-testid="summary-card">
      <h2 id="ai-summary-title" className="text-lg font-semibold">
        {t('summary.title')}
      </h2>
      {/* The live region is a sibling of the busy content: NVDA/JAWS may suppress announcements inside aria-busy. */}
      <SummaryStatus message={message} />
      <div className="flex flex-col gap-3 rounded-lg border p-3 sm:p-4" aria-busy={gen.isPending || query.isPending}>
        {gen.isPending ? (
          <Generating onCancel={gen.cancel} />
        ) : query.data ? (
          <CardContent
            response={query.data}
            zone={zone}
            error={gen.error}
            onGenerate={run}
            onRetry={() => run(lastRegenerate.current)}
            headlineRef={headlineRef}
            actionRef={actionRef}
            retryRef={retryRef}
          />
        ) : query.isError ? (
          <div role="alert" className="flex flex-col items-start gap-3" data-testid="summary-load-error">
            <p className="flex items-center gap-2 text-sm font-medium">
              <CircleAlert className="size-4 shrink-0 text-destructive" aria-hidden="true" />
              {t('summary.loadFailed')}
            </p>
            <Button
              type="button"
              variant="outline"
              className="min-h-11 sm:min-h-9"
              onClick={() => void query.refetch()}
            >
              {t('summary.retry')}
            </Button>
          </div>
        ) : (
          <output aria-label={t('summary.loading')} className="flex flex-col gap-2">
            <Skeleton className="h-6 w-2/3" />
            <Skeleton className="h-4 w-full" />
            <Skeleton className="h-4 w-5/6" />
          </output>
        )}
      </div>
    </section>
  )
}

/** The wait for the model: a skeleton, the seconds so far and Cancel, which takes the focus Generate had. */
function Generating({ onCancel }: { onCancel: () => void }) {
  const { t } = useInsightsT()
  const [seconds, setSeconds] = useState(0)
  const cancelRef = useRef<HTMLButtonElement>(null)

  useEffect(() => {
    cancelRef.current?.focus()
    const timer = setInterval(() => setSeconds((value) => value + 1), 1000)
    return () => clearInterval(timer)
  }, [])

  return (
    <div className="flex flex-col gap-3" data-testid="summary-generating">
      <p className="text-sm font-medium">{t('summary.generating.title')}</p>
      <div className="flex flex-col gap-2" aria-hidden="true">
        <Skeleton className="h-6 w-2/3" />
        <Skeleton className="h-4 w-full" />
        <Skeleton className="h-4 w-5/6" />
      </div>
      <div className="flex flex-wrap items-center gap-x-3 gap-y-2">
        <Button ref={cancelRef} type="button" variant="outline" className="min-h-11 sm:min-h-9" onClick={onCancel}>
          {t('summary.actions.cancel')}
        </Button>
        <span className="text-sm text-muted-foreground" data-testid="summary-elapsed">
          {t('summary.generating.elapsed', { seconds })}
        </span>
      </div>
    </div>
  )
}

interface ContentProps {
  response: SummaryResponse
  zone: string | undefined
  error: SummaryError | null
  onGenerate: (regenerate: boolean) => void
  onRetry: () => void
  headlineRef: Ref<HTMLHeadingElement>
  actionRef: Ref<HTMLButtonElement>
  retryRef: Ref<HTMLButtonElement>
}

function CardContent({ response, zone, error, onGenerate, onRetry, headlineRef, actionRef, retryRef }: ContentProps) {
  const { t } = useInsightsT()
  const { summary, generation } = response
  const retryable = error !== null && isRetryable(error)

  return (
    <>
      {error ? (
        <SummaryErrorAlert error={error} zone={zone} onRetry={retryable ? onRetry : null} retryRef={retryRef} />
      ) : null}

      {summary ? (
        <SummaryBody ref={headlineRef} response={response} summary={summary} zone={zone} />
      ) : generation.availability === 'notConfigured' ? (
        <p className="text-sm" data-testid="summary-not-configured">
          {t('summary.notConfigured')}
        </p>
      ) : generation.availability === 'notNeeded' ? (
        <p className="text-sm">{t('summary.notNeeded')}</p>
      ) : (
        <div className="flex flex-col gap-1" data-testid="summary-none">
          <h3 className="text-base font-semibold">{t('summary.none.title')}</h3>
          <p className="text-sm text-muted-foreground">{t('summary.none.body')}</p>
        </div>
      )}

      {summary && generation.availability === 'notConfigured' ? (
        <p className="text-sm text-muted-foreground">{t('summary.notConfiguredHint')}</p>
      ) : null}

      {/* While the alert offers "Try again" the card has one action only. */}
      {hasSummaryAction(response) && !retryable ? (
        <SummaryActions ref={actionRef} response={response} zone={zone} onGenerate={onGenerate} />
      ) : null}
    </>
  )
}

function errorMessage(
  error: SummaryError,
  zone: string | undefined,
  locale: string,
  t: ReturnType<typeof useInsightsT>['t'],
) {
  switch (error.kind) {
    case 'inProgress':
      return t('summary.errors.inProgress')
    case 'quota': {
      const time = error.resetsAt ? formatResetTime(error.resetsAt, locale, zone) : null
      if (error.scope === 'global') {
        return time ? t('summary.errors.quotaGlobalReset', { time }) : t('summary.errors.quotaGlobal')
      }
      return time ? t('summary.errors.quotaAccountReset', { time }) : t('summary.errors.quotaAccount')
    }
    case 'rateLimited':
      return error.retryAfterSeconds === null
        ? t('summary.errors.rateLimited')
        : t('summary.errors.rateLimitedWait', { seconds: error.retryAfterSeconds })
    case 'failed':
      return error.reason === 'invalidOutput'
        ? t('summary.errors.failedInvalidOutput')
        : error.reason === 'timeout'
          ? t('summary.errors.failedTimeout')
          : error.reason === 'provider'
            ? t('summary.errors.failedProvider')
            : t('summary.errors.failed')
    case 'unavailable':
      return t('summary.errors.unavailable')
    case 'busy':
      return t('summary.errors.busy')
    case 'network':
      return t('summary.errors.network')
    case 'generic':
      return t('summary.errors.generic')
  }
}

function SummaryErrorAlert({
  error,
  zone,
  onRetry,
  retryRef,
}: {
  error: SummaryError
  zone: string | undefined
  onRetry: (() => void) | null
  retryRef: Ref<HTMLButtonElement>
}) {
  const { t, i18n } = useInsightsT()
  return (
    <div
      role="alert"
      className="flex flex-col items-start gap-3 rounded-md border border-destructive/50 p-3"
      data-testid="summary-error"
      data-kind={error.kind}
    >
      <p className="flex items-start gap-2 text-sm">
        <CircleAlert className="mt-0.5 size-4 shrink-0 text-destructive" aria-hidden="true" />
        {errorMessage(error, zone, i18n.language, t)}
      </p>
      {onRetry ? (
        <Button ref={retryRef} type="button" variant="outline" className="min-h-11 sm:min-h-9" onClick={onRetry}>
          {t('summary.actions.retry')}
        </Button>
      ) : null}
    </div>
  )
}
