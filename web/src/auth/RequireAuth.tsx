import { useTranslation } from 'react-i18next'
import { Navigate, Outlet, useLocation } from 'react-router'
import { signInPath } from './returnTo'
import { useSession } from './session'

/**
 * Layout route that wraps everything except /signin (ADR 0014 §7). Anonymous visitors go to
 * /signin?returnTo=<where they wanted to go>&reason=<why they are signed out>. No API calls here (no loaders).
 */
export function RequireAuth() {
  const session = useSession()
  const location = useLocation()
  const { t } = useTranslation('auth')

  if (session.kind === 'restoring') {
    return <output className="block p-6 text-muted-foreground">{t('signIn.restoring')}</output>
  }

  if (session.kind === 'anonymous') {
    const reason = session.reason
    // A deliberate sign-out should not remember the page; an expiry should.
    const returnTo = reason === 'signedOut' ? null : location.pathname + location.search + location.hash
    return <Navigate to={signInPath({ returnTo, reason })} replace />
  }

  return <Outlet />
}
