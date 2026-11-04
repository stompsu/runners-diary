import type { SummaryValue } from '../insightsTypes'

export type ValueFormatter = (key: string, value: SummaryValue) => string | null

const MISSING = '—'
const TOKEN = /\{([^{}]*)\}/g

/**
 * Turns the server text with `{key}` placeholders into plain text (ADR 0026 §2): every token is replaced by the value
 * formatted by the client. A token without a value (unknown key, value that cannot be formatted) renders "—",
 * and no brace ever reaches the screen. The result is meant for a React text node and is never parsed as markup.
 */
export function renderSummaryText(
  text: string,
  values: Readonly<Record<string, SummaryValue>>,
  format: ValueFormatter,
) {
  let result = ''
  let last = 0
  for (const match of text.matchAll(TOKEN)) {
    const key = match[1] ?? ''
    result += stripBraces(text.slice(last, match.index))
    // `hasOwn`: keys like "constructor" or "__proto__" must not resolve through the prototype chain.
    const value = Object.hasOwn(values, key) ? values[key] : undefined
    result += (value ? format(key, value) : null) ?? MISSING
    last = match.index + match[0].length
  }
  result += stripBraces(text.slice(last))
  // Stripped braces can leave a double space.
  return result.replace(/\s{2,}/g, ' ').trim()
}

function stripBraces(part: string) {
  return part.replace(/[{}]/g, '')
}
