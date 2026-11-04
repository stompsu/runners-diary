import {
  AlertTriangle,
  BookOpen,
  CheckCircle2,
  FileText,
  Info,
  Lightbulb,
  Sparkles,
  type LucideIcon,
} from 'lucide-react'
import { forwardRef } from 'react'
import { useTranslation } from 'react-i18next'
import { Badge } from '@/components/ui/badge'
import type { SummaryContent, SummaryResponse, SummaryText } from '../insightsTypes'
import { useInsightsT } from '../useInsightsT'
import { formatCreated, formatSummaryValue } from './summaryFormat'
import { renderSummaryText } from './summaryText'

const SOURCE_BADGE: Record<SummaryContent['source'], { icon: LucideIcon; key: 'model' | 'demo' | 'template' }> = {
  model: { icon: Sparkles, key: 'model' },
  demo: { icon: BookOpen, key: 'demo' },
  template: { icon: FileText, key: 'template' },
}

/** A hook so that every text of a summary is formatted with the same locale and translators. */
function useSummaryText(summary: SummaryContent) {
  const { t, i18n } = useInsightsT()
  const { t: tType } = useTranslation('runTypes')
  const ctx = { locale: i18n.language, t, tType }
  return (text: SummaryText) =>
    renderSummaryText(text.text, summary.values, (key, value) => formatSummaryValue(key, value, ctx))
}

interface Props {
  response: SummaryResponse
  summary: SummaryContent
  zone: string | undefined
}

/**
 * The summary itself: source badge, headline (`h3`, the focus target after a generation), the lists with visible
 * headings and icons (no tone colours), the stale note and the footer with the disclosure. Texts are React text nodes.
 */
export const SummaryBody = forwardRef<HTMLHeadingElement, Props>(function SummaryBody(
  { response, summary, zone },
  ref,
) {
  const { t, i18n } = useInsightsT()
  const text = useSummaryText(summary)
  const badge = SOURCE_BADGE[summary.source]
  const BadgeIcon = badge.icon
  const stale = response.state === 'stale'
  const created = summary.createdAt ? formatCreated(summary.createdAt, i18n.language, zone) : null

  const sections: { id: 'wentWell' | 'watchOuts' | 'dataNotes'; icon: LucideIcon; items: SummaryText[] }[] = [
    { id: 'wentWell', icon: CheckCircle2, items: summary.wentWell },
    { id: 'watchOuts', icon: AlertTriangle, items: summary.watchOuts },
    { id: 'dataNotes', icon: Info, items: summary.dataNotes },
  ]

  return (
    <div className="flex flex-col gap-3" data-source={summary.source} data-state={response.state}>
      <Badge variant="secondary" className="h-auto whitespace-normal py-1 text-sm" data-testid="summary-badge">
        <BadgeIcon aria-hidden="true" />
        {t(`summary.badge.${badge.key}`)}
      </Badge>

      {stale ? (
        <p className="flex items-start gap-2 rounded-md border border-dashed p-2 text-sm" data-testid="summary-stale">
          <Info className="mt-0.5 size-4 shrink-0" aria-hidden="true" />
          {created ? t('summary.stale.note', { date: created }) : t('summary.stale.noDate')}
        </p>
      ) : null}

      <h3 ref={ref} tabIndex={-1} className="text-base font-semibold outline-none focus-visible:underline sm:text-lg">
        {text(summary.headline)}
      </h3>

      {sections.map(({ id, icon: Icon, items }) =>
        items.length > 0 ? (
          <div key={id} className="flex flex-col gap-1" data-section={id}>
            <h4 className="flex items-center gap-2 text-sm font-medium">
              <Icon className="size-4 shrink-0" aria-hidden="true" />
              {t(`summary.sections.${id}`)}
            </h4>
            <ul className="ms-6 list-disc space-y-1 text-sm">
              {items.map((item, index) => (
                <li key={index}>{text(item)}</li>
              ))}
            </ul>
          </div>
        ) : null,
      )}

      {summary.suggestion ? (
        <div className="flex flex-col gap-1" data-section="suggestion">
          <h4 className="flex items-center gap-2 text-sm font-medium">
            <Lightbulb className="size-4 shrink-0" aria-hidden="true" />
            {t('summary.sections.suggestion')}
          </h4>
          <p className="text-sm">{text(summary.suggestion)}</p>
        </div>
      ) : null}

      <SummaryFooter summary={summary} created={created} />
    </div>
  )
})

function SummaryFooter({ summary, created }: { summary: SummaryContent; created: string | null }) {
  const { t } = useInsightsT()
  // The template note never names a model; the demo example has no date, model or prompt either.
  const details = [
    created ? t('summary.footer.generated', { date: created }) : null,
    summary.model ? t('summary.footer.model', { model: summary.model }) : null,
    summary.promptVersion !== null ? t('summary.footer.prompt', { version: summary.promptVersion }) : null,
  ].filter((part): part is string => part !== null)

  return (
    <footer className="flex flex-col gap-1 border-t pt-2 text-xs text-muted-foreground" data-testid="summary-footer">
      {summary.source === 'demo' ? <p>{t('summary.footer.demo')}</p> : null}
      {details.length > 0 ? <p>{details.join(' · ')}</p> : null}
      <p>
        {summary.source === 'template' ? null : `${t('summary.footer.aiDisclosure')} `}
        {t('summary.footer.notMedical')}
      </p>
    </footer>
  )
}
