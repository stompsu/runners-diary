import { useTranslation } from 'react-i18next'
import { useClientConfig } from '@/auth/useClientConfig'
import { safeHttpUrl } from '@/features/runs/safeUrl'
import { ExternalAnchor } from './ExternalAnchor'
import { sourceName } from './raceFormat'
import '@/i18n/lazyBundles'

/** "Source: running.life", linking the race's own page at the source (or the source's home page without one). */
export function RaceSourceAttribution({ sourceId, sourceUrl }: { sourceId: string; sourceUrl: string | null }) {
  const { t } = useTranslation('races')
  const sources = useClientConfig().data?.races?.sources
  const text = t('attribution.source', { name: sourceName(sources, sourceId) })
  const url = safeHttpUrl(sourceUrl) ?? safeHttpUrl(sources?.find((source) => source.id === sourceId)?.url)

  return (
    <p className="text-sm text-muted-foreground">
      {url ? (
        <ExternalAnchor href={url} className="underline">
          {text}
          <span className="sr-only"> {t('details.actions.opensNewTab')}</span>
        </ExternalAnchor>
      ) : (
        text
      )}
      {' · '}
      {t('attribution.disclaimer')}
    </p>
  )
}
