import { useTranslation } from 'react-i18next'
import { isRouteErrorResponse, useRouteError } from 'react-router'
import { NotFoundPage } from './NotFoundPage'

/** Router errorElement: 404 responses render NotFound; anything else a generic message (no error details). */
export function RouteErrorPage() {
  const { t } = useTranslation()
  const error = useRouteError()

  if (isRouteErrorResponse(error) && error.status === 404) return <NotFoundPage />

  return (
    <section role="alert" className="p-6">
      <h1 className="text-2xl font-semibold">{t('errors.unexpectedTitle')}</h1>
      <p className="mt-2 text-muted-foreground">{t('errors.unexpectedBody')}</p>
    </section>
  )
}
