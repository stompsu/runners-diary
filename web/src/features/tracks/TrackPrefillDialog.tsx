import { TriangleAlert } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router'
import { ResponsiveDialog } from '@/components/ResponsiveDialog'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { QueryError } from '@/features/errors/QueryError'
import type { PrefillState, RunDto, RunTypeRules } from '@/features/runs/runFormTypes'
import { dayLabel } from '@/lib/dates/plainDate'
import {
  selectedPatch,
  trackSuggestions,
  type DurationOption,
  type Suggestion,
  type SuggestionId,
} from './trackSuggestions'
import { useTrack } from './trackQueries'
import '@/i18n/lazyBundles'

export interface TrackPrefillDialogProps {
  run: RunDto
  rules: RunTypeRules
  trackId: string
  /** `/diary/:year/:month` */
  base: string
  /** Opened right after an upload: when nothing differs there is nothing to review, so it closes itself. */
  fromUpload?: boolean
  onClose: () => void
  returnFocusTo?: () => HTMLElement | null | undefined
}

/**
 * Review of the values a GPX track proposes (ADR 0023 §3): the run's current value next to the track's, one checkbox
 * per row. Apply opens the edit form with the selected values set and dirty; nothing is saved here.
 */
export function TrackPrefillDialog(props: TrackPrefillDialogProps) {
  const { run, trackId, onClose, returnFocusTo } = props
  const { t } = useTranslation('tracks')
  const track = useTrack(run.id, trackId)

  return (
    <ResponsiveDialog
      title={t('prefill.title')}
      description={track.data ? t('prefill.description', { file: track.data.originalFileName }) : undefined}
      onClose={onClose}
      size="md"
      panel="track-prefill"
      returnFocusTo={returnFocusTo}
    >
      {track.isError ? (
        <QueryError error={track.error} onRetry={() => void track.refetch()} />
      ) : !track.data ? (
        <Skeleton className="h-40 w-full" />
      ) : (
        <PrefillBody {...props} track={track.data} />
      )}
    </ResponsiveDialog>
  )
}

function PrefillBody({
  run,
  rules,
  base,
  fromUpload,
  onClose,
  track,
}: TrackPrefillDialogProps & { track: NonNullable<ReturnType<typeof useTrack>['data']> }) {
  const { t, i18n } = useTranslation('tracks')
  const navigate = useNavigate()
  const rows = useMemo(() => trackSuggestions(run, track, rules, i18n.language), [run, track, rules, i18n.language])
  const [selected, setSelected] = useState<Set<SuggestionId>>(
    () => new Set(rows.filter((row) => row.checked).map((row) => row.id)),
  )
  const [durationChoice, setDurationChoice] = useState<DurationOption['key']>('elapsed')

  const nothingToReview = rows.length === 0 && !rules.isRestDay
  useEffect(() => {
    if (fromUpload && nothingToReview) onClose()
  }, [fromUpload, nothingToReview, onClose])

  const patch = selectedPatch(rows, selected, durationChoice)
  const toggle = (id: SuggestionId, on: boolean) =>
    setSelected((current) => {
      const next = new Set(current)
      if (on) next.add(id)
      else next.delete(id)
      return next
    })

  function apply() {
    const state: PrefillState & { returnFocus: string } = {
      prefill: { patch, source: track.originalFileName },
      returnFocus: `[data-panel="details"] [data-use-values="${track.id}"]`,
    }
    void navigate(`${base}/runs/${run.id}/edit`, { state })
  }

  if (rules.isRestDay) return <p>{t('prefill.restDay')}</p>
  if (track.status !== 'Processed' || !track.summary) return <p>{t('section.notProcessed')}</p>
  if (rows.length === 0) {
    return (
      <div className="flex flex-col gap-4">
        <p>{t('prefill.nothing')}</p>
        <div className="flex justify-end">
          <Button type="button" className="h-11 sm:h-9" onClick={onClose}>
            {t('actions.close')}
          </Button>
        </div>
      </div>
    )
  }

  return (
    <div className="flex flex-col gap-4">
      <p className="text-sm text-muted-foreground">{t('prefill.notSaved')}</p>
      <ul className="flex flex-col gap-3">
        {rows.map((row) => (
          <PrefillRow
            key={row.id}
            row={row}
            checked={selected.has(row.id)}
            onToggle={(on) => toggle(row.id, on)}
            durationChoice={durationChoice}
            onDurationChoice={setDurationChoice}
            language={i18n.language}
          />
        ))}
      </ul>
      <div className="flex flex-wrap justify-end gap-2">
        <Button type="button" variant="outline" className="h-11 sm:h-9" onClick={onClose}>
          {t('actions.cancel')}
        </Button>
        <Button type="button" className="h-11 sm:h-9" disabled={selected.size === 0} onClick={apply}>
          {t('prefill.apply')}
        </Button>
      </div>
    </div>
  )
}

