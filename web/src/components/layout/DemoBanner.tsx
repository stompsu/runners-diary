import { Clock } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { Button } from '@/components/ui/button'
import { signOut, useSession } from '@/auth/session'
import { useClientConfig } from '@/auth/useClientConfig'
import { useCountdown } from '@/auth/useCountdown'
import { formatDuration } from '@/lib/format/duration'

const expire = () => signOut('expired')
const endDemo = () => signOut('signedOut')

/** Guest-only banner (brief §1.3): demo notice, countdown to expiry, End demo. At zero the session ends (AC5). */
export function DemoBanner() {
  const session = useSession()
  if (session.kind !== 'guest') return null
  return <GuestBanner expiresAt={session.expiresAt} />
}

function GuestBanner({ expiresAt }: { expiresAt: number }) {
  const { t } = useTranslation('auth')
  const remaining = useCountdown(expiresAt, expire)
  const config = useClientConfig()
  const entraConfigured = config.data?.entra != null

  return (
    <section aria-label={t('demo.banner')} className="border-b bg-muted">
      <div className="mx-auto flex max-w-6xl flex-wrap items-center gap-x-4 gap-y-2 px-4 py-2 text-sm">
        <Clock className="size-4 shrink-0" aria-hidden="true" />
        <p className="flex-1">{t('demo.banner')}</p>
        {/* role=timer is not announced on every tick; the visible value is enough for a countdown. */}
        <span role="timer" className="tabular-nums">
          {t('demo.remaining', { time: formatDuration(remaining) })}
        </span>
        {entraConfigured && (
          <Button asChild variant="outline" size="sm" className="min-h-9">
            <Link to="/signin" onClick={endDemo}>
              {t('demo.signIn')}
            </Link>
          </Button>
        )}
        <Button variant="outline" size="sm" className="min-h-9" onClick={endDemo}>
          {t('demo.end')}
        </Button>
      </div>
    </section>
  )
}
