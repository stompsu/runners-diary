/** DOM elements for MapLibre markers. They carry shape and icon as well as colour (colour is never the only signal). */

const SVG_NS = 'http://www.w3.org/2000/svg'

function icon(path: string, fill: boolean): SVGSVGElement {
  const svg = document.createElementNS(SVG_NS, 'svg')
  svg.setAttribute('viewBox', '0 0 24 24')
  svg.setAttribute('width', '18')
  svg.setAttribute('height', '18')
  svg.setAttribute('aria-hidden', 'true')
  const shape = document.createElementNS(SVG_NS, 'path')
  shape.setAttribute('d', path)
  shape.setAttribute('fill', fill ? '#fff' : 'none')
  shape.setAttribute('stroke', '#fff')
  shape.setAttribute('stroke-width', '2.5')
  shape.setAttribute('stroke-linecap', 'round')
  shape.setAttribute('stroke-linejoin', 'round')
  svg.append(shape)
  return svg
}

/** Start = green circle with a play triangle; finish = dark square with a flag. */
export function createEndpointElement(kind: 'start' | 'finish', label: string): HTMLElement {
  const el = document.createElement('div')
  el.setAttribute('role', 'img')
  el.setAttribute('aria-label', label)
  el.dataset.testid = `map-${kind}`
  el.style.cssText = [
    'width:28px',
    'height:28px',
    'display:flex',
    'align-items:center',
    'justify-content:center',
    'border:2px solid #fff',
    'box-shadow:0 1px 4px rgba(0,0,0,.45)',
    kind === 'start' ? 'background:#15803d;border-radius:50%' : 'background:#1f2937;border-radius:6px',
  ].join(';')
  el.append(kind === 'start' ? icon('M8 5.5 L19 12 L8 18.5 Z', true) : icon('M7 4v16M7 5h11l-2.5 4 2.5 4H7', false))
  return el
}

/** A race location: a red round pin with a flag; the caller supplies the translated label. */
export function createRaceElement(label: string): HTMLElement {
  const el = document.createElement('div')
  el.setAttribute('role', 'img')
  el.setAttribute('aria-label', label)
  el.dataset.testid = 'map-marker'
  el.dataset.kind = 'race'
  el.style.cssText = [
    'width:32px',
    'height:32px',
    'display:flex',
    'align-items:center',
    'justify-content:center',
    'border:2px solid #fff',
    'border-radius:50% 50% 50% 0',
    'transform:rotate(-45deg)',
    'background:#b91c1c',
    'box-shadow:0 1px 4px rgba(0,0,0,.5)',
  ].join(';')
  const flag = icon('M7 4v16M7 5h11l-2.5 4 2.5 4H7', false)
  flag.style.transform = 'rotate(45deg)'
  el.append(flag)
  return el
}

/** The moving marker driven by the elevation profile: a white ring around a blue dot. */
export function createHighlightElement(label: string): HTMLElement {
  const el = document.createElement('div')
  el.setAttribute('role', 'img')
  el.setAttribute('aria-label', label)
  el.dataset.testid = 'map-highlight'
  el.style.cssText = [
    'width:18px',
    'height:18px',
    'border-radius:50%',
    'background:#2563eb',
    'border:3px solid #fff',
    'box-shadow:0 0 0 2px #2563eb,0 1px 4px rgba(0,0,0,.5)',
  ].join(';')
  return el
}
