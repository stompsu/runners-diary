import 'i18next'
import type { defaultNS, resources } from './index'

// Type-checked translation keys: t('nav.diary') compiles, t('nav.typo') does not.
declare module 'i18next' {
  interface CustomTypeOptions {
    defaultNS: typeof defaultNS
    resources: (typeof resources)['en']
  }
}
