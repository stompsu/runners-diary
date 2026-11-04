/**
 * Default OpenFreeMap style (verified 2026-11-03: GET returns a MapLibre style v8 JSON). The environment overrides it
 * with `maps.mapLibreStyleUrl` from client-config (`Maps:MapLibre:StyleUrl`); the style host is never called by tests.
 */
export const DEFAULT_MAPLIBRE_STYLE_URL = 'https://tiles.openfreemap.org/styles/liberty'

/** Application default when nothing else is known (plan §3.5): Gliwice. */
export const DEFAULT_MAP_CENTER: readonly [longitude: number, latitude: number] = [18.6714, 50.2945]
