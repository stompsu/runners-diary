import i18n from 'i18next'
import maps from './en/maps.json'
import tracks from './en/tracks.json'

/**
 * The `maps` and `tracks` namespaces are only needed by the map, track and location UI, which are lazy chunks. Their
 * strings therefore ship with those chunks instead of the initial load (bundle budget, plan AC14): every module that
 * uses one of the two namespaces imports this file, which registers them once (idempotent). The keys are still
 * type-checked through `AllResources` in ./index.ts.
 */
i18n.addResourceBundle('en', 'maps', maps, true, true)
i18n.addResourceBundle('en', 'tracks', tracks, true, true)
