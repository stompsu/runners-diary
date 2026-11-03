import { useQueryClient } from '@tanstack/react-query'
import { lazy, Suspense, useEffect } from 'react'
import { finishRestoring, onSignOut, useSession } from './session'
import { useClientConfig } from './useClientConfig'

// The MSAL chunk is only fetched when the API reports Entra settings (ADR 0014 §1, ADR 0015).
const CLIENT_CONFIG_PATH = '/api/client-config'
const EntraAuthBoundary = lazy(() => import('./entra/EntraAuthBoundary'))

/**
 * Mounted once inside the providers: clears the query cache whenever a session ends (sign-out, 401, expiry) and
 * loads the Entra boundary when configured. Without Entra config a pending "restoring" state resolves to anonymous.
 */
export function AuthBootstrap() {
  const queryClient = useQueryClient()
  const session = useSession()
  const { data, isError, isSuccess } = useClientConfig()

  useEffect(
    () =>
      onSignOut(() => {
        // Everything user-specific goes; the anonymous, session-independent client config may stay.
        void queryClient.cancelQueries()
        // Mutations of the ended session must not run their cache updates against the next one.
        queryClient.getMutationCache().clear()
        queryClient.removeQueries({ predicate: (query) => query.queryKey[1] !== CLIENT_CONFIG_PATH })
      }),
    [queryClient],
  )

  const entra = data?.entra ?? null
  const noEntra = isError || (isSuccess && entra === null)
  useEffect(() => {
    if (noEntra && session.kind === 'restoring') finishRestoring()
  }, [noEntra, session.kind])

  if (!entra) return null
  return (
    <Suspense fallback={null}>
      <EntraAuthBoundary config={entra} />
    </Suspense>
  )
}
