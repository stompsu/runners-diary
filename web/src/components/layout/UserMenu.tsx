import { useTranslation } from 'react-i18next'
import { Button } from '@/components/ui/button'
import { signOutEntra, useSession } from '@/auth/session'

/** Account area of the header. Guests end their demo from the banner; real users sign out here. */
export function UserMenu() {
  const { t } = useTranslation('auth')
  const session = useSession()
  if (session.kind !== 'entra') return null

  return (
    <div className="flex items-center gap-2 text-sm">
      {session.name && (
        <span className="text-muted-foreground">{t('userMenu.signedInAs', { name: session.name })}</span>
      )}
      <Button variant="outline" size="sm" className="min-h-9" onClick={() => void signOutEntra()}>
        {t('userMenu.signOut')}
      </Button>
    </div>
  )
}
