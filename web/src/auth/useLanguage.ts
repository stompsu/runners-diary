import { useQueryClient } from '@tanstack/react-query'
import { useEffect, useRef } from 'react'
import { $api } from '@/api/client'
import { toast } from 'sonner'
import { useTranslation } from 'react-i18next'
import {
  applyProfileLanguage,
  hasExplicitLanguageChoice,
  markExplicitLanguageChoice,
  setLanguage,
  type Language,
} from '@/i18n/language'
import { useMe } from './useMe'
import { useSession } from './session'

/**
 * An explicit language choice: applied locally first (no reload, `<html lang>`, `rd.lang`), then saved to the profile
 * when there is a session (ADR 0042 §3). The save is fire-and-forget: a failure keeps the local choice.
 */
export function useChangeLanguage(): (language: Language) => Promise<void> {
  const session = useSession()
  const queryClient = useQueryClient()
  const { mutate } = $api.useMutation('put', '/api/me/language')
  const { t } = useTranslation()
  const signedIn = session.kind === 'guest' || session.kind === 'entra'

  return async (language) => {
    // Marked before loading: a slower /api/me that returns meanwhile must not apply an older profile value.
    markExplicitLanguageChoice(true)
    try {
      await setLanguage(language)
    } catch {
      markExplicitLanguageChoice(false)
      toast.error(t('language.failed'))
      return
    }
    if (!signedIn) return
    // A failed save keeps the local choice (it is the user's latest); the next page load reads the profile again.
    mutate(
      { body: { language } },
      {
        onSuccess: () => queryClient.invalidateQueries({ queryKey: ['get', '/api/me'] }),
        onError: () => undefined,
      },
    )
  }
}

/**
 * The profile language wins over `rd.lang`, once per account and page load: a later explicit switch must not be undone
 * by a refetch of `/api/me` (for example when saving it failed). Null (never chosen) changes nothing.
 */
export function useLanguageSync(): void {
  const { data: me } = useMe()
  const appliedFor = useRef<string | null>(null)

  useEffect(() => {
    if (!me || appliedFor.current === me.id) return
    appliedFor.current = me.id
    if (hasExplicitLanguageChoice()) return
    void applyProfileLanguage(me.language)
  }, [me])
}
