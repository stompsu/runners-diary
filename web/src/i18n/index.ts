import i18n from 'i18next'
import { initReactI18next } from 'react-i18next'
import enCommon from './en/common.json'

export const defaultNS = 'common'

export const resources = {
  en: { common: enCommon },
} as const

// English first; Polish ('pl') is added next by adding a resource bundle (brief §1.2).
void i18n.use(initReactI18next).init({
  resources,
  lng: 'en',
  fallbackLng: 'en',
  defaultNS,
  interpolation: { escapeValue: false },
})

export default i18n
