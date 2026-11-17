import { Globe } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { NativeSelect, NativeSelectOption } from '@/components/ui/native-select'
import { useChangeLanguage } from '@/auth/useLanguage'
import { releasedLanguages, toLanguage } from '@/i18n/language'

/**
 * The language switcher (ADR 0042 §5): a native select, so keyboard and screen readers work without extra code. Each
 * option is named in its own language, with `lang` set on it. Renders nothing while only one language is released.
 */
export function LanguageSwitcher() {
  const { t, i18n } = useTranslation()
  const changeLanguage = useChangeLanguage()
  const languages = releasedLanguages()
  if (languages.length < 2) return null

  return (
    <div className="flex items-center gap-1.5">
      <Globe className="size-4 shrink-0 text-muted-foreground" aria-hidden="true" />
      <NativeSelect
        size="sm"
        aria-label={t('language.label')}
        value={toLanguage(i18n.language) ?? 'en'}
        onChange={(event) => {
          const language = toLanguage(event.target.value)
          if (language) void changeLanguage(language)
        }}
        className="min-h-9 w-auto"
      >
        {languages.map((language) => (
          <NativeSelectOption key={language} value={language} lang={language}>
            {t(`language.names.${language}`)}
          </NativeSelectOption>
        ))}
      </NativeSelect>
    </div>
  )
}
