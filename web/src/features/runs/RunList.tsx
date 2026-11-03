import { CircleCheck, CircleDashed } from 'lucide-react'
import { useId, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { Button } from '@/components/ui/button'
import { FAMILY_ICON, familyOf, familyStyle, runTypeLabelKey } from '@/features/calendar/runTypePresentation'
import type { Run } from '@/features/calendar/types'
import { diaryPath, type DiaryMonth } from '@/features/diary/diaryMonth'
import { dayLabel } from '@/lib/dates/plainDate'
import { cn } from '@/lib/utils'
import { returnFocusState } from './returnFocus'
import { useRunMetrics } from './useRunMetrics'

export interface RunListProps {
  runs: Run[]
  codes: ReadonlyMap<number, string>
  month: DiaryMonth
  /** Show each run's date (month list); the day panel already has it in its title. */
  showDate?: boolean
  onDelete: (run: Run, origin: HTMLElement) => void
}

export function StatusLabel({ status }: { status: Run['status'] }) {
  const { t } = useTranslation('runs')
  const completed = status === 'Completed'
  const Icon = completed ? CircleCheck : CircleDashed
  return (
    <span
      className={cn(
        'inline-flex items-center gap-1 rounded-full border-2 px-2 py-0.5 text-xs font-medium',
        completed ? 'border-solid border-foreground/70' : 'border-dashed border-foreground/60',
      )}
    >
      <Icon className="size-3.5" aria-hidden="true" />
      {t(completed ? 'status.completed' : 'status.planned')}
    </span>
  )
}

/** The runs of a day or a month with their actions: View details, Edit, Mark as completed, Rate, Delete. */
export function RunList({ runs, codes, month, showDate, onDelete }: RunListProps) {
  return (
    <ul className="flex flex-col gap-3">
      {runs.map((run) => (
        <RunListItem
          key={run.id}
          run={run}
          code={codes.get(run.runTypeId)}
          month={month}
          showDate={showDate}
          onDelete={onDelete}
        />
      ))}
    </ul>
  )
}

function RunListItem({
  run,
  code,
  month,
  showDate,
  onDelete,
}: {
  run: Run
  code: string | undefined
  month: DiaryMonth
  showDate?: boolean
  onDelete: RunListProps['onDelete']
}) {
  const { t, i18n } = useTranslation('runs')
  const { t: tType } = useTranslation('runTypes')
  const describedBy = useId()
  const metrics = useRunMetrics()(run)
  const Icon = FAMILY_ICON[familyOf(code)]
  const base = diaryPath(month)
  const typeLabel = tType(runTypeLabelKey(code))
  const row = 'h-11 sm:h-9'
  const link = { describedBy, className: row, runId: run.id }

  return (
    <li
      data-run-id={run.id}
      data-status={run.status}
      style={familyStyle(code)}
      className={cn(
        'rounded-lg border-2 border-l-8 border-(--run) p-3',
        run.status === 'Planned' ? 'border-dashed' : 'border-solid',
      )}
    >
      <div className="flex flex-wrap items-center gap-2">
        <h3 id={describedBy} className="flex min-w-0 items-center gap-1.5 font-medium">
          <Icon className="size-4 shrink-0" aria-hidden="true" />
          <span className="truncate">{run.title ? `${typeLabel} · ${run.title}` : typeLabel}</span>
        </h3>
        <StatusLabel status={run.status} />
      </div>
      {showDate ? <p className="mt-1 text-sm text-muted-foreground">{dayLabel(run.date, i18n.language)}</p> : null}
      {metrics.length > 0 ? <p className="mt-1 text-sm">{metrics.join(' · ')}</p> : null}
      <div className="mt-3 flex flex-wrap gap-2">
        <ActionLink action="details" to={`${base}/runs/${run.id}`} {...link}>
          {t('actions.view')}
        </ActionLink>
        <ActionLink action="edit" to={`${base}/runs/${run.id}/edit`} {...link}>
          {t('actions.edit')}
        </ActionLink>
        {run.status === 'Planned' ? (
          <ActionLink action="complete" to={`${base}/runs/${run.id}/edit?complete=1`} {...link}>
            {t('actions.markCompleted')}
          </ActionLink>
        ) : run.rating ? null : (
          <ActionLink action="rate" to={`${base}/runs/${run.id}?rate=1`} {...link}>
            {t('actions.rate')}
          </ActionLink>
        )}
        <Button
          type="button"
          variant="outline"
          className={cn(row, 'text-destructive')}
          aria-describedby={describedBy}
          onClick={(event) => onDelete(run, event.currentTarget)}
        >
          {t('actions.delete')}
        </Button>
      </div>
    </li>
  )
}

function ActionLink({
  to,
  action,
  runId,
  describedBy,
  className,
  children,
}: {
  to: string
  action: 'details' | 'edit' | 'complete' | 'rate'
  runId: string
  describedBy: string
  className: string
  children: ReactNode
}) {
  return (
    <Button asChild variant="outline" className={className}>
      <Link
        to={to}
        aria-describedby={describedBy}
        data-action={action}
        // Closing the panel this opens puts the focus back on this very link (AC13).
        state={returnFocusState(`[data-run-id="${runId}"] a[data-action="${action}"]`)}
      >
        {children}
      </Link>
    </Button>
  )
}
