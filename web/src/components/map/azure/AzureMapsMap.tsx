import * as atlas from 'azure-maps-control'
import 'azure-maps-control/dist/atlas.min.css'
import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { LonLat, TrackGeoJson } from '@/features/tracks/trackGeoJson'
import { DEFAULT_MAP_CENTER } from '../mapConfig'
import { createEndpointElement, createHighlightElement } from '../markers'
import type { MapProviderProps } from '../types'
import { markAzureMapsUnavailable } from './azureSession'
import { getMapsToken } from './mapsToken'
import '@/i18n/lazyBundles'

const LINE_COLOR = '#1d4ed8'
const FIT_PADDING = 36
const PLACE_ZOOM = 14

/** Longitudes beyond +-180 (a click on a repeated world copy) wrapped back into range. */
const wrapLongitude = (longitude: number) => ((((longitude + 180) % 360) + 360) % 360) - 180

/**
 * The Azure Maps provider (ADR 0022, 0033): the same props as the MapLibre one, authenticated with a short-lived SAS token from
 * `GET /api/maps/token` (no key ever reaches the browser). Any failure before the map is ready (token, SDK, style) marks Azure Maps
 * unavailable for the session, and `MapView` switches to MapLibre. Lazily loaded; only used when client-config selects it.
 */
