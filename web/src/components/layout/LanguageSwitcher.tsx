import { Globe } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { NativeSelect, NativeSelectOption } from '@/components/ui/native-select'
import { useChangeLanguage } from '@/auth/useLanguage'
import { releasedLanguages, toLanguage } from '@/i18n/language'

/**
 * The language switcher (ADR 0042 §5): a native select, so keyboard and screen readers work without extra code. Each
 * option is named in its own language, with `lang` set on it. Renders nothing while only one language is released.
 *
 * Below `sm` the closed select shows the language code ("EN", "PL") instead of the name: the select's own text is made
 * transparent and an `aria-hidden` code sits on top. The accessible name, the option names and the open list stay
 * full; in forced-colors mode (where transparency is overridden) the full name is shown and the code is hidden.
 */
export function LanguageSwitcher() {
  const { t, i18n } = useTranslation()
  const changeLanguage = useChangeLanguage()
  const languages = releasedLanguages()
  if (languages.length < 2) return null

  return (
    <div className="flex items-center gap-1.5">
      <Globe className="size-4 shrink-0 text-muted-foreground" aria-hidden="true" />
      <div className="relative">
        <NativeSelect
          size="sm"
          aria-label={t('language.label')}
          value={toLanguage(i18n.language) ?? 'en'}
          onChange={(event) => {
            const language = toLanguage(event.target.value)
            if (language) void changeLanguage(language)
          }}
          className="min-h-9 w-auto max-sm:min-h-11 max-sm:w-[4.75rem] max-sm:text-transparent max-sm:forced-colors:w-auto max-sm:forced-colors:text-[CanvasText]"
        >
          {languages.map((language) => (
            <NativeSelectOption key={language} value={language} lang={language}>
              {t(`language.names.${language}`)}
            </NativeSelectOption>
          ))}
        </NativeSelect>
        <span
          aria-hidden="true"
          className="pointer-events-none absolute top-1/2 left-3 -translate-y-1/2 text-sm sm:hidden forced-colors:hidden"
        >
          {(toLanguage(i18n.language) ?? 'en').toUpperCase()}
        </span>
      </div>
    </div>
  )
}
