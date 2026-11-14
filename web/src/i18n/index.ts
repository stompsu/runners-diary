import i18n from 'i18next'
import { initReactI18next } from 'react-i18next'
import auth from './en/auth.json'
import calendar from './en/calendar.json'
import common from './en/common.json'
import errors from './en/errors.json'
import runs from './en/runs.json'
import runTypes from './en/runTypes.json'

export const defaultNS = 'common'

// Eager namespaces. `maps`, `tracks`, `insights` and `races` load with the lazy map/track/insights UI (./lazyBundles.ts).
export const resources = {
  en: { common, auth, calendar, runs, runTypes, errors },
} as const

/** Every namespace, for typed keys (the lazy ones are type-only imports here). */
export type AllResources = {
  en: (typeof resources)['en'] & {
    maps: typeof import('./en/maps.json')
    tracks: typeof import('./en/tracks.json')
    insights: typeof import('./en/insights.json')
    races: typeof import('./en/races.json')
  }
}

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
