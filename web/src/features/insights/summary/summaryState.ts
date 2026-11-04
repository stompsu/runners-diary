import type { SummaryResponse } from '../insightsTypes'

/** Whether the card offers a button for this response at all (template notes and unconfigured environments do not). */
export function hasSummaryAction({ generation, summary }: SummaryResponse): boolean {
  if (generation.availability !== 'available' && generation.availability !== 'quotaExhausted') return false
  return summary?.source !== 'template'
}
