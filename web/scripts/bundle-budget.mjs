// Bundle budget (M4 plan AC14): the initial load (entry + everything it imports statically + its CSS) may grow by at
// most 5 kB gzip over the M3 baseline. MapLibre and Recharts must stay in lazy chunks.
// Usage: `npm run build && npm run bundle-budget`. Update BASELINE_BYTES deliberately (and say why) when the initial
// load is meant to grow; never to make a failing run pass.
import { readFileSync } from 'node:fs'
import { dirname, join, normalize } from 'node:path'
import { gzipSync } from 'node:zlib'

/** Initial load of `main` at the end of M3 (commit ab7dd03), measured with this script on `npm run build`. */
const BASELINE_BYTES = 201_242
const ALLOWED_GROWTH_BYTES = 5 * 1024
/** Chunk groups from vite.config.ts that must only ever be reached through dynamic import(). */
// M5: the insights route chunk (dashboard, cards, charts, `insights` strings) is reached through the lazy /insights route.
// M7: the Azure Maps Web SDK (azure-maps-vendor, AzureMapsMap) loads only when client-config selects it.
// M8: the Settings -> Race interests page (form, pickers, `races` strings) loads only through its lazy route.
const LAZY_ONLY = [
  'map-vendor',
  'azure-maps-vendor',
  'AzureMapsMap',
  'chart-vendor',
  'InsightsPage',
  'RaceInterestsPage',
]

const dist = join(import.meta.dirname, '..', 'dist')
const gzipSize = (file) => gzipSync(readFileSync(join(dist, file)), { level: 9 }).length

const html = readFileSync(join(dist, 'index.html'), 'utf8')
const initial = new Set()

/** Static imports only: `import"./x.js"` / `import{a}from"./x.js"`; a dynamic `import("./x.js")` has a parenthesis. */
const STATIC_IMPORT = /\bimport\s*(?:[\w${}*,\s]+?\s*from\s*)?["'](\.{1,2}\/[^"']+)["']/g

function visit(file) {
  if (initial.has(file)) return
  initial.add(file)
  if (!file.endsWith('.js')) return
  const source = readFileSync(join(dist, file), 'utf8')
  for (const match of source.matchAll(STATIC_IMPORT)) {
    visit(normalize(join(dirname(file), match[1])))
  }
}

for (const match of html.matchAll(/<(?:script|link)\b[^>]*?\b(?:src|href)="\/([^"]+\.(?:js|css))"/g)) {
  visit(match[1])
}

const rows = [...initial].sort().map((file) => ({ file, gzip: gzipSize(file) }))
const total = rows.reduce((sum, row) => sum + row.gzip, 0)
const limit = BASELINE_BYTES + ALLOWED_GROWTH_BYTES
const kb = (bytes) => `${(bytes / 1024).toFixed(1)} kB`

for (const row of rows) console.log(`  ${kb(row.gzip).padStart(9)}  ${row.file}`)
console.log(`Initial load: ${kb(total)} gzip = ${total} B (baseline ${kb(BASELINE_BYTES)}, limit ${kb(limit)})`)

const failures = []
if (total > limit) failures.push(`initial load ${kb(total)} exceeds the budget of ${kb(limit)}`)
for (const file of initial) {
  const group = LAZY_ONLY.find((name) => file.includes(name))
  if (group) failures.push(`${file} (${group}) is part of the initial load; it must only be loaded lazily`)
}
if (failures.length > 0) {
  for (const failure of failures) console.error(`FAIL: ${failure}`)
  process.exit(1)
}
console.log('OK')
