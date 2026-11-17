import i18n from '@/i18n'

/**
 * Server 400 field messages are English (ADR 0042 §7). In any other UI language the form shows this localised generic
 * message instead; null in English, where the server text is shown as before.
 */
export function genericFieldMessage(): string | undefined {
  return i18n.language === 'en' ? undefined : i18n.t('problem.fieldInvalid', { ns: 'errors' })
}
