import { CircleAlert, Info } from 'lucide-react'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Navigate, useSearchParams } from 'react-router'
import { useMutation } from '@tanstack/react-query'
import { fetchClient } from '@/api/client'
import { Button } from '@/components/ui/button'
import { sanitizeReturnTo } from '@/auth/returnTo'
import { isSignOutReason, signInWithEntra, startGuestSession, useEntraReady, useSession } from '@/auth/session'
import { useClientConfig } from '@/auth/useClientConfig'
import { parseProblem } from '@/lib/api/problem'
import { SlowStartNotice } from './SlowStartNotice'
import { guestCreationMessage } from './signInProblem'

/** The only public route: "Try as guest" always, "Sign in" only when the API reports Entra settings (ADR 0015). */
export function SignInPage() {
  const { t } = useTranslation('auth')
  const { t: tErrors } = useTranslation('errors')
  const [params] = useSearchParams()
  const session = useSession()
  const entraReady = useEntraReady()
  const config = useClientConfig()
  // fetchClient (not $api.useMutation) because the response's Date header is needed to correct the expiry for client
  // clock skew. Errors are thrown in the normalised problem shape, as $api does.
  const createGuest = useMutation({
    mutationFn: async () => {
      const { data, error, response } = await fetchClient.POST('/api/guest-sessions', {})
      if (error !== undefined || !data) throw error ?? new Error('Empty guest session response')
      return { ...data, serverDate: response.headers.get('Date') }
    },
  })
  const [entraFailed, setEntraFailed] = useState(false)
  const [entraStarting, setEntraStarting] = useState(false)

  const returnTo = sanitizeReturnTo(params.get('returnTo'))
  const reasonParam = params.get('reason')
  const urlReason = isSignOutReason(reasonParam) ? reasonParam : null
  // A sign-out that happened while this page was already showing (e.g. a failed redirect) has no URL reason.
  const reason = urlReason ?? (session.kind === 'anonymous' ? session.reason : null)

  if (session.kind === 'guest' || session.kind === 'entra') return <Navigate to={returnTo} replace />

  const restoring = session.kind === 'restoring'
  const entraConfigured = config.data?.entra != null
  const configPending = config.isPending
  const busy = createGuest.isPending || entraStarting

  const guestError = createGuest.isError ? guestCreationMessage(parseProblem(createGuest.error), tErrors) : null

  function tryAsGuest() {
    createGuest.mutate(undefined, {
      onSuccess: (data) =>
        startGuestSession({
          accessToken: data.accessToken,
          expiresAt: data.expiresAt,
          userId: data.userId,
          serverDate: data.serverDate,
        }),
    })
  }

  async function signIn() {
    setEntraFailed(false)
    setEntraStarting(true)
    try {
      await signInWithEntra()
    } catch {
      setEntraFailed(true)
      setEntraStarting(false)
    }
    // On success the browser navigates to the IdP, so there is nothing more to do here.
  }

  return (
    <main id="main" className="mx-auto flex min-h-dvh max-w-md flex-col justify-center gap-6 px-4 py-10">
      <header className="flex flex-col gap-2">
        <h1 className="text-2xl font-semibold">{t('signIn.title')}</h1>
        <p className="text-muted-foreground">{t('signIn.intro')}</p>
      </header>

      {reason && (
        <output className="flex items-start gap-2 rounded-md border bg-muted p-3 text-sm">
          <Info className="mt-0.5 size-4 shrink-0" aria-hidden="true" />
          <span>{t(`reason.${reason}`)}</span>
        </output>
      )}

      {restoring ? (
        <output className="block text-muted-foreground">{t('signIn.restoring')}</output>
      ) : (
        <div className="flex flex-col gap-4">
          {entraConfigured && (
            <Button size="lg" className="h-12 text-base" onClick={() => void signIn()} disabled={!entraReady || busy}>
              {t('signIn.signIn')}
            </Button>
          )}
          {configPending && <p className="text-sm text-muted-foreground">{t('signIn.loadingOptions')}</p>}

          <div className="flex flex-col gap-2">
            <Button
              size="lg"
              variant={entraConfigured ? 'outline' : 'default'}
              className="h-12 text-base"
              onClick={tryAsGuest}
              disabled={busy}
              aria-busy={createGuest.isPending}
            >
              {createGuest.isPending ? t('signIn.creatingGuest') : t('signIn.tryAsGuest')}
            </Button>
            <p className="text-sm text-muted-foreground">{t('signIn.guestHelp')}</p>
          </div>

          <SlowStartNotice pending={createGuest.isPending} />

          {(guestError ?? (entraFailed ? tErrors('problem.unknown') : null)) && (
            <p role="alert" className="flex items-start gap-2 rounded-md border border-destructive p-3 text-sm">
              <CircleAlert className="mt-0.5 size-4 shrink-0 text-destructive" aria-hidden="true" />
              <span>{guestError ?? tErrors('problem.unknown')}</span>
            </p>
          )}
        </div>
      )}
    </main>
  )
}
