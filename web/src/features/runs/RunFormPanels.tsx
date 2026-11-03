import { useMemo } from 'react'
import { useTranslation } from 'react-i18next'
import { useParams, useSearchParams } from 'react-router'
import { ResponsiveDialog } from '@/components/ResponsiveDialog'
import { Skeleton } from '@/components/ui/skeleton'
import { useMe } from '@/auth/useMe'
import { useRunTypeCodes } from '@/features/calendar/calendarQueries'
import { QueryError } from '@/features/errors/QueryError'
import { isValid, todayFromMe } from '@/lib/dates/plainDate'
import { parseProblem } from '@/lib/api/problem'
import { RunForm } from './RunForm'
import { RunGone } from './RunGone'
import { defaultFormValues } from './runFormMapping'
import { useRun } from './runQueries'
import { useClosePanel } from './usePanel'

/** `/diary/:year/:month/runs/new?date=YYYY-MM-DD` */
export function RunNewPanel() {
  const { t } = useTranslation('runs')
  const close = useClosePanel()
  const [search] = useSearchParams()
  const me = useMe()
  const { query: types, codes } = useRunTypeCodes()
  const requested = search.get('date')
  const date = requested && isValid(requested) ? requested : me.data ? todayFromMe(me.data) : null

  const initialValues = useMemo(() => {
    if (!date || !types.data) return null
    const easy = types.data.find((type) => type.code === 'easy') ?? types.data[0]
    return easy ? defaultFormValues({ date, runTypeId: easy.id }) : null
  }, [date, types.data])

  const failed = [me, types].find((query) => query.isError)

  return (
    <ResponsiveDialog title={t('form.addTitle')} onClose={close} size="lg" restoreFocusDate={date} panel="run-form">
      {failed ? (
        <QueryError
          error={failed.error}
          onRetry={() => {
            for (const query of [me, types]) if (query.isError) void query.refetch()
          }}
        />
      ) : initialValues && types.data && me.data ? (
        <RunForm
          initialValues={initialValues}
          types={types.data}
          codes={codes}
          profileZone={me.data.timeZoneId}
          onSaved={close}
          onGone={close}
          onCancel={close}
        />
      ) : (
        <Skeleton className="h-64 w-full" />
      )}
    </ResponsiveDialog>
  )
}

/** `/diary/:year/:month/runs/:runId/edit[?complete=1]` */
export function RunEditPanel() {
  const { t } = useTranslation('runs')
  const { runId = '' } = useParams()
  const [search] = useSearchParams()
  const close = useClosePanel()
  const me = useMe()
  const { query: types, codes } = useRunTypeCodes()
  const run = useRun(runId)

  if (run.isError && parseProblem(run.error).kind === 'notFound') return <RunGone onClose={close} />

  const failed = [run, me, types].find((query) => query.isError)
  return (
    <ResponsiveDialog
      title={t(search.get('complete') === '1' ? 'form.completeTitle' : 'form.editTitle')}
      onClose={close}
      size="lg"
      restoreFocusDate={run.data?.date}
      panel="run-form"
    >
      {failed ? (
        <QueryError
          error={failed.error}
          onRetry={() => {
            for (const query of [run, me, types]) if (query.isError) void query.refetch()
          }}
        />
      ) : run.data && types.data && me.data ? (
        <RunForm
          // A different run (or a reload) starts a fresh form.
          key={run.data.id}
          run={run.data}
          completePreset={search.get('complete') === '1'}
          types={types.data}
          codes={codes}
          profileZone={me.data.timeZoneId}
          onSaved={close}
          onGone={close}
          onCancel={close}
        />
      ) : (
        <Skeleton className="h-64 w-full" />
      )}
    </ResponsiveDialog>
  )
}
