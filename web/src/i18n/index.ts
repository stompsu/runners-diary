import i18n from 'i18next'
import { initReactI18next } from 'react-i18next'
import auth from './en/auth.json'
import calendar from './en/calendar.json'
import common from './en/common.json'
import errors from './en/errors.json'
import runs from './en/runs.json'
import runTypes from './en/runTypes.json'

export const defaultNS = 'common'

export const resources = {
  en: { common, auth, calendar, runs, runTypes, errors },
} as const

// English first; Polish ('pl') is added next by adding a resource bundle (brief §1.2).
void i18n.use(initReactI18next).init({
  resources,
  lng: 'en',
  fallbackLng: 'en',
  defaultNS,
  ns: Object.keys(resources.en),
  interpolation: { escapeValue: false },
})

export default i18n
