import {
  LngLatBounds,
  Map as MapLibreGlMap,
  Marker,
  NavigationControl,
  setWorkerUrl,
  type GeoJSONSource,
  type LngLatLike,
  type MapOptions,
} from 'maplibre-gl'
import workerUrl from 'maplibre-gl/dist/maplibre-gl-worker.mjs?url'
import 'maplibre-gl/dist/maplibre-gl.css'
import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { TrackGeoJson } from '@/features/tracks/trackGeoJson'
import { DEFAULT_MAPLIBRE_STYLE_URL, DEFAULT_MAP_CENTER } from '../mapConfig'
import type { MapProviderProps } from '../types'
import { createEndpointElement, createHighlightElement, createRaceElement } from '../markers'
import { circlePolygon, ringBounds } from '../geo/circlePolygon'
import '@/i18n/lazyBundles'

// MapLibre 6 resolves its worker at runtime (`new URL('./maplibre-gl-worker.mjs', import.meta.url)`), which a bundler or
// the dev server's dependency pre-bundling cannot see. Hand it the asset URL Vite emits (a lazy asset, fetched with the map).
setWorkerUrl(workerUrl)

const TRACK_SOURCE = 'rd-track'
const CASING_LAYER = 'rd-track-casing'
const LINE_LAYER = 'rd-track-line'
const LINE_COLOR = '#1d4ed8'
const AREA_SOURCE = 'rd-area'
const AREA_FILL_LAYER = 'rd-area-fill'
const AREA_LINE_LAYER = 'rd-area-line'
/** The app's primary ink (`--primary`, oklch 0.205 0 0); paint properties cannot read CSS variables. */
const AREA_COLOR = '#171717'
const FIT_PADDING = 36
const PLACE_ZOOM = 14

const areaData = (center: { latitude: number; longitude: number } | undefined, radiusMeters: number) => ({
  type: 'FeatureCollection' as const,
  features: center
    ? [
        {
          type: 'Feature' as const,
          properties: {},
          geometry: { type: 'Polygon' as const, coordinates: [circlePolygon(center, radiusMeters)] },
        },
      ]
    : [],
})

const lineData = (track: TrackGeoJson | null | undefined) => ({
  type: 'FeatureCollection' as const,
  features: track ? [track.features[0]] : [],
})

