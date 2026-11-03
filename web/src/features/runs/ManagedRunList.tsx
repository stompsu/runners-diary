import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { runTypeLabelKey } from '@/features/calendar/runTypePresentation'
import type { Run } from '@/features/calendar/types'
import type { DiaryMonth } from '@/features/diary/diaryMonth'
import { dayLabel } from '@/lib/dates/plainDate'
import { DeleteRunDialog, type DeleteTarget } from './DeleteRunDialog'
import { RunList } from './RunList'

/** `RunList` plus the delete confirmation, shared by the day panel and the month list. */
export function ManagedRunList({
  runs,
  codes,
  month,
  showDate,
}: {
  runs: Run[]
  codes: ReadonlyMap<number, string>
  month: DiaryMonth
  showDate?: boolean
}) {
  const { t, i18n } = useTranslation('runs')
  const { t: tType } = useTranslation('runTypes')
  const [target, setTarget] = useState<DeleteTarget | null>(null)
  // Kept after the dialog closes: the dialog reads it while handing the focus back.
  const [origin, setOrigin] = useState<HTMLElement | null>(null)

  return (
    <>
      <RunList
        runs={runs}
        codes={codes}
        month={month}
        showDate={showDate}
        onDelete={(run, trigger) => {
          setOrigin(trigger)
          setTarget({
            id: run.id,
            summary: t('delete.summary', {
              type: tType(runTypeLabelKey(codes.get(run.runTypeId))),
              date: dayLabel(run.date, i18n.language),
            }),
          })
        }}
      />
      <DeleteRunDialog target={target} origin={origin} onClose={() => setTarget(null)} onDeleted={() => undefined} />
    </>
  )
}
