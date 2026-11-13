import type { RacesClientConfig } from './raceTypes'

/** Race distances for a row: "10 / 21.1 km" (ascending as sent; at most one decimal, so 21.0975 m reads 21.1). */
export function formatRaceDistances(distancesMeters: readonly number[], locale: string): string {
  if (distancesMeters.length === 0) return ''
  const number = new Intl.NumberFormat(locale, { minimumFractionDigits: 0, maximumFractionDigits: 1 })
  return `${distancesMeters.map((meters) => number.format(meters / 1000)).join(' / ')} km`
}

/** "PLN 60", "PLN 59.90"; an unknown currency code falls back to "60 XYZ" instead of throwing. */
export function formatRacePrice(amount: number, currency: string, locale: string): string {
  try {
    return new Intl.NumberFormat(locale, {
      style: 'currency',
      currency,
      currencyDisplay: 'code',
      minimumFractionDigits: Number.isInteger(amount) ? 0 : 2,
      maximumFractionDigits: 2,
    })
      .format(amount)
      .replace(/ /g, ' ')
  } catch {
    return `${amount} ${currency}`
  }
}

/** The display name of a source id; an id the client config does not know (a newer server) is shown as it is. */
export function sourceName(sources: RacesClientConfig['sources'] | undefined, id: string): string {
  return sources?.find((source) => source.id === id)?.name ?? id
}
