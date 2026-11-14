import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { raceConflict } from './raceProblems'
import { useSetRaceProposalState } from './raceQueries'
import type { SettableProposalState } from './raceTypes'
import '@/i18n/lazyBundles'

/** Sets Interested / Dismissed / None on a race; failures (including the 2,000 / 200 marks limit) are toasts. */
export function useRaceStateChange() {
  const { t } = useTranslation('races')
  const mutation = useSetRaceProposalState()

  async function change(raceId: string, state: SettableProposalState): Promise<boolean> {
    try {
      await mutation.mutateAsync({ params: { path: { raceId } }, body: { state } })
      return true
    } catch (error) {
      toast.error(
        raceConflict(error)?.kind === 'stateLimit' ? t('proposals.toast.stateLimit') : t('proposals.toast.stateFailed'),
      )
      return false
    }
  }

  return { change, pending: mutation.isPending }
}
