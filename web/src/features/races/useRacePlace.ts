import { useTranslation } from 'react-i18next'
import { isPolishRegionCode } from './polishRegions'
import type { RaceProposal } from './raceTypes'
import '@/i18n/lazyBundles'

/** "Gliwice, Silesian": the locality as the source wrote it (Polish) and the voivodeship name in the UI language. */
export function useRacePlace(): (race: Pick<RaceProposal, 'locationName' | 'regionCode'>) => string {
  const { t } = useTranslation('races')
  return (race) =>
    [race.locationName, isPolishRegionCode(race.regionCode) ? t(`regions.${race.regionCode}.en`) : null]
      .filter(Boolean)
      .join(', ')
}