function PrefillRow({
  row,
  checked,
  onToggle,
  durationChoice,
  onDurationChoice,
  language,
}: {
  row: Suggestion
  checked: boolean
  onToggle: (on: boolean) => void
  durationChoice: DurationOption['key']
  onDurationChoice: (key: DurationOption['key']) => void
  language: string
}) {
  const { t } = useTranslation('tracks')
  const label = t(`prefill.rows.${row.id}`)
  const warningId = `prefill-${row.id}-warning`
  const chosen = row.durationOptions?.find((option) => option.key === durationChoice)

  return (
    <li className="rounded-lg border p-3" data-row={row.id}>
      <label className="flex min-h-11 cursor-pointer items-start gap-3 sm:min-h-9">
        <input
          type="checkbox"
          checked={checked}
          aria-describedby={row.dateMismatch ? warningId : undefined}
          onChange={(event) => onToggle(event.target.checked)}
          className="mt-1 size-5 shrink-0 sm:size-4"
        />
        <span className="flex min-w-0 flex-col gap-0.5">
          <span className="font-medium">{label}</span>
          {row.id === 'complete' ? (
            <span className="text-sm text-muted-foreground">{t('prefill.completeHint')}</span>
          ) : (
            <span className="text-sm">
              <span className="text-muted-foreground">{t('prefill.current')}: </span>
              {row.current ?? t('prefill.empty')}
              <span aria-hidden="true">{' → '}</span>
              <span className="sr-only">{', '}</span>
              <span className="text-muted-foreground">{t('prefill.fromTrack')}: </span>
              <span className="font-medium">{chosen ? chosen.text : row.proposed}</span>
            </span>
          )}
        </span>
      </label>
      {row.durationOptions ? (
        <div role="radiogroup" aria-label={t('prefill.durationChoice')} className="mt-2 flex flex-wrap gap-2 pl-8">
          {row.durationOptions.map((option) => (
            <label
              key={option.key}
              className="flex min-h-11 cursor-pointer items-center gap-2 rounded-md border px-3 has-checked:border-primary has-checked:bg-primary/5 has-focus-visible:ring-[3px] has-focus-visible:ring-ring/50 sm:min-h-9"
            >
              <input
                type="radio"
                name="prefill-duration"
                checked={durationChoice === option.key}
                onChange={() => onDurationChoice(option.key)}
                className="size-4"
              />
              {t(`prefill.duration.${option.key}`, { time: option.text })}
            </label>
          ))}
        </div>
      ) : null}
      {row.dateMismatch ? (
        <p id={warningId} className="mt-2 flex items-start gap-2 rounded-md bg-amber-50 p-2 text-sm text-amber-950">
          <TriangleAlert className="mt-0.5 size-4 shrink-0" aria-hidden="true" />
          <span>
            {t('prefill.dateMismatch', {
              trackDate: dayLabel(row.dateMismatch.trackDate, language),
              runDate: dayLabel(row.dateMismatch.runDate, language),
            })}
          </span>
        </p>
      ) : null}
    </li>
  )
}
