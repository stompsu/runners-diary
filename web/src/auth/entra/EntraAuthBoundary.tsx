import { useEffect } from 'react'
import { finishRestoring, getSession, registerEntraController, startEntraSession } from '../session'
import { bootEntra, isUserCancel, type EntraConfig } from './msalClient'

/**
 * Lazy MSAL boundary (ADR 0014 §1): loaded only when client-config contains Entra settings. Renders nothing; it boots
 * MSAL, completes a pending redirect sign-in, restores the cached account after a reload, and registers the controller
 * the session store uses for tokens, login and logout.
 */
export default function EntraAuthBoundary({ config }: { config: EntraConfig }) {
  const { clientId, authority, apiScope } = config

  useEffect(() => {
    let cancelled = false
    bootEntra({ clientId, authority, apiScope })
      .then(({ controller, account, redirectError }) => {
        if (cancelled) return
        registerEntraController(controller)
        if (getSession().kind !== 'restoring') return
        if (account && !redirectError) startEntraSession(account.name ?? account.username ?? null)
        else finishRestoring(isUserCancel(redirectError) ? null : 'sessionEnded')
      })
      .catch((error: unknown) => {
        console.error('Entra sign-in could not be initialised', error)
        if (!cancelled) finishRestoring('sessionEnded')
      })
    return () => {
      cancelled = true
    }
  }, [clientId, authority, apiScope])

  return null
}
