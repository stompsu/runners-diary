import { useTranslation } from 'react-i18next'
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog'
import '@/i18n/lazyBundles'

/** "Generating replaces the suggestions of X": confirmed before sending `replaceOtherGoals: true`. */
export function ReplaceOtherGoalDialog({
  open,
  otherGoalTitle,
  onConfirm,
  onCancel,
}: {
  open: boolean
  otherGoalTitle: string | null
  onConfirm: () => void
  onCancel: () => void
}) {
  const { t } = useTranslation('goals')
  return (
    <AlertDialog open={open} onOpenChange={(next) => (next ? undefined : onCancel())}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{t('replace.title')}</AlertDialogTitle>
          <AlertDialogDescription>
            {otherGoalTitle ? t('replace.description', { goal: otherGoalTitle }) : t('replace.descriptionUnknown')}
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel className="h-11 sm:h-9">{t('replace.cancel')}</AlertDialogCancel>
          <AlertDialogAction className="h-11 sm:h-9" onClick={onConfirm}>
            {t('replace.confirm')}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  )
}
