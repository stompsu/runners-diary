import { type KeyboardEvent, type PointerEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { Area, AreaChart, ReferenceDot, ReferenceLine, ResponsiveContainer, XAxis, YAxis } from 'recharts'
import type { ProfileSample } from '@/features/tracks/trackGeoJson'
import { formatDistance, formatElevation } from '@/lib/format/units'
import { cn } from '@/lib/utils'
import { indexAtDistance, nextProfileIndex } from './profileNavigation'
import '@/i18n/lazyBundles'

/** Layout constants shared by the chart and the pointer mapping (the plot area is the chart minus these). */
const Y_AXIS_WIDTH = 44
const MARGIN = { top: 8, right: 8, bottom: 0, left: 0 } as const
const STROKE = '#1d4ed8'

export interface ElevationProfileProps {
  profile: readonly ProfileSample[]
  /** The sample under the pointer / keyboard focus, or null. Controlled by the parent (`TrackViewer`). */
  activeIndex: number | null
  /** Reports pointer moves and keyboard steps; null = left the chart. */
  onHover: (index: number | null) => void
  className?: string
}

/**
 * Elevation along the route (Recharts, lazy chunk). Recharts only draws; the interaction is a native-feeling slider on
 * the wrapper (role="slider": arrows, Page keys, Home/End) plus pointer tracking, so keyboard and pointer both drive
 * `onHover` without depending on Recharts' tooltip internals. A text alternative sits next to it (`TrackDataTable`).
 */
export default function ElevationProfile({ profile, activeIndex, onHover, className }: ElevationProfileProps) {
  const { t, i18n } = useTranslation('tracks')
  const data = profile.map(([distance, elevation]) => ({ distance, elevation }))
  const last = profile[profile.length - 1]
  const active = activeIndex === null ? undefined : data[activeIndex]
  const readout = active
    ? t('profile.readout', {
        distance: formatDistance(active.distance, i18n.language),
        elevation: formatElevation(active.elevation, i18n.language),
      })
    : ''

  const elevations = data.map((d) => d.elevation)
  const yDomain: [number, number] = [Math.floor(Math.min(...elevations) - 2), Math.ceil(Math.max(...elevations) + 2)]

  function onKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    if (event.key === 'Escape') {
      onHover(null)
      return
    }
    const next = nextProfileIndex(event.key, activeIndex, profile.length)
    if (next === null) return
    event.preventDefault()
    onHover(next)
  }

  function onPointer(event: PointerEvent<HTMLDivElement>) {
    if (!last) return
    const rect = event.currentTarget.getBoundingClientRect()
    const plotWidth = rect.width - Y_AXIS_WIDTH - MARGIN.left - MARGIN.right
    if (plotWidth <= 0) return
    const fraction = Math.min(1, Math.max(0, (event.clientX - rect.left - Y_AXIS_WIDTH - MARGIN.left) / plotWidth))
    onHover(indexAtDistance(profile, fraction * last[0]))
  }

  return (
    <div className={cn('space-y-1', className)}>
      <p className="min-h-5 text-sm text-muted-foreground tabular-nums" aria-hidden="true">
        {readout}
      </p>
      <div
        onPointerDown={onPointer}
        onPointerMove={onPointer}
        onPointerLeave={(event) => {
          // A finger lifted after a tap keeps the marker; a mouse leaving the chart clears it.
          if (event.pointerType === 'mouse') onHover(null)
        }}
        className="relative h-[120px] touch-pan-y rounded-md focus-within:ring-[3px] focus-within:ring-ring/50 sm:h-40"
      >
        {/* The keyboard/screen-reader control is a native range input laid over the chart: one tab stop, a real
            slider role and value text. It ignores the pointer (the wrapper maps pointer x to the plot area). */}
        <input
          type="range"
          min={0}
          max={Math.max(0, profile.length - 1)}
          step={1}
          value={activeIndex ?? 0}
          aria-label={t('profile.label')}
          aria-valuetext={readout || t('profile.title')}
          onChange={(event) => onHover(Number(event.target.value))}
          onFocus={() => {
            if (activeIndex === null) onHover(0)
          }}
          onBlur={() => onHover(null)}
          onKeyDown={onKeyDown}
          className="pointer-events-none absolute inset-0 z-10 size-full cursor-default opacity-0"
        />
        <ResponsiveContainer width="100%" height="100%" initialDimension={{ width: 600, height: 160 }}>
          <AreaChart data={data} margin={MARGIN} accessibilityLayer={false} role="presentation">
            <XAxis
              dataKey="distance"
              type="number"
              domain={[0, 'dataMax']}
              tickFormatter={(meters: number) => String(Math.round((meters / 1000) * 10) / 10)}
              tick={{ fontSize: 11 }}
              height={20}
              tickMargin={2}
            />
            <YAxis
              dataKey="elevation"
              domain={yDomain}
              width={Y_AXIS_WIDTH}
              tick={{ fontSize: 11 }}
              tickFormatter={(meters: number) => String(Math.round(meters))}
            />
            <Area
              dataKey="elevation"
              type="monotone"
              stroke={STROKE}
              strokeWidth={2}
              fill={STROKE}
              fillOpacity={0.18}
              dot={false}
              isAnimationActive={false}
            />
            {active && <ReferenceLine x={active.distance} stroke={STROKE} strokeDasharray="4 3" />}
            {active && (
              <ReferenceDot
                x={active.distance}
                y={active.elevation}
                r={5}
                fill={STROKE}
                stroke="#fff"
                strokeWidth={2}
              />
            )}
          </AreaChart>
        </ResponsiveContainer>
      </div>
    </div>
  )
}