/** The MapLibre GL provider (ADR 0022): one imperative map, driven by props through effects. Lazily loaded. */
export default function MapLibreMap({
  track,
  marker,
  markerKind,
  markerLabel,
  area,
  highlight,
  onPick,
  fitTo,
  center,
  dashed,
  styleUrl,
  onStateChange,
}: MapProviderProps) {
  const { t } = useTranslation('maps')
  const containerRef = useRef<HTMLDivElement>(null)
  const [map, setMap] = useState<MapLibreGlMap | null>(null)
  // Latest values for handlers and one-off effects, so the map is not rebuilt when they change.
  const onPickRef = useRef(onPick)
  const onStateChangeRef = useRef(onStateChange)
  const markerRef = useRef(marker)
  const centerRef = useRef(center)
  const labels = useRef({
    start: t('markers.start'),
    finish: t('markers.finish'),
    position: t('markers.position'),
    highlight: t('markers.highlight'),
  })
  useEffect(() => {
    onPickRef.current = onPick
    onStateChangeRef.current = onStateChange
    markerRef.current = marker
  })

  // Create the map once per style.
  useEffect(() => {
    const container = containerRef.current
    if (!container) return
    let instance: MapLibreGlMap
    try {
      const options: MapOptions = {
        container,
        style: styleUrl || DEFAULT_MAPLIBRE_STYLE_URL,
        center: centerRef.current ? [centerRef.current.longitude, centerRef.current.latitude] : [...DEFAULT_MAP_CENTER],
        zoom: centerRef.current ? PLACE_ZOOM : 11,
        // Page scroll must keep working on a phone when the map is part of a scrolling sheet; the picker needs
        // one-finger interaction.
        cooperativeGestures: !onPickRef.current,
        locale: {
          'Map.Title': t('controls.map'),
          'NavigationControl.ZoomIn': t('controls.zoomIn'),
          'NavigationControl.ZoomOut': t('controls.zoomOut'),
          'NavigationControl.ResetBearing': t('controls.resetBearing'),
          'AttributionControl.ToggleAttribution': t('controls.toggleAttribution'),
          'AttributionControl.MapFeedback': t('controls.mapFeedback'),
          'Marker.Title': t('controls.markerTitle'),
          'CooperativeGesturesHandler.WindowsHelpText': t('controls.gesturesWindows'),
          'CooperativeGesturesHandler.MacHelpText': t('controls.gesturesMac'),
          'CooperativeGesturesHandler.MobileHelpText': t('controls.gesturesMobile'),
        },
      }
      instance = new MapLibreGlMap(options)
    } catch {
      onStateChangeRef.current('error')
      return
    }
    instance.addControl(new NavigationControl({ showCompass: false }), 'top-right')
    let loaded = false
    instance.on('load', () => {
      loaded = true
      setMap(instance)
      onStateChangeRef.current('ready')
    })
    // An error before `load` is a style that could not be fetched or parsed; later errors are single tiles.
    instance.on('error', () => {
      if (!loaded) onStateChangeRef.current('error')
    })
    instance.on('click', (event) => {
      // A click on a repeated world copy reports longitudes beyond +-180: wrap them back.
      const { lat, lng } = event.lngLat.wrap()
      onPickRef.current?.({ latitude: lat, longitude: lng })
    })
    return () => {
      setMap(null)
      instance.remove()
    }
  }, [styleUrl, t])

  // The line: source + casing + line layer, updated in place.
  useEffect(() => {
    if (!map) return
    const source = map.getSource<GeoJSONSource>(TRACK_SOURCE)
    if (source) {
      source.setData(lineData(track))
    } else {
      map.addSource(TRACK_SOURCE, { type: 'geojson', data: lineData(track) })
      const layout = { 'line-cap': 'round', 'line-join': 'round' } as const
      map.addLayer({
        id: CASING_LAYER,
        type: 'line',
        source: TRACK_SOURCE,
        layout,
        paint: { 'line-color': '#ffffff', 'line-width': 8, 'line-opacity': 0.9 },
      })
      map.addLayer({
        id: LINE_LAYER,
        type: 'line',
        source: TRACK_SOURCE,
        layout,
        paint: { 'line-color': LINE_COLOR, 'line-width': 4 },
      })
    }
    // Planned courses are dashed (a solid line is a one-dash pattern of full length).
    map.setPaintProperty(LINE_LAYER, 'line-dasharray', dashed ? [2, 1.5] : [1, 0])
  }, [map, track, dashed])

  // Start and finish markers.
  useEffect(() => {
    if (!map || !track) return
    const [, start, finish] = track.features
    const markers = [
      new Marker({ element: createEndpointElement('start', labels.current.start) })
        .setLngLat(start.geometry.coordinates as LngLatLike)
        .addTo(map),
      new Marker({ element: createEndpointElement('finish', labels.current.finish) })
        .setLngLat(finish.geometry.coordinates as LngLatLike)
        .addTo(map),
    ]
    return () => markers.forEach((m) => m.remove())
  }, [map, track])

  // A single place (picker / run details).
  useEffect(() => {
    if (!map || !marker) return
    const pin = new Marker(
      markerKind === 'race' ? { element: createRaceElement(markerLabel ?? '') } : { color: LINE_COLOR },
    )
      .setLngLat([marker.longitude, marker.latitude])
      .addTo(map)
    pin.getElement().dataset.testid = 'map-marker'
    // A place typed in as coordinates may be off screen; bring it into view (a click on the map never is).
    if (!map.getBounds().contains([marker.longitude, marker.latitude])) {
      map.jumpTo({ center: [marker.longitude, marker.latitude] })
    }
    return () => {
      pin.remove()
    }
  }, [map, marker, markerKind, markerLabel])

  // The radius circle: one polygon source, updated in place; layers are removed with the map on unmount.
  const areaLat = area?.center.latitude
  const areaLon = area?.center.longitude
  const areaRadius = area?.radiusMeters ?? 0
  useEffect(() => {
    if (!map) return
    const areaCenter =
      areaLat === undefined || areaLon === undefined ? undefined : { latitude: areaLat, longitude: areaLon }
    const source = map.getSource<GeoJSONSource>(AREA_SOURCE)
    if (source) {
      source.setData(areaData(areaCenter, areaRadius))
      return
    }
    map.addSource(AREA_SOURCE, { type: 'geojson', data: areaData(areaCenter, areaRadius) })
    map.addLayer({
      id: AREA_FILL_LAYER,
      type: 'fill',
      source: AREA_SOURCE,
      paint: { 'fill-color': AREA_COLOR, 'fill-opacity': 0.1 },
    })
    map.addLayer({
      id: AREA_LINE_LAYER,
      type: 'line',
      source: AREA_SOURCE,
      paint: { 'line-color': AREA_COLOR, 'line-width': 2 },
    })
  }, [map, areaLat, areaLon, areaRadius])

  // The hover/keyboard marker from the elevation profile.
  const highlightLon = highlight?.[0]
  const highlightLat = highlight?.[1]
  useEffect(() => {
    if (!map || highlightLon === undefined || highlightLat === undefined) return
    const dot = new Marker({ element: createHighlightElement(labels.current.highlight) })
      .setLngLat([highlightLon, highlightLat])
      .addTo(map)
    return () => {
      dot.remove()
    }
  }, [map, highlightLon, highlightLat])

  // Fit the view: to the track whenever it changes; to a single place only once (picking must not jump the map).
  const wantsTrack = (fitTo ?? (track ? 'track' : 'marker')) === 'track'
  useEffect(() => {
    if (!map) return
    if (wantsTrack && track) {
      map.fitBounds(new LngLatBounds([track.bbox[0], track.bbox[1]], [track.bbox[2], track.bbox[3]]), {
        padding: FIT_PADDING,
        duration: 0,
        maxZoom: 17,
      })
    } else if (areaLat !== undefined && areaLon !== undefined && !track && fitTo !== 'marker') {
      const [w, s, e, n] = ringBounds(circlePolygon({ latitude: areaLat, longitude: areaLon }, areaRadius))
      map.fitBounds(new LngLatBounds([w, s], [e, n]), { padding: FIT_PADDING, duration: 0 })
    } else if (!wantsTrack && markerRef.current) {
      map.jumpTo({ center: [markerRef.current.longitude, markerRef.current.latitude], zoom: PLACE_ZOOM })
    }
  }, [map, track, wantsTrack, fitTo, areaLat, areaLon, areaRadius])

  // Inline, not a utility class: maplibre-gl.css (a lazy stylesheet that loads after ours) sets `.maplibregl-map
  // { position: relative }`, which would beat Tailwind's `absolute`, collapse the container to zero height and clip
  // the whole map. Found by the picker's click test in the production build.
  return <div ref={containerRef} style={{ position: 'absolute', inset: 0 }} />
}
