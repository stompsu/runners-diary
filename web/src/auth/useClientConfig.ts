import { $api } from '@/api/client'

/** `GET /api/client-config` (ADR 0015): loaded once; a failure just means "no Entra". */
export function useClientConfig() {
  return $api.useQuery('get', '/api/client-config', {}, { staleTime: Infinity })
}
