import { Flag } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { FAMILY_ICON } from './runTypePresentation'

const Trophy = FAMILY_ICON.race

/**
 * What the bubbles mean: completed = filled, planned = dashed, race = trophy, race proposal = dotted with a flag.
 * Every entry pairs a shape with its label, so no meaning rests on colour. The proposal entry is there only while the
 * proposals are shown.
 */
export function CalendarLegend({ showProposals }: { showProposals: boolean }) {
  const { t } = useTranslation('calendar')
  return (
    <ul aria-label={t('legend.title')} className="mt-3 flex flex-wrap items-center gap-x-5 gap-y-1 text-xs">
      <li className="flex items-center gap-1.5" data-legend="completed">
        <span aria-hidden="true" className="h-3 w-5 rounded border-2 border-solid border-foreground bg-foreground" />
        {t('legend.completed')}
      </li>
      <li className="flex items-center gap-1.5" data-legend="planned">
        <span aria-hidden="true" className="h-3 w-5 rounded border-2 border-dashed border-foreground bg-transparent" />
        {t('legend.planned')}
      </li>
      <li className="flex items-center gap-1.5" data-legend="race">
        <Trophy aria-hidden="true" className="size-3.5" />
        {t('legend.race')}
      </li>
      {showProposals ? (
        <li className="flex items-center gap-1.5" data-legend="proposal">
          <span
            aria-hidden="true"
            className="flex h-4 w-6 items-center justify-center rounded border border-dotted border-muted-foreground"
          >
            <Flag className="size-3 text-muted-foreground" />
          </span>
          {t('legend.proposal')}
        </li>
      ) : null}
    </ul>
  )
}
