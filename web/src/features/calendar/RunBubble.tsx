import { useTranslation } from 'react-i18next'
import { cn } from '@/lib/utils'
import { formatDistance } from '@/lib/format/units'
import { FAMILY_ICON, familyOf, familyStyle, runTypeLabelKey } from './runTypePresentation'
import type { Run } from './types'

/**
 * Desktop bubble: the type's icon (trophy for races), label and distance. Completed = filled, planned = dashed
 * outline, so status never relies on colour alone. Presentational for keyboard and screen-reader users (the cell's
 * accessible name lists the runs); mouse and touch users can click it to open the run directly.
 */
export function RunBubble({
  run,
  code,
  onOpen,
}: {
  run: Run
  code: string | undefined
  onOpen: (runId: string) => void
}) {
  const { t, i18n } = useTranslation('runTypes')
  const family = familyOf(code)
  const Icon = FAMILY_ICON[family]
  const completed = run.status === 'Completed'

  return (
    <button
      type="button"
      tabIndex={-1}
      aria-hidden="true"
      data-run-id={run.id}
      data-status={run.status}
      data-family={family}
      style={familyStyle(code)}
      onClick={(event) => {
        event.stopPropagation()
        onOpen(run.id)
      }}
      className={cn(
        'hidden w-full items-center gap-1 rounded border-2 border-(--run) px-1 text-left text-[11px] leading-4 sm:flex',
        completed ? 'border-solid bg-(--run) text-(--run-ink)' : 'border-dashed bg-transparent text-foreground',
      )}
    >
      <Icon className="size-3 shrink-0" />
      <span className="truncate">{t(runTypeLabelKey(code))}</span>
      {run.distanceMeters ? (
        <span className="ml-auto shrink-0">{formatDistance(run.distanceMeters, i18n.language, 1)}</span>
      ) : null}
    </button>
  )
}

/** Phone bubble: a dot per run (filled = completed, hollow = planned; trophy for races). */
export function RunDot({ run, code }: { run: Run; code: string | undefined }) {
  const family = familyOf(code)
  const completed = run.status === 'Completed'

  if (family === 'race') {
    const Icon = FAMILY_ICON.race
    return (
      <Icon
        data-run-id={run.id}
        data-status={run.status}
        data-family={family}
        style={familyStyle(code)}
        className={cn('size-3 text-(--run)', completed ? 'fill-(--run)' : 'fill-transparent')}
      />
    )
  }
  return (
    <span
      data-run-id={run.id}
      data-status={run.status}
      data-family={family}
      style={familyStyle(code)}
      className={cn(
        'size-2.5 rounded-full border-2 border-(--run)',
        completed ? 'bg-(--run)' : 'border-dashed bg-transparent',
      )}
    />
  )
}
