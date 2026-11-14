import { useQueryClient } from '@tanstack/react-query'
import { Flag } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { toast } from 'sonner'
import { $api } from '@/api/client'
import { Button } from '@/components/ui/button'
import { useProposalsGate, useUpdateRaceInterests } from '@/features/races/raceQueries'
import type { RaceInterestsDto, UpdateRaceInterestsRequest } from '@/features/races/raceTypes'
import { cn } from '@/lib/utils'

const interestsKey = $api.queryOptions('get', '/api/races/interests', {}).queryKey

function writeRequest(interests: RaceInterestsDto, showInCalendar: boolean): UpdateRaceInterestsRequest {
  return {
    kinds: interests.kinds,
    regionCodes: interests.regionCodes,
    center: interests.center,
    radiusMeters: interests.radiusMeters,
    minDistanceMeters: interests.minDistanceMeters,
    maxDistanceMeters: interests.maxDistanceMeters,
    horizonDays: interests.horizonDays,
    keywords: interests.keywords,
    showInCalendar,
  }
}

/**
 * Month header control for the proposals layer (ADR 0037 §4): a pressed/unpressed toggle that saves `showInCalendar`
 * at once. The cache is updated first (the markers appear or go immediately) and restored when the save fails.
 * Without a saved profile it is a link to the settings page instead; with the feature off it does not render.
 */
export function ProposalsToggle() {
  const { t } = useTranslation('calendar')
  const queryClient = useQueryClient()
  const { racesEnabled, interests, configured, shown } = useProposalsGate()
  const update = useUpdateRaceInterests()
  const className = 'h-11 sm:h-9'

  const current = interests.data
  if (!racesEnabled || !current) return null

  if (!configured) {
    return (
      <Button asChild variant="outline" className={className}>
        <Link to="/settings/races">
          <Flag aria-hidden="true" />
          {t('proposalsToggle.setup')}
        </Link>
      </Button>
    )
  }

  async function toggle(before: RaceInterestsDto) {
    const next = !before.showInCalendar
    await queryClient.cancelQueries({ queryKey: interestsKey })
    queryClient.setQueryData(interestsKey, { ...before, showInCalendar: next })
    update.mutate(
      { body: writeRequest(before, next) },
      {
        onError: () => {
          queryClient.setQueryData(interestsKey, before)
          toast.error(t('proposalsToggle.failed'))
        },
      },
    )
  }

  return (
    <Button
      type="button"
      variant="outline"
      aria-pressed={shown}
      data-testid="proposals-toggle"
      className={cn(className, shown && 'border-foreground')}
      onClick={() => void toggle(current)}
    >
      <Flag aria-hidden="true" className={shown ? 'fill-current' : undefined} />
      {t('proposalsToggle.label')}
    </Button>
  )
}
