import { useTranslation } from 'react-i18next'

/**
 * Turns a stored field error into text: our own zod messages are `validation.*` i18n keys; anything else is a
 * server message (fixed English text from the API) and is shown as it is (ADR 0018 §3).
 */
export function useFieldMessage() {
  const { t } = useTranslation('runs')
  return (message: string | undefined): string | undefined => {
    if (!message) return undefined
    return message.startsWith('validation.') ? t(message as 'validation.distance') : message
  }
}
