import i18n from 'i18next'
import goals from './en/goals.json'
import insights from './en/insights.json'
import maps from './en/maps.json'
import races from './en/races.json'
import tracks from './en/tracks.json'

/**
 * The `maps`, `tracks`, `insights`, `races` and `goals` namespaces are only needed by the map, track, location, insights, race and goals UI, which are lazy chunks. Their
 * strings therefore ship with those chunks instead of the initial load (bundle budget, plan AC14): every module that
 * uses one of these namespaces imports this file, which registers them once (idempotent). The keys are still
 * type-checked through `AllResources` in ./index.ts.
 */
i18n.addResourceBundle('en', 'maps', maps, true, true)
i18n.addResourceBundle('en', 'tracks', tracks, true, true)
i18n.addResourceBundle('en', 'insights', insights, true, true)
i18n.addResourceBundle('en', 'races', races, true, true)
i18n.addResourceBundle('en', 'goals', goals, true, true)
