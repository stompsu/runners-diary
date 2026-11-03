import { useTranslation } from 'react-i18next'
import { ResponsiveDialog } from '@/components/ResponsiveDialog'
import { Button } from '@/components/ui/button'

/** Shown instead of a run's dialog when the run no longer exists (404): deleted elsewhere, or a stale link. */
export function RunGone({ onClose, restoreFocusDate }: { onClose: () => void; restoreFocusDate?: string | null }) {
  const { t } = useTranslation('runs')
  const { t: tErrors } = useTranslation('errors')
  return (
    <ResponsiveDialog
      title={t('gone.title')}
      description={tErrors('problem.notFound')}
      onClose={onClose}
      restoreFocusDate={restoreFocusDate}
      panel="gone"
      footer={
        <Button type="button" className="h-11 sm:h-9" onClick={onClose}>
          {t('actions.close')}
        </Button>
      }
    >
      <span className="sr-only">{t('gone.title')}</span>
    </ResponsiveDialog>
  )
}
