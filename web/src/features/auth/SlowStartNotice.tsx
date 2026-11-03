import { useTranslation } from 'react-i18next'
import { useDelayedFlag } from '@/auth/useDelayedFlag'

export const SLOW_START_DELAY_MS = 3000

/** "Waking up the server…" after 3 s of waiting (the Container Apps API scales to zero). Polite live region. */
export function SlowStartNotice({ pending }: { pending: boolean }) {
  const { t } = useTranslation('auth')
  const slow = useDelayedFlag(pending, SLOW_START_DELAY_MS)

  return <output className="block min-h-6 text-sm text-muted-foreground">{slow ? t('signIn.slowStart') : null}</output>
}