export default function AzureMapsMap({
  track,
  marker,
  highlight,
  onPick,
  fitTo,
  center,
  dashed,
  azureMapsDomain,
  onStateChange,
}: MapProviderProps) {
  const { t } = useTranslation('maps')
  const containerRef = useRef<HTMLDivElement>(null)
  const [map, setMap] = useState<atlas.Map | null>(null)
  const onPickRef = useRef(onPick)
  const onStateChangeRef = useRef(onStateChange)
  const markerRef = useRef(marker)
  const centerRef = useRef(center)
  const labels = useRef({
    start: t('markers.start'),
    finish: t('markers.finish'),
    highlight: t('markers.highlight'),
  })
  useEffect(() => {
    onPickRef.current = onPick
    onStateChangeRef.current = onStateChange
    markerRef.current = marker
  })

  // Create the map once per domain.
  useEffect(() => {
    const container = containerRef.current
    if (!container || !azureMapsDomain) return
    let instance: atlas.Map
    let ready = false
    const fail = () => {
      if (ready) return
      onStateChangeRef.current('error')
      markAzureMapsUnavailable()
    }
    try {
      const start = centerRef.current
      instance = new atlas.Map(container, {
        domain: azureMapsDomain,
        authOptions: {
          authType: atlas.AuthenticationType.sas,
          getToken: (resolve, reject) => {
            getMapsToken().then(resolve, (reason: unknown) => {
              reject(reason)
              fail()
            })
          },
        },
        center: start ? [start.longitude, start.latitude] : [...DEFAULT_MAP_CENTER],
        zoom: start ? PLACE_ZOOM : 11,
        style: 'road',
        // No usage telemetry from the SDK, and no logo or feedback link slots that would eat the small map.
        disableTelemetry: true,
        showFeedbackLink: false,
        // A view-only map sits inside a scrolling page: the wheel must keep scrolling it (zoom with the buttons).
        scrollZoomInteraction: !!onPickRef.current,
      })
    } catch {
      fail()
      return
    }
    instance.controls.add(new atlas.control.ZoomControl(), { position: atlas.ControlPosition.TopRight })
    instance.events.add('ready', () => {
      ready = true
      setMap(instance)
      onStateChangeRef.current('ready')
    })
    // An error before `ready` is a token, style or SDK failure; later errors are single tiles.
    instance.events.add('error', fail)
    instance.events.add('click', (event) => {
      if (!event.position) return
      const [longitude = 0, latitude = 0] = event.position
      onPickRef.current?.({ latitude, longitude: wrapLongitude(longitude) })
    })
    return () => {
      setMap(null)
      instance.dispose()
    }
  }, [azureMapsDomain])

  // The line: a source with the line and a casing + line layer.
  useEffect(() => {
    if (!map || !track) return
    const source = new atlas.source.DataSource()
    map.sources.add(source)
    source.add(new atlas.data.MultiLineString(lineCoordinates(track)))
    const layout = { lineCap: 'round', lineJoin: 'round' } as const
    const casing = new atlas.layer.LineLayer(source, undefined, { strokeColor: '#ffffff', strokeWidth: 8, ...layout })
    const line = new atlas.layer.LineLayer(source, undefined, {
      strokeColor: LINE_COLOR,
      strokeWidth: 4,
      strokeDashArray: dashed ? [2, 2] : undefined,
      ...layout,
    })
    map.layers.add([casing, line])
    return () => {
      map.layers.remove([casing, line])
      map.sources.remove(source)
    }
  }, [map, track, dashed])

  // Start and finish markers.
  useEffect(() => {
    if (!map || !track) return
    const [, start, finish] = track.features
    const markers = [
      new atlas.HtmlMarker({
        position: toPosition(start.geometry.coordinates),
        htmlContent: createEndpointElement('start', labels.current.start),
      }),
      new atlas.HtmlMarker({
        position: toPosition(finish.geometry.coordinates),
        htmlContent: createEndpointElement('finish', labels.current.finish),
      }),
    ]
    map.markers.add(markers)
    return () => {
      map.markers.remove(markers)
    }
  }, [map, track])

  // A single place (picker / run details).
  useEffect(() => {
    if (!map || !marker) return
    const pin = new atlas.HtmlMarker({ position: [marker.longitude, marker.latitude], color: LINE_COLOR })
    map.markers.add(pin)
    pin.getElement().dataset.testid = 'map-marker'
    // A place typed in as coordinates may be off screen; bring it into view (a click on the map never is).
    const bounds = map.getCamera().bounds
    if (!bounds || !contains(bounds, marker.longitude, marker.latitude)) {
      map.setCamera({ center: [marker.longitude, marker.latitude], type: 'jump' })
    }
    return () => {
      map.markers.remove(pin)
    }
  }, [map, marker])

  // The hover/keyboard marker from the elevation profile.
  const highlightLon = highlight?.[0]
  const highlightLat = highlight?.[1]
  useEffect(() => {
    if (!map || highlightLon === undefined || highlightLat === undefined) return
    const dot = new atlas.HtmlMarker({
      position: [highlightLon, highlightLat],
      htmlContent: createHighlightElement(labels.current.highlight),
    })
    map.markers.add(dot)
    return () => {
      map.markers.remove(dot)
    }
  }, [map, highlightLon, highlightLat])

  // Fit the view: to the track whenever it changes; to a single place only once (picking must not jump the map).
  const wantsTrack = (fitTo ?? (track ? 'track' : 'marker')) === 'track'
  useEffect(() => {
    if (!map) return
    if (wantsTrack && track) {
      map.setCamera({ bounds: [...track.bbox], padding: FIT_PADDING, maxZoom: 17, type: 'jump' })
    } else if (!wantsTrack && markerRef.current) {
      map.setCamera({
        center: [markerRef.current.longitude, markerRef.current.latitude],
        zoom: PLACE_ZOOM,
        type: 'jump',
      })
    }
  }, [map, track, wantsTrack])

  // Inline, as for MapLibre: the SDK's own stylesheet loads lazily and must not collapse the container.
  return <div ref={containerRef} style={{ position: 'absolute', inset: 0 }} />
}

const toPosition = (point: LonLat): atlas.data.Position => [point[0], point[1]]

/** The track line is a MultiLineString: one coordinate list per segment. */
function lineCoordinates(track: TrackGeoJson): atlas.data.Position[][] {
  return track.features[0].geometry.coordinates.map((segment) => segment.map(toPosition))
}

function contains(bounds: atlas.data.BoundingBox, longitude: number, latitude: number): boolean {
  const [west = 0, south = 0, east = 0, north = 0] = bounds
  const inLongitude = west <= east ? longitude >= west && longitude <= east : longitude >= west || longitude <= east
  return inLongitude && latitude >= south && latitude <= north
}
