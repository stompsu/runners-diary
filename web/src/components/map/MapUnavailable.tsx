import { MapPinOff } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { cn } from '@/lib/utils'
import '@/i18n/lazyBundles'

/** The text-first stand-in for the map (no WebGL2, or the engine/style failed). The text alternatives stay on the page. */
export function MapUnavailable({ reason, className }: { reason: 'noWebGl' | 'error'; className?: string }) {
  const { t } = useTranslation('maps')
  return (
    <output
      className={cn(
        'flex size-full flex-col items-center justify-center gap-2 bg-muted p-4 text-center text-sm text-foreground',
        className,
      )}
    >
      <MapPinOff className="size-6" aria-hidden="true" />
      <p className="font-semibold">{t('unavailable.title')}</p>
      <p>{t(`unavailable.${reason}`)}</p>
    </output>
  )
}
