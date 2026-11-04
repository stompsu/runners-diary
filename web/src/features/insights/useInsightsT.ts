import { useTranslation } from 'react-i18next'
import '@/i18n/lazyBundles'

/** The `insights` namespace ships with the lazy insights chunk (bundle budget, M5 AC13); importing this registers it. */
export function useInsightsT() {
  return useTranslation('insights')
}
