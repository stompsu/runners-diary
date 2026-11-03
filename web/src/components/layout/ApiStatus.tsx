import { useTranslation } from 'react-i18next'
import { $api } from '@/api/client'
import { Badge } from '@/components/ui/badge'

type StatusKey = 'checking' | 'healthy' | 'degraded' | 'unhealthy'

const variants: Record<StatusKey, 'secondary' | 'default' | 'outline' | 'destructive'> = {
  checking: 'secondary',
  healthy: 'default',
  degraded: 'outline',
  unhealthy: 'destructive',
}

/** Shows the API's aggregate health from GET /api/health (also proves the generated client works). */
export function ApiStatus() {
  const { t } = useTranslation()
  const { data, isPending, isError } = $api.useQuery(
    'get',
    '/api/health',
    {},
    { refetchInterval: 30_000, retry: false },
  )

  let status: StatusKey
  if (isPending) status = 'checking'
  else if (isError || !data) status = 'unhealthy'
  else status = data.status === 'Healthy' ? 'healthy' : data.status === 'Degraded' ? 'degraded' : 'unhealthy'

  return (
    // <output> has the implicit "status" role, so screen readers announce changes politely.
    <output aria-label={t('apiStatus.label')}>
      <Badge variant={variants[status]}>{t(`apiStatus.${status}`)}</Badge>
    </output>
  )
}
