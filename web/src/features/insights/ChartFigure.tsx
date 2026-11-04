import { useId, type ReactNode } from 'react'
import { safeSvgId } from './insightsFormat'
import { ScrollRegion } from './ScrollRegion'
import { useInsightsT } from './useInsightsT'

export interface DataTable {
  /** Column headers; the first column is the row header. */
  headers: readonly string[]
  rows: readonly (readonly string[])[]
}

export interface LegendItem {
  label: string
  /** An SVG swatch (a fill, a pattern or a dash style) so the legend never relies on colour alone. */
  swatch: ReactNode
}

/**
 * Every chart is a `<figure>` (M5 plan §3.5): caption, a one-sentence summary generated from the data, the chart itself
 * (decorative: `aria-hidden`, nothing focusable inside) and the same numbers in a "Show data" table. The table and the
 * summary are the accessible path to the chart.
 */
export function ChartFigure({
  title,
  summary,
  table,
  legend,
  controls,
  note,
  children,
}: {
  title: string
  summary: string
  table: DataTable
  legend?: readonly LegendItem[]
  controls?: ReactNode
  note?: string
  children: ReactNode
}) {
  const { t } = useInsightsT()
  const id = useId()
  const captionId = `${id}-caption`
  const summaryId = `${id}-summary`

  return (
    <figure
      aria-labelledby={captionId}
      aria-describedby={summaryId}
      className="flex min-w-0 flex-col gap-2 rounded-lg border p-3 sm:p-4"
    >
      <figcaption id={captionId} className="font-medium">
        {title}
      </figcaption>
      <p id={summaryId} className="text-sm text-muted-foreground">
        {summary}
      </p>
      {controls}
      <div aria-hidden="true" className="min-w-0">
        {children}
      </div>
      {legend && legend.length > 0 ? (
        <ul aria-hidden="true" className="flex flex-wrap gap-x-4 gap-y-1 text-xs text-muted-foreground">
          {legend.map((item) => (
            <li key={item.label} className="flex items-center gap-1.5">
              {item.swatch}
              {item.label}
            </li>
          ))}
        </ul>
      ) : null}
      {note ? <p className="text-xs text-muted-foreground">{note}</p> : null}
      <details className="text-sm">
        <summary className="inline-flex min-h-11 cursor-pointer sm:min-h-9 items-center rounded-md px-1 font-medium focus-visible:outline-2 focus-visible:outline-offset-2">
          {t('chart.showData')}
        </summary>
        <ScrollRegion label={t('chart.tableRegion', { title })} className="mt-2 max-h-80">
          <table className="w-full min-w-max text-left tabular-nums">
            <caption className="sr-only">{title}</caption>
            <thead className="sticky top-0 bg-muted">
              <tr>
                {table.headers.map((header) => (
                  <th key={header} scope="col" className="px-3 py-2 font-medium">
                    {header}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {table.rows.map((row, index) => (
                <tr key={index} className="border-t">
                  {row.map((cell, column) =>
                    column === 0 ? (
                      <th key={column} scope="row" className="px-3 py-1.5 font-normal">
                        {cell}
                      </th>
                    ) : (
                      <td key={column} className="px-3 py-1.5">
                        {cell}
                      </td>
                    ),
                  )}
                </tr>
              ))}
            </tbody>
          </table>
        </ScrollRegion>
      </details>
    </figure>
  )
}

/** A short line sample in a legend: solid or dashed, in the series colour. */
export function LineSwatch({ color, dash }: { color: string; dash?: string }) {
  return (
    <svg width="22" height="8" aria-hidden="true" focusable="false">
      <line x1="1" y1="4" x2="21" y2="4" stroke={color} strokeWidth="2.5" strokeDasharray={dash} />
    </svg>
  )
}

/** A filled square, optionally hatched (planned values use the hatch, not only a lighter colour). */
export function BoxSwatch({ color, hatched }: { color: string; hatched?: boolean }) {
  const id = safeSvgId(useId())
  return (
    <svg width="14" height="14" aria-hidden="true" focusable="false">
      {hatched ? <HatchPattern id={id} color={color} /> : null}
      <rect
        x="1"
        y="1"
        width="12"
        height="12"
        fill={hatched ? `url(#${id})` : color}
        stroke={color}
        strokeWidth="1.5"
        strokeDasharray={hatched ? '3 2' : undefined}
      />
    </svg>
  )
}

export function HatchPattern({ id, color }: { id: string; color: string }) {
  return (
    <defs>
      <pattern id={id} width="5" height="5" patternUnits="userSpaceOnUse" patternTransform="rotate(45)">
        <rect width="5" height="5" fill="transparent" />
        <line x1="0" y1="0" x2="0" y2="5" stroke={color} strokeWidth="2" />
      </pattern>
    </defs>
  )
}
